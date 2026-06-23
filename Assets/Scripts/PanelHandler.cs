using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

/// <summary>
/// Controls the sliding panel UI.
/// Object hierarchy: panelRoot -> { PanelHandle (VRC_Pickup), Canvas }
///
/// Behavior:
///   - When UI is closed and handle is not held: root follows the player.
///   - When UI is open OR handle is held: root is fixed in world space.
///   - Handle is constrained to root-local X axis within ±slideLength/2,
///     and offset on the Z axis by handleZOffset to sit closer to the player.
///   - Hysteresis thresholds: open when handle reaches 70%, close at 30%.
///   - On release the handle snaps (lerps) to the nearest endpoint.
///   - On open the panel is placed in front of the user, facing them, tilted up 19°.
///   - Auto-close applies to both VR and Desktop when player walks too far.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PanelHandler : UdonSharpBehaviour
{
    // ── References ──────────────────────────────
    [Header("References")]
    public Transform panelRoot;
    public Transform handle;
    public VRC_Pickup handlePickup;
    public GameObject canvas;
    public Image panelImage;

    [Header("Sprites")]
    public Sprite pcSprite;
    public Sprite vrSprite;

    // ── Tuning ──────────────────────────────────
    [Header("Slide")]
    public float slideLength = 0.3f;
    public float snapSpeed = 10f;
    [Tooltip("Negative value moves handle toward the player (local -Z).")]
    public float handleZOffset = -0.02f;

    [Header("Panel Open Position")]
    public float panelDistance = 0.7f;
    public float heightOffset = 0.3f;
    public float minPanelHeight = 0.3f;

    [Header("Auto Close")]
    public float maxDistance = 3f;

    [Header("Initial Open")]
    [Tooltip("Delay (seconds) before opening the panel after joining, to allow player height sync.")]
    public float initialOpenDelay = 3f;

    // ── Runtime state ───────────────────────────
    private VRCPlayerApi _localPlayer;
    private bool _isOpen;
    private bool _isInVR;
    private bool _initialized;

    // Cached slide boundaries
    private float _slideMin;
    private float _slideMax;
    private float _openThreshold;   // handle must reach here to open  (70%)
    private float _closeThreshold;  // handle must drop here to close  (30%)

    /// <summary>Whether the panel UI is currently open.</summary>
    public bool IsOpen => _isOpen;

    // ════════════════════════════════════════════
    //  Lifecycle
    // ════════════════════════════════════════════

    private void Start()
    {
        _slideMin = -slideLength / 2f;
        _slideMax =  slideLength / 2f;
        _openThreshold  = _slideMin + slideLength * 0.7f;  // 70% from closed end
        _closeThreshold = _slideMin + slideLength * 0.3f;  // 30% from closed end
    }

    private void Update()
    {
        if (!_initialized)
        {
            _localPlayer = Networking.LocalPlayer;
            if (_localPlayer == null) return;
            Initialize();
            return;
        }

        if (_isInVR) UpdateVR();
        else         UpdateDesktop();
    }

    private void Initialize()
    {
        _isInVR = _localPlayer.IsUserInVR();
        panelImage.sprite = _isInVR ? vrSprite : pcSprite;
        handle.gameObject.SetActive(_isInVR);

        // Start closed; schedule a delayed open to let player height sync
        handle.localPosition = new Vector3(_slideMin, 0f, handleZOffset);
        SetOpen(false);

        SendCustomEventDelayedSeconds(nameof(DelayedInitialOpen), initialOpenDelay);

        _initialized = true;
    }

    /// <summary>
    /// Called after initialOpenDelay seconds to open the panel once player
    /// tracking data (especially height) has stabilised.
    /// </summary>
    public void DelayedInitialOpen()
    {
        handle.localPosition = new Vector3(_slideMax, 0f, handleZOffset);
        SetOpen(true);
    }

    // ════════════════════════════════════════════
    //  VR mode
    // ════════════════════════════════════════════

    private void UpdateVR()
    {
        bool isHeld = handlePickup.IsHeld;

        // 1. Position the handle
        if (isHeld) ConstrainHandle();
        else        SnapHandle();

        // 2. Determine open / close
        UpdateOpenState();

        // 3. Auto-close when too far away
        if (_isOpen) CheckAutoClose();

        // 4. Follow player only when closed AND not held
        if (!_isOpen && !isHeld) FollowPlayer();
    }

    /// <summary>
    /// While held, clamp the handle to root-local X axis within [slideMin, slideMax].
    /// Y is zeroed and Z is set to handleZOffset so the handle cannot leave the rail.
    /// </summary>
    private void ConstrainHandle()
    {
        Vector3 lp = handle.localPosition;
        lp.x = Mathf.Clamp(lp.x, _slideMin, _slideMax);
        lp.y = 0f;
        lp.z = handleZOffset;
        handle.localPosition = lp;
        handle.rotation = panelRoot.rotation;
    }

    /// <summary>
    /// When released, lerp the handle toward the current-state endpoint
    /// (open → slideMax, closed → slideMin).
    /// </summary>
    private void SnapHandle()
    {
        float target = _isOpen ? _slideMax : _slideMin;
        Vector3 lp = handle.localPosition;
        lp.x = Mathf.Lerp(lp.x, target, Time.deltaTime * snapSpeed);
        lp.y = 0f;
        lp.z = handleZOffset;
        handle.localPosition = lp;
        handle.rotation = panelRoot.rotation;
    }

    /// <summary>
    /// Hysteresis check: open when handle exceeds the 70% threshold,
    /// close when it drops below the 30% threshold.
    /// </summary>
    private void UpdateOpenState()
    {
        float x = handle.localPosition.x;
        if (!_isOpen && x > _openThreshold)  SetOpen(true);
        if ( _isOpen && x < _closeThreshold) SetOpen(false);
    }

    // ════════════════════════════════════════════
    //  Desktop mode
    // ════════════════════════════════════════════

    private void UpdateDesktop()
    {
        if (Input.GetKeyDown(KeyCode.Tab)) SetOpen(!_isOpen);

        if (_isOpen) CheckAutoClose();

        // When closed, keep following so the next open appears near the player
        if (!_isOpen) FollowPlayer();
    }

    // ════════════════════════════════════════════
    //  Shared helpers
    // ════════════════════════════════════════════

    /// <summary>
    /// If the player walks too far from the panel, force-close and drop the handle.
    /// Works for both VR and Desktop.
    /// </summary>
    private void CheckAutoClose()
    {
        float dist = Vector3.Distance(_localPlayer.GetPosition(), panelRoot.position);
        if (dist > maxDistance)
        {
            if (_isInVR)
            {
                if (handlePickup.IsHeld) handlePickup.Drop();
                handle.localPosition = new Vector3(_slideMin, 0f, handleZOffset);
            }
            SetOpen(false);
        }
    }

    /// <summary>
    /// Compute the ideal panel pose: directly in front of the player at
    /// head-height minus heightOffset (clamped to minPanelHeight),
    /// facing the player with a 19° upward tilt.
    /// </summary>
    private void GetIdealPanelPose(out Vector3 pos, out Quaternion rot)
    {
        VRCPlayerApi.TrackingData head =
            _localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);

        // Horizontal forward (ignore vertical look angle)
        Vector3 forward = head.rotation * Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
            forward = _localPlayer.GetRotation() * Vector3.forward;
        forward.Normalize();

        // Position: panelDistance in front, height-adjusted
        pos = head.position + forward * panelDistance;
        pos.y = Mathf.Max(head.position.y - heightOffset, minPanelHeight);

        // Rotation: face the player, tilted 19° upward
        rot = Quaternion.LookRotation(forward) * Quaternion.Euler(19f, 0f, 0f);
    }

    /// <summary>
    /// Move the panel root to the ideal pose (in front of the player).
    /// </summary>
    private void FollowPlayer()
    {
        GetIdealPanelPose(out Vector3 pos, out Quaternion rot);
        panelRoot.position = pos;
        panelRoot.rotation = rot;
    }

    /// <summary>
    /// Toggle open/close state.  When opening, the panel root is snapped to
    /// the ideal pose (same calculation as the follow target).
    /// </summary>
    private void SetOpen(bool open)
    {
        _isOpen = open;
        canvas.SetActive(open);
        if (open)
        {
            GetIdealPanelPose(out Vector3 pos, out Quaternion rot);
            panelRoot.position = pos;
            panelRoot.rotation = rot;
        }
    }
}