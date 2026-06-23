using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using Scenes.main_UdonProgramSources;
using TMPro;

public class CtrlPanel : UdonSharpBehaviour
{
    public GravitySimulator simulator;


    [Header("Console Configs")] public int defMaxBodies = 64;
    public float defMaxStep = 50f;
    public float defSimSpeed = 1.0f;
    public int defBatchCount = 0;

    [Header("Console")] public TextSlider maxBodiesSlider;
    public TextSlider maxStepSlider;
    public TextSlider simSpeedSlider;
    public TextSlider batchCountSlider;

    [HideInInspector] public int activeMaxBodies;
    [HideInInspector] public float activeMaxStep;
    [HideInInspector] public float activeSimSpeed;
    [HideInInspector] public int activeBatchCount;

    [Header("Colors Configs")] public float defAvatarLight = 1.0f;
    public float defFlash = 1.0f;
    public float defGlow = 1.0f;
    public Vector2 defHslH = new Vector2(0.0f, 1.0f);
    public Vector2 defHslS = new Vector2(1.0f, 1.0f);
    public Vector2 defHslL = new Vector2(0.5f, 0.5f);

    [Header("Colors")] public CustomRenderTexture colorCRT;
    public float minGlowMass = 10000.0f;
    public TextSlider avatarLightSlider;
    public Light avatarLight;
    public TextSlider flashSlider;
    public TextSlider glowSlider;
    public TextDuoSlider hslHSlider;
    public TextDuoSlider hslSSlider;
    public TextDuoSlider hslLSlider;

    [HideInInspector] public Vector2 activeHslH;
    [HideInInspector] public Vector2 activeHslS;
    [HideInInspector] public Vector2 activeHslL;

    [Header("Settings Configs")] public float defGravConst = 0.5f;
    public float defDestroyRadius = 20000.0f;
    public float defSpawnRadius = 5000.0f;
    public Vector2 defFragMass = new Vector2(5.0f, 15.0f);
    public Vector2 defInitMass = new Vector2(1000.0f, 10000.0f);

    [Header("Settings")] public TextSlider gravConstSlider;
    public TextSlider destroyRadiusSlider;
    public TextSlider spawnRadiusSlider;
    public TextDuoSlider fragMassSlider;
    public TextDuoSlider initMassSlider;

    [HideInInspector] public float activeGravConst;
    [HideInInspector] public float activeDestroyRadius;
    [HideInInspector] public float activeSpawnRadius;
    [HideInInspector] public Vector2 activeFragMass;
    [HideInInspector] public Vector2 activeInitMass;

    [Header("Debug")] public Toggle debugToggle;
    public TextMeshProUGUI debugInfoText;

    private int _idFlashBrightness, _idBodyBrightness, _idMinGlowMass;
    private int _idHslH, _idHslS, _idHslL, _idColor;

    void Start()
    {
        _idFlashBrightness = VRCShader.PropertyToID("_Udon_FlashBrightness");
        _idBodyBrightness = VRCShader.PropertyToID("_Udon_BodyBrightness");
        _idMinGlowMass = VRCShader.PropertyToID("_Udon_MinGlowMass");
        _idHslH = VRCShader.PropertyToID("_Udon_HSL_H");
        _idHslS = VRCShader.PropertyToID("_Udon_HSL_S");
        _idHslL = VRCShader.PropertyToID("_Udon_HSL_L");
        _idColor = VRCShader.PropertyToID("_Udon_Color");

        activeMaxBodies = defMaxBodies * 256;
        activeMaxStep = defMaxStep;
        activeSimSpeed = defSimSpeed;
        activeBatchCount = defBatchCount;

        activeHslH = defHslH;
        activeHslS = defHslS;
        activeHslL = defHslL;

        activeGravConst = defGravConst;
        activeDestroyRadius = defDestroyRadius;
        activeSpawnRadius = defSpawnRadius;
        activeFragMass = defFragMass;
        activeInitMass = defInitMass;

        flashSlider.callbackTarget = this;
        flashSlider.callbackEvent = nameof(OnFlashChanged);
        glowSlider.callbackTarget = this;
        glowSlider.callbackEvent = nameof(OnGlowChanged);
        avatarLightSlider.callbackTarget = this;
        avatarLightSlider.callbackEvent = nameof(OnModelLightChanged);
        maxStepSlider.callbackTarget = this;
        maxStepSlider.callbackEvent = nameof(OnMaxStepChanged);
        simSpeedSlider.callbackTarget = this;
        simSpeedSlider.callbackEvent = nameof(OnSimSpeedChanged);
        batchCountSlider.callbackTarget = this;
        batchCountSlider.callbackEvent = nameof(OnBatchCountChanged);

        maxBodiesSlider.SetValueAndRefresh(defMaxBodies);
        maxStepSlider.SetValueAndRefresh(defMaxStep);
        simSpeedSlider.SetValueAndRefresh(defSimSpeed);
        batchCountSlider.SetValueAndRefresh(defBatchCount);

        avatarLightSlider.SetValueAndRefresh(defAvatarLight);
        flashSlider.SetValueAndRefresh(defFlash);
        glowSlider.SetValueAndRefresh(defGlow);

        hslHSlider.SetValuesAndRefresh(defHslH.x, hslHSlider.sliderB.maxValue - defHslH.y);
        hslSSlider.SetValuesAndRefresh(defHslS.x, hslSSlider.sliderB.maxValue - defHslS.y);
        hslLSlider.SetValuesAndRefresh(defHslL.x, hslLSlider.sliderB.maxValue - defHslL.y);

        gravConstSlider.SetValueAndRefresh(defGravConst);
        destroyRadiusSlider.SetValueAndRefresh(defDestroyRadius);
        spawnRadiusSlider.SetValueAndRefresh(defSpawnRadius);

        fragMassSlider.SetValuesAndRefresh(defFragMass.x, fragMassSlider.sliderB.maxValue - defFragMass.y);
        initMassSlider.SetValuesAndRefresh(defInitMass.x, initMassSlider.sliderB.maxValue - defInitMass.y);

        VRCShader.SetGlobalFloat(_idFlashBrightness, defFlash);
        VRCShader.SetGlobalFloat(_idBodyBrightness, defGlow);
        VRCShader.SetGlobalFloat(_idMinGlowMass, minGlowMass);

        VRCShader.SetGlobalTexture(_idColor, colorCRT);
        PushColors();
        ApplySettings();
    }

