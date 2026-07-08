using UdonSharp;
using UnityEngine;
using VRC.SDK3.Rendering;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

[DefaultExecutionOrder(2)]
public class GravitySimulator : UdonSharpBehaviour
{
    [Header("CRTs Group A")] public CustomRenderTexture posMassA;
    public CustomRenderTexture velMiscA;
    public CustomRenderTexture eventDataA;

    [Header("CRTs Group B")] public CustomRenderTexture posMassB;
    public CustomRenderTexture velMiscB;
    public CustomRenderTexture eventDataB;


    [Header("UI Controller")] public CtrlPanel ctrlPanel;

    [Header("Simulation Settings")] public float innerDensity = 2.7f;
    public float outerDensity = 1.0f;
    public float innerRatio = 0.5f;

    [Tooltip("Bodies below this mass directly merge on outer radii overlap.")]
    public float minInteractMass = 2.0f;

    public float fadeStartDistance = 250.0f;

    [Header("States")] public bool isPaused = false;
    public bool isDebug = false;

    private int _currentPhase = 1;
    private int _currentBatch = 0;
    private int _cycleCount = 0;
    private int _actualBatchCount = 1;
    private int _slowFrameCount = 0;
    private bool _posMassIsA = true;
    private bool _velMiscIsA = true;

    private float _timeSinceLastUpdate = 0f;
    private float _previousTickTime = 0.05f;
    private uint _frameCount = 0;
    private float _averageDeltaTime = 0.02f;
    private bool _hasRunInitialStep = false;

    // Recenter States
    private int _readBackState = 0; // 0: Idle, 1: Waiting Pos, 2: Waiting Vel, 3: Apply Offset, 4: Reset Offset
    private int _readBackTarget = 0; // 0: Empty, 1: recenter, 2: snapshot, 3: post recenter, 4: recenter clear up
    private Color[] _posData;
    private Color[] _velData;

    // Snapshot States
    public SyncManager syncManager;
    private int _snapshotActiveCount = 0;
    private Color[] _snapshotPosBuffer = new Color[65536];
    private Color[] _snapshotVelBuffer = new Color[65536];

    // Cached Property IDs
    private int _idGravitationalConstant,
        _idInnerDensity,
        _idOuterDensity,
        _idInnerRatio,
        _idDestroyRadius,
        _idSpawnRadius,
        _idFadeStartDistance;

    private int _idDeltaTime, _idSimSpeed, _idMaxStep, _idFrame, _idCycle;
    private int _idPosMass, _idVelMisc, _idEventData, _idStartId, _idEndId;
    private int _idFragmentSizeRange;
    private int _idInitialBodySizeRange;


    private int _idPosMassPrev, _idInterpolationRatio, _idMaxBodies;
    private int _idMinInteractMass;
    private int _idRandomSeed;


    private int _idPosMassNext, _idEventDataNext;
    private int _idApplyOffset, _idPosOffset, _idVelOffset;

