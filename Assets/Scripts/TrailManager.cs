using UdonSharp;
using UnityEngine;
using VRC.SDK3.Rendering;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

public class TrailManager : UdonSharpBehaviour
{
    public GravitySimulator simulator;
    public CustomRenderTexture trailHistoryA;
    public CustomRenderTexture trailHistoryB;
    public float updateInterval = 0.1f;
    public float trailRecordDistance = 50.0f; // 每次记录的最小距离阈值
    public int processBatchSize = 256; // 每次 Update 处理的天体数量

    private CustomRenderTexture _currTrail;
    private CustomRenderTexture _prevTrail;

    private Texture2D _top64Tex;
    private Color[] _top64Colors;
    private float _timer = 0;
    private bool _isReadingBack = false;
    private bool _isProcessingReadback = false;
    private int _processIndex = 0;
    private int _idTop64IDs;
    private int _idTrailHistory, _idTrailHistoryPrev, _idTrailRecordDistance;
    private int[] _prevTopIDs = new int[64];

    private float[] _readbackData;
    private int[] _topIDs;
    private float[] _topMasses;
    private int[] _newOutputIDs;
    private bool[] _usedNew;
    private bool[] _filledOutput;

    void Start()
    {
        for (int i = 0; i < 64; i++)
        {
            _prevTopIDs[i] = -1;
        }

        _top64Tex = new Texture2D(64, 1, TextureFormat.RFloat, false);
        _top64Tex.filterMode = FilterMode.Point;
        _top64Colors = new Color[64];
        _currTrail = trailHistoryA;
        _prevTrail = trailHistoryB;

        _readbackData = new float[65536 * 4];
        _topIDs = new int[64];
        _topMasses = new float[64];
        _newOutputIDs = new int[64];
        _usedNew = new bool[64];
        _filledOutput = new bool[64];

        _idTop64IDs = VRCShader.PropertyToID("_Udon_Top64IDs");
        _idTrailHistory = VRCShader.PropertyToID("_Udon_TrailHistory");
        _idTrailHistoryPrev = VRCShader.PropertyToID("_Udon_TrailHistory_Prev");
        _idTrailRecordDistance = VRCShader.PropertyToID("_Udon_TrailRecordDistance");

        VRCShader.SetGlobalTexture(_idTop64IDs, _top64Tex);
        VRCShader.SetGlobalFloat(_idTrailRecordDistance, trailRecordDistance);

        if (_currTrail != null) VRCShader.SetGlobalTexture(_idTrailHistory, _currTrail);
        if (_prevTrail != null) VRCShader.SetGlobalTexture(_idTrailHistoryPrev, _prevTrail);
    }

    public void ClearTrails()
    {
        if (trailHistoryA != null) trailHistoryA.Initialize();
        if (trailHistoryB != null) trailHistoryB.Initialize();

        for (int i = 0; i < 64; i++)
        {
            _prevTopIDs[i] = -1;
            _top64Colors[i] = new Color(-1, 0, 0, 0);
        }

        _top64Tex.SetPixels(_top64Colors);
        _top64Tex.Apply();
    }

    void Update()
    {
        //已优化：使用一维float数组直接读取RGBAFloat，按活跃天体数量遍历，并缓存数组查询结果。彻底解决掉帧问题。
        VRCShader.SetGlobalFloat(_idTrailRecordDistance, trailRecordDistance);

        if (simulator.isPaused) return;

        // 每帧自动更新 TrailHistory 的 ping-pong
        CustomRenderTexture temp = _currTrail;
        _currTrail = _prevTrail;
        _prevTrail = temp;

        VRCShader.SetGlobalTexture(_idTrailHistoryPrev, _prevTrail);
        VRCShader.SetGlobalTexture(_idTrailHistory, _currTrail);
        _currTrail.Update();


        if (_isProcessingReadback)
        {
            int activeBodies = simulator.ctrlPanel.activeMaxBodies;
            int endIndex = Mathf.Min(_processIndex + processBatchSize, activeBodies);

            float threshold = _topMasses[63];

            for (int i = _processIndex; i < endIndex; i++)
            {
                float m = _readbackData[i * 4 + 3];
                if (m > threshold)
                {
                    int insertPos = 63;
                    while (insertPos > 0 && m > _topMasses[insertPos - 1])
                    {
                        _topMasses[insertPos] = _topMasses[insertPos - 1];
                        _topIDs[insertPos] = _topIDs[insertPos - 1];
                        insertPos--;
                    }

                    _topMasses[insertPos] = m;
                    _topIDs[insertPos] = i;

                    threshold = _topMasses[63];
                }
            }

            _processIndex = endIndex;

            if (_processIndex >= activeBodies)
            {
                _isProcessingReadback = false;
                FinalizeTop64();
                _isReadingBack = false;
            }
        }
        else
        {
            _timer += Time.deltaTime;
            if (_timer > updateInterval && !_isReadingBack)
            {
                _timer = 0;
                _isReadingBack = true;

                // 总是回读最新的 PosMass 状态
                // 虽然 posMassIsA 在 GravitySimulator 是 private，但我们回读 posMassA
                // 只要它是更新好的就行。不过由于两张图交替，读某一张会导致少许延迟。
                CustomRenderTexture currPosMass = simulator.posMassA;
                VRCAsyncGPUReadback.Request(currPosMass, 0, TextureFormat.RGBAFloat, (IUdonEventReceiver)this);
            }
        }
    }

    public override void OnAsyncGpuReadbackComplete(VRCAsyncGPUReadbackRequest request)
    {
        if (request.hasError)
        {
            _isReadingBack = false;
            return;
        }

        if (!request.TryGetData(_readbackData))
        {
            _isReadingBack = false;
            return;
        }

        for (int i = 0; i < 64; i++)
        {
            _topIDs[i] = -1;
            _topMasses[i] = -1.0f;
            _newOutputIDs[i] = -1;
            _usedNew[i] = false;
            _filledOutput[i] = false;
        }

        _processIndex = 0;
        _isProcessingReadback = true;
    }

    private void FinalizeTop64()
    {
        for (int i = 0; i < 64; i++)
        {
            int id = _topIDs[i];
            int prevIndex = -1;
            for (int j = 0; j < 64; j++)
            {
                if (_prevTopIDs[j] == id && !_filledOutput[j])
                {
                    prevIndex = j;
                    break;
                }
            }

            if (prevIndex != -1)
            {
                _newOutputIDs[prevIndex] = id;
                _filledOutput[prevIndex] = true;
                _usedNew[i] = true;
            }
        }

        int emptySlotIdx = 0;
        for (int i = 0; i < 64; i++)
        {
            if (!_usedNew[i])
            {
                while (emptySlotIdx < 64 && _filledOutput[emptySlotIdx])
                {
                    emptySlotIdx++;
                }

                if (emptySlotIdx < 64)
                {
                    _newOutputIDs[emptySlotIdx] = _topIDs[i];
                    _filledOutput[emptySlotIdx] = true;
                }
            }
        }

        for (int i = 0; i < 64; i++)
        {
            _prevTopIDs[i] = _newOutputIDs[i];
            _top64Colors[i] = new Color(_newOutputIDs[i], 0, 0, 0);
        }

        _top64Tex.SetPixels(_top64Colors);
        _top64Tex.Apply();
    }
}