    public void OnMaxStepChanged()
    {
        activeMaxStep = maxStepSlider.slider.value;
    }

    public void OnSimSpeedChanged()
    {
        activeSimSpeed = simSpeedSlider.slider.value;
    }

    public void OnBatchCountChanged()
    {
        activeBatchCount = (int)batchCountSlider.slider.value;
    }

    public void OnModelLightChanged()
    {
        avatarLight.intensity = avatarLightSlider.slider.value;
    }

    public void OnFlashChanged()
    {
        VRCShader.SetGlobalFloat(_idFlashBrightness, flashSlider.slider.value);
    }

    public void OnGlowChanged()
    {
        VRCShader.SetGlobalFloat(_idBodyBrightness, glowSlider.slider.value);
    }

    public void PushColors()
    {
        activeHslH = new Vector2(hslHSlider.sliderA.value, hslHSlider.sliderB.maxValue - hslHSlider.sliderB.value);
        activeHslS = new Vector2(hslSSlider.sliderA.value, hslSSlider.sliderB.maxValue - hslSSlider.sliderB.value);
        activeHslL = new Vector2(hslLSlider.sliderA.value, hslLSlider.sliderB.maxValue - hslLSlider.sliderB.value);

        VRCShader.SetGlobalVector(_idHslH, activeHslH);
        VRCShader.SetGlobalVector(_idHslS, activeHslS);
        VRCShader.SetGlobalVector(_idHslL, activeHslL);

        colorCRT.Initialize();
    }

    public void ApplySettings()
    {
        activeMaxBodies = (int)maxBodiesSlider.slider.value * 256;
        activeMaxStep = maxStepSlider.slider.value;
        activeSimSpeed = simSpeedSlider.slider.value;
        activeBatchCount = (int)batchCountSlider.slider.value;

        activeGravConst = gravConstSlider.slider.value;
        activeDestroyRadius = destroyRadiusSlider.slider.value;
        activeSpawnRadius = spawnRadiusSlider.slider.value;

        activeFragMass = new Vector2(fragMassSlider.sliderA.value,
            fragMassSlider.sliderB.maxValue - fragMassSlider.sliderB.value);
        activeInitMass = new Vector2(initMassSlider.sliderA.value,
            initMassSlider.sliderB.maxValue - initMassSlider.sliderB.value);
    }

    public void OnBtnStart()
    {
        simulator.isPaused = false;
        debugToggle.isOn = false;
    }

    public void OnBtnStop()
    {
        simulator.isPaused = true;
    }

    public void OnBtnRecenter()
    {
        simulator.isPaused = true;
        simulator.RecenterAndZeroMomentum();
    }

    public void OnBtnSnapshot()
    {
        simulator.isPaused = true;
    }

    public void OnBtnReset()
    {
        simulator.isPaused = true;
        ApplySettings();
        simulator.ResetSimulation();
    }

    public void OnDebugToggleChanged()
    {
        simulator.isDebug = debugToggle.isOn;
    }

    public void OnBtnStepFrame()
    {
        simulator.isPaused = false;
    }

    void Update()
    {
        debugInfoText.text =
            $"物理步长(ms): {simulator.GetPhysicsStep() * 1000:F2}    \t\t目标CRT: {simulator.GetCurrentCRT()}\n" +
            $"当前批次: {simulator.GetCurrentBatch()}\t\t\t\t总批次数: {simulator.GetTotalBatches()}\n";
    }
}