    void Start()
    {
        ctrlPanel.InitCtrlPanel();
        // Parameters
        _idGravitationalConstant = VRCShader.PropertyToID("_Udon_GravitationalConstant");
        _idInnerDensity = VRCShader.PropertyToID("_Udon_InnerDensity");
        _idOuterDensity = VRCShader.PropertyToID("_Udon_OuterDensity");
        _idInnerRatio = VRCShader.PropertyToID("_Udon_InnerRatio");
        _idDestroyRadius = VRCShader.PropertyToID("_Udon_DestroyRadius");
        _idSpawnRadius = VRCShader.PropertyToID("_Udon_SpawnRadius");
        _idFadeStartDistance = VRCShader.PropertyToID("_Udon_FadeStartDistance");


        _idDeltaTime = VRCShader.PropertyToID("_Udon_DeltaTime");
        _idSimSpeed = VRCShader.PropertyToID("_Udon_SimSpeed");
        _idMaxStep = VRCShader.PropertyToID("_Udon_MaxStep");
        _idFrame = VRCShader.PropertyToID("_Udon_Frame");
        _idCycle = VRCShader.PropertyToID("_Udon_Cycle");
        _idMinInteractMass = VRCShader.PropertyToID("_Udon_MinInteractMass");

        _idStartId = VRCShader.PropertyToID("_Udon_StartID");
        _idEndId = VRCShader.PropertyToID("_Udon_EndID");

        _idFragmentSizeRange = VRCShader.PropertyToID("_Udon_FragmentSizeRange");
        _idInitialBodySizeRange = VRCShader.PropertyToID("_Udon_InitialBodySizeRange");


        _idInterpolationRatio = VRCShader.PropertyToID("_Udon_InterpolationRatio");
        _idMaxBodies = VRCShader.PropertyToID("_Udon_MaxBodies");
        _idRandomSeed = VRCShader.PropertyToID("_Udon_RandomSeed");

        _idApplyOffset = VRCShader.PropertyToID("_Udon_ApplyOffset");
        _idPosOffset = VRCShader.PropertyToID("_Udon_PosOffset");
        _idVelOffset = VRCShader.PropertyToID("_Udon_VelOffset");

        // Textures
        _idPosMass = VRCShader.PropertyToID("_Udon_PosMass");
        _idVelMisc = VRCShader.PropertyToID("_Udon_VelMisc");
        _idEventData = VRCShader.PropertyToID("_Udon_EventData");

        _idPosMassNext = VRCShader.PropertyToID("_Udon_PosMass_Next");
        _idEventDataNext = VRCShader.PropertyToID("_Udon_EventData_Next");


        _idPosMassPrev = VRCShader.PropertyToID("_Udon_PosMass_Prev");

        _posData = new Color[65536];
        _velData = new Color[65536];
        InitializeShaderGlobals();
        ResetSimulation();
        isPaused = true;
        _actualBatchCount = ctrlPanel.activeBatchCount <= 0 ? 1 : ctrlPanel.activeBatchCount;
    }

    private void InitializeShaderGlobals()
    {
        VRCShader.SetGlobalFloat(_idInnerDensity, innerDensity);
        VRCShader.SetGlobalFloat(_idOuterDensity, outerDensity);
        VRCShader.SetGlobalFloat(_idInnerRatio, innerRatio);
        VRCShader.SetGlobalFloat(_idFadeStartDistance, fadeStartDistance);
    }

    public void ResetSimulation()
    {
        VRCShader.SetGlobalFloat(_idMaxBodies, ctrlPanel.activeMaxBodies);
        VRCShader.SetGlobalFloat(_idGravitationalConstant, ctrlPanel.activeGravConst);
        VRCShader.SetGlobalFloat(_idDestroyRadius, ctrlPanel.activeDestroyRadius);
        VRCShader.SetGlobalFloat(_idSpawnRadius, ctrlPanel.activeSpawnRadius);
        VRCShader.SetGlobalVector(_idFragmentSizeRange, ctrlPanel.activeFragMass);
        VRCShader.SetGlobalVector(_idInitialBodySizeRange, ctrlPanel.activeInitMass);
        VRCShader.SetGlobalFloat(_idRandomSeed, Random.value * 1000000f);
        posMassA.Initialize();
        posMassB.Initialize();
        velMiscA.Initialize();
        velMiscB.Initialize();
        eventDataA.Initialize();
        eventDataB.Initialize();

        _currentPhase = 1;
        _currentBatch = 0;
        _cycleCount = 0;
        _timeSinceLastUpdate = 0;
        _frameCount = 0;
        _readBackState = 0;
        _slowFrameCount = 0;
        VRCShader.SetGlobalFloat(_idApplyOffset, 0.0f);


        _posMassIsA = true;
        _velMiscIsA = true;

        CustomRenderTexture currPosMass = _posMassIsA ? posMassA : posMassB;

        VRCShader.SetGlobalTexture(_idPosMass, currPosMass);
        VRCShader.SetGlobalTexture(_idPosMassPrev, currPosMass);

        CustomRenderTexture currEventData = _posMassIsA ? eventDataA : eventDataB;
        VRCShader.SetGlobalTexture(_idEventData, currEventData);

        VRCShader.SetGlobalFloat(_idInterpolationRatio, 1.0f);
    }

