using network;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Rendering;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

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

    [Range(0.05f, 0.75f)]
    [Tooltip("Fraction of the configured body capacity reserved as DEAD slots for fragment recycling.")]
    public float fragmentPoolRatio = 0.25f;

    public float fadeStartDistance = 250.0f;

    [Header("States")] public bool isPaused = false;
    public bool isDebug = false;

    private int _currentPhase = 1;
    private int _currentBatch = 0;
    private int _cycleCount = 0;
    private int _actualBatchCount = 1;
    private int _pendingBatchCount = 1;
    private int _slowFrameCount = 0;
    private bool _posMassIsA = true;
    private bool _velMiscIsA = true;

    private float _timeSinceLastUpdate = 0f;
    private float _previousTickTime = 0.05f;
    private float _physicsTickTime = 0.05f;
    private uint _frameCount = 0;
    private float _averageDeltaTime = 0.02f;
    private bool _hasRunInitialStep = false;

    // Recenter States
    private int _readBackState = 0; // 0: Idle, 1: Waiting Pos, 2: Waiting Vel, 3: Apply Offset, 4: Reset Offset
    private int _readBackTarget = 0; // 0: Empty, 1: recenter, 2: snapshot, 3: post recenter, 4: recenter clear up
    private bool _trailOffsetPending = false;
    private int _trailOffsetWaitFrames = 0;
    private Color[] _posData;
    private Color[] _velData;

    // Snapshot States
    public SyncManager syncManager;
    private int _snapshotActiveCount = 0;
    private bool _snapshotRequested = false;
    private bool _snapshotReadbackNextFrame = false;
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
    private int _idPosMass, _idVelMisc, _idEventData, _idEventMeta, _idStartId, _idEndId;
    private int _idFragmentSizeRange;
    private int _idInitialBodySizeRange;


    private int _idPosMassPrev, _idInterpolationRatio, _idMaxBodies, _idInitialActiveBodies;
    private int _idMinInteractMass;
    private int _idRandomSeed;


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
        _idInitialActiveBodies = VRCShader.PropertyToID("_Udon_InitialActiveBodies");
        _idRandomSeed = VRCShader.PropertyToID("_Udon_RandomSeed");

        _idApplyOffset = VRCShader.PropertyToID("_Udon_ApplyOffset");
        _idPosOffset = VRCShader.PropertyToID("_Udon_PosOffset");
        _idVelOffset = VRCShader.PropertyToID("_Udon_VelOffset");

        // Textures
        _idPosMass = VRCShader.PropertyToID("_Udon_PosMass");
        _idVelMisc = VRCShader.PropertyToID("_Udon_VelMisc");
        _idEventData = VRCShader.PropertyToID("_Udon_EventData");
        _idEventMeta = VRCShader.PropertyToID("_Udon_EventMeta");


        _idPosMassPrev = VRCShader.PropertyToID("_Udon_PosMass_Prev");

        _posData = new Color[65536];
        _velData = new Color[65536];
        InitializeShaderGlobals();
        ResetSimulation();
        isPaused = true;
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
        int maxBodies = Mathf.Clamp(ctrlPanel.activeMaxBodies, 2, 65536);
        float effectivePoolRatio = Mathf.Clamp(fragmentPoolRatio, 0.05f, 0.75f);
        int reservedSlots = Mathf.Clamp(Mathf.RoundToInt(maxBodies * effectivePoolRatio), 1, maxBodies - 1);
        int initialActiveBodies = maxBodies - reservedSlots;

        VRCShader.SetGlobalFloat(_idMaxBodies, maxBodies);
        VRCShader.SetGlobalFloat(_idInitialActiveBodies, initialActiveBodies);
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
        _timeSinceLastUpdate = 0f;
        _previousTickTime = 0.05f;
        _physicsTickTime = 0.05f;
        _averageDeltaTime = 0.02f;
        _frameCount = 0;
        _readBackState = 0;
        _readBackTarget = 0;
        _trailOffsetPending = false;
        _trailOffsetWaitFrames = 0;
        _slowFrameCount = 0;
        _snapshotRequested = false;
        _snapshotReadbackNextFrame = false;
        _hasRunInitialStep = true;
        _actualBatchCount = ctrlPanel.activeBatchCount <= 0
            ? 1
            : Mathf.Clamp(ctrlPanel.activeBatchCount, 1, 64);
        _pendingBatchCount = _actualBatchCount;
        VRCShader.SetGlobalFloat(_idApplyOffset, 0.0f);


        _posMassIsA = true;
        _velMiscIsA = true;

        CustomRenderTexture currPosMass = _posMassIsA ? posMassA : posMassB;

        VRCShader.SetGlobalTexture(_idPosMass, currPosMass);
        VRCShader.SetGlobalTexture(_idPosMassPrev, currPosMass);
        VRCShader.SetGlobalTexture(_idVelMisc, velMiscA);

        VRCShader.SetGlobalTexture(_idEventData, eventDataA);
        VRCShader.SetGlobalTexture(_idEventMeta, eventDataB);

        VRCShader.SetGlobalFloat(_idInterpolationRatio, 1.0f);

        if (syncManager != null && syncManager.trailManager != null)
        {
            syncManager.trailManager.ClearTrails();
        }
    }

    public void StartRecenter()
    {
        if (!isPaused || _readBackState != 0) return;
        _readBackState = 1;
        _readBackTarget = 1;
        StartReadBack();
    }

    public void LogError(string msg)
    {
        Debug.LogError($"[GravitySimulator] {msg}");
    }

    private bool IsBodyActive(int index)
    {
        if (_posData[index].a <= 0f) return false;
        
        float signal = _velData[index].a;
        if (signal == 0f) return true;
        
        byte[] bytes = System.BitConverter.GetBytes(signal);
        uint usig = System.BitConverter.ToUInt32(bytes, 0);
        int type = (int)(usig & 0x7u);
        return type != 6; // EVENT_DEAD = 6
    }

    public void StartSnapshot()
    {
        if (_readBackState != 0 || _snapshotRequested || _snapshotReadbackNextFrame) return;

        // Snapshots are coherent after an odd (respawn) cycle has completed.
        bool isSafeBoundary = isPaused && _currentPhase == 1 && _currentBatch == 0 && (_cycleCount % 2 == 0);
        if (isSafeBoundary)
        {
            BeginSnapshotReadback();
            return;
        }

        _snapshotRequested = true;
        isPaused = false;
    }

    private void BeginSnapshotReadback()
    {
        isPaused = true;
        _snapshotRequested = false;
        _snapshotReadbackNextFrame = false;
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

        int currentMaxBodies = Mathf.Clamp(ctrlPanel.activeMaxBodies, 2, 65536);
        for (int i = 0; i < currentMaxBodies; i++)
        {
            float mass = _posData[i].a;
            if (IsBodyActive(i))
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
        _snapshotActiveCount = 0;
        int currentMaxBodies = Mathf.Clamp(ctrlPanel.activeMaxBodies, 2, 65536);

        for (int i = 0; i < currentMaxBodies; i++)
        {
            if (IsBodyActive(i))
            {
                _snapshotPosBuffer[_snapshotActiveCount] = _posData[i];
                Color stableVelocity = _velData[i];
                stableVelocity.a = 0f;
                _snapshotVelBuffer[_snapshotActiveCount] = stableVelocity;
                _snapshotActiveCount++;
            }
        }

        syncManager.OnSnapshotComplete(_snapshotActiveCount, _snapshotPosBuffer, _snapshotVelBuffer);
        _readBackTarget = 0;
    }

    public void ApplyDownloadedSnapshot(int bodyCount, Color[] posBuffer, Color[] velBuffer)
    {
        isPaused = true;
        int configuredMaxBodies = Mathf.Clamp(ctrlPanel.activeMaxBodies, 2, 65536);
        int availableBodies = 0;
        if (posBuffer != null && velBuffer != null)
        {
            availableBodies = Mathf.Min(posBuffer.Length, velBuffer.Length);
        }
        int safeBodyCount = Mathf.Clamp(bodyCount, 0, Mathf.Min(configuredMaxBodies, availableBodies));
        
        Texture2D posTex = new Texture2D(256, 256, TextureFormat.RGBAFloat, false);
        Texture2D velTex = new Texture2D(256, 256, TextureFormat.RGBAFloat, false);
        
        byte[] deadBytes = System.BitConverter.GetBytes(0x00800006u);
        float deadSignal = System.BitConverter.ToSingle(deadBytes, 0);

        Color[] fullPos = new Color[65536];
        Color[] fullVel = new Color[65536];

        for (int i = 0; i < 65536; i++)
        {
            if (i < safeBodyCount)
            {
                fullPos[i] = posBuffer[i];
                Color stableVelocity = velBuffer[i];
                stableVelocity.a = 0f;
                fullVel[i] = stableVelocity;
            }
            else
            {
                fullPos[i] = new Color(0, 0, 0, 0);
                fullVel[i] = new Color(0, 0, 0, deadSignal);
            }
        }
        
        posTex.SetPixels(fullPos);
        posTex.Apply();
        velTex.SetPixels(fullVel);
        velTex.Apply();
        
        VRCGraphics.Blit(posTex, posMassA);
        VRCGraphics.Blit(posTex, posMassB);
        VRCGraphics.Blit(velTex, velMiscA);
        VRCGraphics.Blit(velTex, velMiscB);
        
        // Clear events
        Texture2D clearTex = new Texture2D(256, 256, TextureFormat.RGBAFloat, false);
        Color[] clearColors = new Color[65536];
        clearTex.SetPixels(clearColors);
        clearTex.Apply();
        
        VRCGraphics.Blit(clearTex, eventDataA);
        VRCGraphics.Blit(clearTex, eventDataB);
        
        // Inform TrailManager to clear if needed, handled by SyncManager
        
        Destroy(posTex);
        Destroy(velTex);
        Destroy(clearTex);
        
        _posMassIsA = true;
        _velMiscIsA = true;
        
        VRCShader.SetGlobalTexture(_idPosMass, posMassA);
        VRCShader.SetGlobalTexture(_idPosMassPrev, posMassA);
        VRCShader.SetGlobalTexture(_idVelMisc, velMiscA);
        VRCShader.SetGlobalTexture(_idEventData, eventDataA);
        VRCShader.SetGlobalTexture(_idEventMeta, eventDataB);
        VRCShader.SetGlobalFloat(_idMaxBodies, configuredMaxBodies);
        VRCShader.SetGlobalFloat(_idInitialActiveBodies, safeBodyCount);
        
        _currentPhase = 1;
        _currentBatch = 0;
        _cycleCount = 0;
        _frameCount = 0;
        _timeSinceLastUpdate = 0f;
        _previousTickTime = 0.05f;
        _physicsTickTime = 0.05f;
        _readBackState = 0;
        _readBackTarget = 0;
        _trailOffsetPending = false;
        _trailOffsetWaitFrames = 0;
        _snapshotRequested = false;
        _snapshotReadbackNextFrame = false;
        _hasRunInitialStep = true;
        _actualBatchCount = ctrlPanel.activeBatchCount <= 0
            ? 1
            : Mathf.Clamp(ctrlPanel.activeBatchCount, 1, 64);
        _pendingBatchCount = _actualBatchCount;
        
        syncManager.OnApplySnapshotComplete();
    }

    void Update()
    {
        int currentMaxBodies = Mathf.Clamp(ctrlPanel.activeMaxBodies, 2, 65536);
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

            // CRT Update is submitted asynchronously. Keep the old textures
            // bound as shader inputs for the rest of this frame; rebinding the
            // destinations here can turn the queued pass into undefined
            // read/write feedback and clear every body's mass channel.
            _trailOffsetPending = false;
            _trailOffsetWaitFrames = -1;
            _readBackTarget = 4;
            return;
        }

        if (_readBackTarget == 4)
        {
            CustomRenderTexture renderCurr = _posMassIsA ? posMassA : posMassB;
            CustomRenderTexture renderVel = _velMiscIsA ? velMiscA : velMiscB;

            // Expose the shifted front buffers before TrailManager runs. This
            // also keeps body rendering coherent while waiting for its update.
            VRCShader.SetGlobalTexture(_idPosMass, renderCurr);
            VRCShader.SetGlobalTexture(_idPosMassPrev, renderCurr);
            VRCShader.SetGlobalTexture(_idVelMisc, renderVel);

            // This is the first frame after submitting the offset passes, so
            // their destinations are now safe to expose. Only now may trail
            // history consume the same coordinate-system shift.
            if (_trailOffsetWaitFrames < 0)
            {
                _trailOffsetPending = syncManager != null && syncManager.trailManager != null;
                _trailOffsetWaitFrames = 0;
                return;
            }

            // TrailManager updates while the simulation is paused and consumes
            // the same offset once. Allow for either Udon Update ordering.
            if (_trailOffsetPending && _trailOffsetWaitFrames < 2)
            {
                _trailOffsetWaitFrames++;
                return;
            }

            _trailOffsetPending = false;
            VRCShader.SetGlobalFloat(_idApplyOffset, 0.0f);

            // Recenter is a teleport. Synchronize both ping-pong sides so the
            // next normal frame cannot interpolate back toward unshifted data.
            VRCGraphics.Blit(renderCurr, nextPosMass);
            VRCGraphics.Blit(renderVel, nextVelMisc);

            _readBackTarget = 0;
            return;
        }

        // CRT writes are queued. Waiting one Unity frame after the safe cycle
        // boundary guarantees that readback sees the completed PosMass update.
        if (_snapshotReadbackNextFrame)
        {
            BeginSnapshotReadback();
            return;
        }

        if (isPaused && _hasRunInitialStep) return;
        _hasRunInitialStep = true;

        _timeSinceLastUpdate += Time.deltaTime;
        _frameCount++;

        AdjustBatchCount();


        // EventData A stores numeric loss; EventData B stores matching metadata.
        if (_currentPhase == 1 && _currentBatch == 0)
        {
            // Freeze the partition for a complete cycle. Mid-cycle changes
            // otherwise overlap or skip body-ID ranges.
            _actualBatchCount = Mathf.Clamp(_pendingBatchCount, 1, currentMaxBodies);
        }

        // Every cycle uses B velocity frames plus one terminal frame. On even
        // cycles that terminal frame captures events and advances positions;
        // the even position pass does not consume the freshly captured events.
        int interpolationFrameCount = _actualBatchCount + 1;
        int interpolationFrame = _currentPhase == 1
            ? _currentBatch + 1
            : _actualBatchCount + 1;
        if (_currentPhase == 1 && _currentBatch == 0)
        {
            // _previousTickTime is the accumulated wall time of one complete
            // batch cycle, including its terminal position pass. Every batch
            // must use this same whole-cycle dt; treating Time.deltaTime as the
            // physics step makes the result depend on the selected batch count.
            _physicsTickTime = Mathf.Max(0.0001f, _previousTickTime);
        }
        float ratio = (float)interpolationFrame / interpolationFrameCount;
        VRCShader.SetGlobalFloat(_idInterpolationRatio, Mathf.Clamp01(ratio));

        // Render Bindings
        VRCShader.SetGlobalTexture(_idPosMass, currPosMass);
        VRCShader.SetGlobalTexture(_idPosMassPrev, nextPosMass);

        VRCShader.SetGlobalTexture(_idVelMisc, currVelMisc);
        VRCShader.SetGlobalTexture(_idEventData, eventDataA);
        VRCShader.SetGlobalTexture(_idEventMeta, eventDataB);

        VRCShader.SetGlobalFloat(_idDeltaTime, _physicsTickTime);
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
            // Capture events after the even interaction cycle and keep that
            // generation unchanged until the following odd respawn cycle ends.
            // Position can be submitted in the same frame because the even
            // position pass does not read EventData/EventMeta.
            if (_cycleCount % 2 == 0)
            {
                eventDataA.Update();
                eventDataB.Update();
            }

            CompletePositionPhase(nextPosMass);
        }

        if (isDebug && !_snapshotRequested)
        {
            isPaused = true;
        }
    }

    private void CompletePositionPhase(CustomRenderTexture nextPosMass)
    {
        nextPosMass.Update();
        _posMassIsA = !_posMassIsA;

        int completedCycle = _cycleCount;
        _currentPhase = 1;
        _currentBatch = 0;
        _cycleCount++;
        _previousTickTime = _timeSinceLastUpdate;
        _timeSinceLastUpdate = 0f;

        if (_snapshotRequested && (completedCycle % 2 == 1))
        {
            _snapshotRequested = false;
            _snapshotReadbackNextFrame = true;
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
                    _pendingBatchCount = Mathf.Clamp(_pendingBatchCount + 1, 1, 256);
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
            _pendingBatchCount = Mathf.Clamp(currentBatchCount, 1, 64);
            _slowFrameCount = 0;
        }
    }

    public float GetPhysicsStep() => Mathf.Min(
        _physicsTickTime * ctrlPanel.activeSimSpeed,
        ctrlPanel.activeMaxStep / 1000.0f);
    public int GetCurrentBatch() => _currentBatch;
    public int GetTotalBatches() => _actualBatchCount;
    public string GetCurrentCRT() => _posMassIsA ? "A->B" : "B->A";
    public CustomRenderTexture GetCurrentPosMass() => _posMassIsA ? posMassA : posMassB;

    public bool ConsumeRecenterTrailOffset()
    {
        if (!_trailOffsetPending) return false;
        _trailOffsetPending = false;
        return true;
    }
}
