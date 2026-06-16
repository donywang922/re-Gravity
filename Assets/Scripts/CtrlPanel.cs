using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

public class CtrlPanel : UdonSharpBehaviour
{
    public GravitySimulator simulator;

    [Header("Console Configs")] public int defMaxBodies = 64;
    public float defMaxStep = 50f;
    public float defSimSpeed = 1.0f;
    public int defBatchCount = 0;

    [Header("Console")] public Slider maxBodiesSlider;
    public Slider maxStepSlider;
    public Slider simSpeedSlider;
    public Slider batchCountSlider;

    [HideInInspector] public int activeMaxBodies;
    [HideInInspector] public float activeMaxStep;
    [HideInInspector] public float activeSimSpeed;
    [HideInInspector] public int activeBatchCount;

    [Header("Colors Configs")] public float defModelLight = 1.0f;
    public float defFlash = 1.0f;
    public float defGlow = 1.0f;
    public Vector2 defHslH = new Vector2(0.0f, 1.0f);
    public Vector2 defHslS = new Vector2(1.0f, 1.0f);
    public Vector2 defHslL = new Vector2(0.5f, 0.5f);

    [Header("Colors")] public CustomRenderTexture colorCRT;
    public float minGlowMass = 10000.0f;
    public Slider modelLightSlider;
    public Slider flashSlider;
    public Slider glowSlider;
    public Slider hslHFromSlider;
    public Slider hslHToSlider;
    public Slider hslSFromSlider;
    public Slider hslSToSlider;
    public Slider hslLFromSlider;
    public Slider hslLToSlider;

    [HideInInspector] public Vector2 activeHslH;
    [HideInInspector] public Vector2 activeHslS;
    [HideInInspector] public Vector2 activeHslL;

    [Header("Settings Configs")] public float defGravConst = 0.5f;
    public float defDestroyRadius = 20000.0f;
    public float defSpawnRadius = 5000.0f;
    public Vector2 defFragMass = new Vector2(5.0f, 15.0f);
    public Vector2 defInitMass = new Vector2(1000.0f, 10000.0f);

    [Header("Settings")] public Slider gravConstSlider;
    public Slider destroyRadiusSlider;
    public Slider spawnRadiusSlider;
    public Slider fragMassFromSlider;
    public Slider fragMassToSlider;
    public Slider initMassFromSlider;
    public Slider initMassToSlider;

    [HideInInspector] public float activeGravConst;
    [HideInInspector] public float activeDestroyRadius;
    [HideInInspector] public float activeSpawnRadius;
    [HideInInspector] public Vector2 activeFragMass;
    [HideInInspector] public Vector2 activeInitMass;

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

        maxBodiesSlider.value = defMaxBodies;
        maxStepSlider.value = defMaxStep;
        simSpeedSlider.value = defSimSpeed;
        batchCountSlider.value = defBatchCount;

        modelLightSlider.value = defModelLight;
        flashSlider.value = defFlash;
        glowSlider.value = defGlow;

        hslHFromSlider.value = defHslH.x;
        hslHToSlider.value = hslHToSlider.maxValue - defHslH.y;
        hslSFromSlider.value = defHslS.x;
        hslSToSlider.value = hslSToSlider.maxValue - defHslS.y;
        hslLFromSlider.value = defHslL.x;
        hslLToSlider.value = hslLToSlider.maxValue - defHslL.y;

        gravConstSlider.value = defGravConst;
        destroyRadiusSlider.value = defDestroyRadius;
        spawnRadiusSlider.value = defSpawnRadius;

        fragMassFromSlider.value = defFragMass.x;
        fragMassToSlider.value = fragMassToSlider.maxValue - defFragMass.y;
        initMassFromSlider.value = defInitMass.x;
        initMassToSlider.value = initMassToSlider.maxValue - defInitMass.y;

        VRCShader.SetGlobalFloat(_idFlashBrightness, defFlash);
        VRCShader.SetGlobalFloat(_idBodyBrightness, defGlow);
        VRCShader.SetGlobalFloat(_idMinGlowMass, minGlowMass);

        VRCShader.SetGlobalTexture(_idColor, colorCRT);
        PushColors();
        ApplySettings();
    }

    public void OnModelLightChanged()
    {
    }

    public void OnFlashChanged()
    {
        VRCShader.SetGlobalFloat(_idFlashBrightness, flashSlider.value);
    }

    public void OnGlowChanged()
    {
        VRCShader.SetGlobalFloat(_idBodyBrightness, glowSlider.value);
    }

    public void PushColors()
    {
        activeHslH = new Vector2(hslHFromSlider.value, hslHToSlider.maxValue - hslHToSlider.value);
        activeHslS = new Vector2(hslSFromSlider.value, hslSToSlider.maxValue - hslSToSlider.value);
        activeHslL = new Vector2(hslLFromSlider.value, hslLToSlider.maxValue - hslLToSlider.value);

        VRCShader.SetGlobalVector(_idHslH, activeHslH);
        VRCShader.SetGlobalVector(_idHslS, activeHslS);
        VRCShader.SetGlobalVector(_idHslL, activeHslL);

        colorCRT.Initialize();
    }

    public void ApplySettings()
    {
        activeMaxBodies = (int)maxBodiesSlider.value * 256;
        activeMaxStep = maxStepSlider.value;
        activeSimSpeed = simSpeedSlider.value;
        activeBatchCount = (int)batchCountSlider.value;

        activeGravConst = gravConstSlider.value;
        activeDestroyRadius = destroyRadiusSlider.value;
        activeSpawnRadius = spawnRadiusSlider.value;

        activeFragMass = new Vector2(fragMassFromSlider.value, fragMassToSlider.maxValue - fragMassToSlider.value);
        activeInitMass = new Vector2(initMassFromSlider.value, initMassToSlider.maxValue - initMassToSlider.value);
    }

    public void OnBtnStart()
    {
        simulator.isPaused = false;
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
}