    public void StartRecenter()
    {
        if (!isPaused || _readBackState != 0) return;
        _readBackState = 1;
        _readBackTarget = 1;
        StartReadBack();
    }

    public void StartSnapshot()
    {
        if (_readBackState != 0) return;
        isPaused = true;
        _readBackState = 1;
        _readBackTarget = 2;
        StartReadBack();
    }

    private void StartReadBack()
    {
        CustomRenderTexture currPosMass = _posMassIsA ? posMassA : posMassB;
        VRCAsyncGPUReadback.Request(currPosMass, 0, TextureFormat.RGBAFloat, (IUdonEventReceiver)this);
    }

    public override void OnAsyncGpuReadbackComplete(VRCAsyncGPUReadbackRequest request)
    {
        if (request.hasError)
        {
            _readBackState = 0;
            _readBackTarget = 0;
            return;
        }

        if (_readBackState == 1)
        {
            if (!request.TryGetData(_posData))
            {
                _readBackState = 0;
                _readBackTarget = 0;
                return;
            }

            _readBackState = 2;
            CustomRenderTexture currVelMisc = _velMiscIsA ? velMiscA : velMiscB;
            VRCAsyncGPUReadback.Request(currVelMisc, 0, TextureFormat.RGBAFloat, (IUdonEventReceiver)this);
        }
        else if (_readBackState == 2)
        {
            if (!request.TryGetData(_velData))
            {
                _readBackState = 0;
                _readBackTarget = 0;
                return;
            }

            _readBackState = 0;
            if (_readBackTarget == 1) ProcessRecenter();
            else if (_readBackTarget == 2) ProcessSnapshot();
        }
    }

    private void ProcessRecenter()
    {
        double totalMass = 0;
        double cx = 0, cy = 0, cz = 0;
        double mx = 0, my = 0, mz = 0;

        int currentMaxBodies = ctrlPanel.activeMaxBodies;
        for (int i = 0; i < currentMaxBodies; i++)
        {
            float mass = _posData[i].a;
            if (mass > 0)
            {
                totalMass += mass;
                cx += _posData[i].r * mass;
                cy += _posData[i].g * mass;
                cz += _posData[i].b * mass;
                mx += _velData[i].r * mass;
                my += _velData[i].g * mass;
                mz += _velData[i].b * mass;
            }
        }

        if (totalMass > 0.001)
        {
            Vector3 posOffset = new Vector3((float)(cx / totalMass), (float)(cy / totalMass), (float)(cz / totalMass));
            Vector3 velOffset = new Vector3((float)(mx / totalMass), (float)(my / totalMass), (float)(mz / totalMass));

            VRCShader.SetGlobalVector(_idPosOffset, posOffset);
            VRCShader.SetGlobalVector(_idVelOffset, velOffset);

            _readBackTarget = 3;
        }
        else
        {
            _readBackTarget = 0;
        }
    }

    private void ProcessSnapshot()
    {
        int currentMaxBodies = ctrlPanel.activeMaxBodies;

        for (int i = 0; i < currentMaxBodies; i++)
        {
            if (_posData[i].a > 0)
            {
                _snapshotPosBuffer[_snapshotActiveCount] = _posData[i];
                _snapshotVelBuffer[_snapshotActiveCount] = _velData[i];
                _snapshotActiveCount++;
            }
        }

        syncManager.OnSnapshotComplete(_snapshotActiveCount, _snapshotPosBuffer, _snapshotVelBuffer);
        _readBackTarget = 0;
    }

    public void ApplyDownloadedSnapshot(int bodyCount, Color[] posBuffer, Color[] velBuffer)
    {
        // isPaused = true;
        // _hasRunInitialStep = false;
        //
        // Texture2D posTex = new Texture2D(256, 256, TextureFormat.RGBAFloat, false);
        // Texture2D velTex = new Texture2D(256, 256, TextureFormat.RGBAFloat, false);
        //
        // Color[] fullPos = new Color[65536];
        // Color[] fullVel = new Color[65536];
        //
        // for (int i = 0; i < bodyCount; i++)
        // {
        //     fullPos[i] = posBuffer[i];
        //     fullVel[i] = velBuffer[i];
        // }
        //
        // posTex.SetPixels(fullPos);
        // posTex.Apply();
        // velTex.SetPixels(fullVel);
        // velTex.Apply();
        //
        // VRCGraphics.Blit(posTex, posMassA);
        // VRCGraphics.Blit(posTex, posMassB);
        // VRCGraphics.Blit(velTex, velMiscA);
        // VRCGraphics.Blit(velTex, velMiscB);
        //
        // // Clear events
        // Texture2D clearTex = new Texture2D(256, 256, TextureFormat.RGBAFloat, false);
        // Color[] clearColors = new Color[65536];
        // clearTex.SetPixels(clearColors);
        // clearTex.Apply();
        //
        // VRCGraphics.Blit(clearTex, eventDataA);
        // VRCGraphics.Blit(clearTex, eventDataB);
        //
        // // Inform TrailManager to clear if needed, handled by SyncManager
        //
        // Destroy(posTex);
        // Destroy(velTex);
        // Destroy(clearTex);
        //
        // _posMassIsA = true;
        // _velMiscIsA = true;
        //
        // VRCShader.SetGlobalTexture(_idPosMass, posMassA);
        // VRCShader.SetGlobalTexture(_idPosMassPrev, posMassA);
        // VRCShader.SetGlobalTexture(_idVelMisc, velMiscA);
        // VRCShader.SetGlobalTexture(_idEventData, eventDataA);
        //
        // _currentPhase = 1;
        // _currentBatch = 0;
        //
        // syncManager.OnApplySnapshotComplete();
    }

    void Update()
    {
        int currentMaxBodies = ctrlPanel.activeMaxBodies;
        VRCShader.SetGlobalFloat(_idMaxBodies, currentMaxBodies);

        CustomRenderTexture currPosMass = _posMassIsA ? posMassA : posMassB;
        CustomRenderTexture nextPosMass = _posMassIsA ? posMassB : posMassA;
        CustomRenderTexture currVelMisc = _velMiscIsA ? velMiscA : velMiscB;
        CustomRenderTexture nextVelMisc = _velMiscIsA ? velMiscB : velMiscA;

        if (_readBackTarget == 3)
        {
            VRCShader.SetGlobalFloat(_idApplyOffset, 1.0f);

            VRCShader.SetGlobalTexture(_idPosMass, currPosMass);
            VRCShader.SetGlobalTexture(_idVelMisc, currVelMisc);

            nextPosMass.Update();
            nextVelMisc.Update();

            _posMassIsA = !_posMassIsA;
            _velMiscIsA = !_velMiscIsA;

            _readBackTarget = 4;
            return;
        }

        if (_readBackTarget == 4)
        {
            VRCShader.SetGlobalFloat(_idApplyOffset, 0.0f);

            // Force rendering to use the new textures immediately
            CustomRenderTexture renderCurr = _posMassIsA ? posMassA : posMassB;
            VRCShader.SetGlobalTexture(_idPosMass, renderCurr);
            VRCShader.SetGlobalTexture(_idPosMassPrev, renderCurr); // No interpolation across the teleport
            VRCShader.SetGlobalTexture(_idVelMisc, _velMiscIsA ? velMiscA : velMiscB);

            _readBackTarget = 0;
            return;
        }

        if (isPaused && _hasRunInitialStep) return;
        _hasRunInitialStep = true;

        _timeSinceLastUpdate += Time.deltaTime;
        _frameCount++;
        int framesSinceUpdate = _currentPhase == 1 ? _currentBatch + 1 : _actualBatchCount + 1;
        float ratio = (float)framesSinceUpdate / (_actualBatchCount + 1);
        VRCShader.SetGlobalFloat(_idInterpolationRatio, Mathf.Clamp01(ratio));

        AdjustBatchCount();


        CustomRenderTexture currEventData = _posMassIsA ? eventDataA : eventDataB;
        CustomRenderTexture nextEventData = _posMassIsA ? eventDataB : eventDataA;

        // Render Bindings
        VRCShader.SetGlobalTexture(_idPosMass, currPosMass);
        VRCShader.SetGlobalTexture(_idPosMassPrev, nextPosMass);

        VRCShader.SetGlobalTexture(_idVelMisc, currVelMisc);
        VRCShader.SetGlobalTexture(_idEventData, currEventData);

        VRCShader.SetGlobalFloat(_idDeltaTime, _previousTickTime > 0.0001f ? _previousTickTime : 0.02f);
        VRCShader.SetGlobalFloat(_idSimSpeed, ctrlPanel.activeSimSpeed);
        VRCShader.SetGlobalFloat(_idMaxStep, ctrlPanel.activeMaxStep / 1000.0f);
        VRCShader.SetGlobalFloat(_idFrame, _frameCount);
        VRCShader.SetGlobalFloat(_idCycle, _cycleCount);
        VRCShader.SetGlobalFloat(_idMinInteractMass, minInteractMass);

        if (_currentPhase == 1)
        {
            int batchSize = currentMaxBodies / _actualBatchCount;
            int startId = _currentBatch * batchSize;
            int endId = startId + batchSize - 1;
            if (_currentBatch == _actualBatchCount - 1) endId = currentMaxBodies - 1;

            // Update VelMisc
            // Globals for PosMass, VelMisc, EventData are already set above

            VRCShader.SetGlobalFloat(_idStartId, startId);
            VRCShader.SetGlobalFloat(_idEndId, endId);

            nextVelMisc.Update();
            _velMiscIsA = !_velMiscIsA;

            _currentBatch++;
            if (_currentBatch >= _actualBatchCount)
            {
                _currentPhase = 2;
            }
        }
        else if (_currentPhase == 2)
        {
            // Inputs for CRT Updates
            // Globals for PosMass, VelMisc, EventData are already set above
            VRCShader.SetGlobalTexture(_idEventDataNext, nextEventData);
            VRCShader.SetGlobalTexture(_idPosMassNext, nextPosMass);

            // Queue Updates
            nextEventData.Update();
            nextPosMass.Update();

            _posMassIsA = !_posMassIsA;

            _currentPhase = 1;
            _currentBatch = 0;
            _cycleCount++;
            _previousTickTime = _timeSinceLastUpdate;
            _timeSinceLastUpdate = 0;
        }

        if (isDebug)
        {
            isPaused = true;
        }
    }

    private void AdjustBatchCount()
    {
        // 增大平滑系数 (0.2)，让平均帧率能更快响应实际掉帧
        _averageDeltaTime = Mathf.Lerp(_averageDeltaTime, Time.deltaTime, 0.2f);

        int currentBatchCount = ctrlPanel.activeBatchCount;

        // Use average frame rate (delta time) for auto batch count scaling
        if (currentBatchCount <= 0)
        {
            if (_averageDeltaTime > 1.0f / 50.0f)
            {
                _slowFrameCount++;
                if (_slowFrameCount >= 5)
                {
                    _actualBatchCount = Mathf.Clamp(_actualBatchCount + 1, 1, 256);
                    _slowFrameCount = -5;
                }
            }
            else
            {
                _slowFrameCount = 0;
            }
        }
        else
        {
            _actualBatchCount = Mathf.Clamp(currentBatchCount, 1, 64);
            _slowFrameCount = 0;
        }
    }

    public float GetPhysicsStep() => _previousTickTime;
    public int GetCurrentBatch() => _currentBatch;
    public int GetTotalBatches() => _actualBatchCount;
    public string GetCurrentCRT() => _posMassIsA ? "A->B" : "B->A";
}