using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon.Common;

/// <summary>
/// Controls the sliding panel UI.
/// Behavior:
///   - PC: Press Tab to toggle.
///   - VR: Double click trigger to toggle.
///   - VR: Hold grip (fist) and swipe right to open, swipe left to close.
///   - Auto-close applies to both VR and Desktop when player walks too far.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PanelHandler : UdonSharpBehaviour
{
    // ── References ──────────────────────────────
    [Header("References")] public Transform panelRoot;
    public GameObject canvas;
    public Image panelImage;

    [Header("Sprites")] public Sprite pcSprite;
    public Sprite vrSprite;

    // ── Tuning ──────────────────────────────────
    [Header("Panel Open Position")] public float panelDistance = 0.7f;
    public float heightOffset = 0.3f;
    public float minPanelHeight = 0.3f;

    [Header("Auto Close")] public float maxDistance = 3f;

    [Header("Initial Open")]
    [Tooltip("Delay (seconds) before opening the panel after joining, to allow player height sync.")]
    public float initialOpenDelay = 3f;

    [Header("Swipe Gesture")]
    public float minSwipeDistance = 0.2f;
    public float maxSwipeTime = 0.4f;

    // ── Runtime state ───────────────────────────
    private VRCPlayerApi _localPlayer;
    private bool _isOpen;
    private bool _isInVR;
    private bool _initialized;

    // ── Input State ─────────────────────────────
    private float _lastTriggerTime = -100f;
    private const float DoubleClickThreshold = 0.3f;

    private bool _leftGripHeld;
    private bool _rightGripHeld;
    private bool _leftSwipeTriggered;
    private bool _rightSwipeTriggered;

    private Vector3 _leftLastPos;
    private Vector3 _rightLastPos;

    private float _leftSwipeDistX;
    private float _leftSwipeTime;

    private float _rightSwipeDistX;
    private float _rightSwipeTime;

    /// <summary>Whether the panel UI is currently open.</summary>
    public bool IsOpen => _isOpen;

    // ════════════════════════════════════════════
    //  Lifecycle
    // ════════════════════════════════════════════

    private void Update()
    {
        if (!_initialized)
        {
            _localPlayer = Networking.LocalPlayer;
            if (_localPlayer == null) return;
            Initialize();
            return;
        }

        if (_isOpen)
        {
            CheckAutoClose();
        }

        if (_isInVR)
        {
            UpdateVRGestures();
        }
        else
        {
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                SetOpen(!_isOpen);
            }
        }
    }

    private void Initialize()
    {
        _isInVR = _localPlayer.IsUserInVR();
        panelImage.sprite = _isInVR ? vrSprite : pcSprite;

        // Start closed; schedule a delayed open to let player height sync
        SetOpen(false);
        SendCustomEventDelayedSeconds(nameof(DelayedInitialOpen), initialOpenDelay);

        _initialized = true;
    }

    public void DelayedInitialOpen()
    {
        SetOpen(true);
    }

    // ════════════════════════════════════════════
    //  Input Handling
    // ════════════════════════════════════════════

    public override void InputUse(bool value, UdonInputEventArgs args)
    {
        if (!_initialized || !_isInVR) return;

        if (value)
        {
            if (Time.time - _lastTriggerTime < DoubleClickThreshold)
            {
                SetOpen(!_isOpen);
                _lastTriggerTime = -100f; // reset
            }
            else
            {
                _lastTriggerTime = Time.time;
            }
        }
    }

    public override void InputGrab(bool value, UdonInputEventArgs args)
    {
        if (!_initialized || !_isInVR) return;

        if (args.handType == HandType.LEFT)
        {
            _leftGripHeld = value;
            if (value)
            {
                _leftLastPos = _localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).position;
                _leftSwipeDistX = 0f;
                _leftSwipeTime = 0f;
                _leftSwipeTriggered = false;
            }
        }
        else if (args.handType == HandType.RIGHT)
        {
            _rightGripHeld = value;
            if (value)
            {
                _rightLastPos = _localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).position;
                _rightSwipeDistX = 0f;
                _rightSwipeTime = 0f;
                _rightSwipeTriggered = false;
            }
        }
    }

    private void UpdateVRGestures()
    {
        if (_leftGripHeld && !_leftSwipeTriggered)
        {
            ProcessSwipe(HandType.LEFT, ref _leftLastPos, ref _leftSwipeDistX, ref _leftSwipeTime, ref _leftSwipeTriggered);
        }

        if (_rightGripHeld && !_rightSwipeTriggered)
        {
            ProcessSwipe(HandType.RIGHT, ref _rightLastPos, ref _rightSwipeDistX, ref _rightSwipeTime, ref _rightSwipeTriggered);
        }
    }

    private void ProcessSwipe(HandType hand, ref Vector3 lastPos, ref float swipeDistX, ref float swipeTime, ref bool triggered)
    {
        VRCPlayerApi.TrackingDataType trackType = hand == HandType.LEFT ? VRCPlayerApi.TrackingDataType.LeftHand : VRCPlayerApi.TrackingDataType.RightHand;
        Vector3 currentPos = _localPlayer.GetTrackingData(trackType).position;
        Vector3 headRight = _localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation * Vector3.right;

        float frameDeltaX = Vector3.Dot(currentPos - lastPos, headRight);
        float dt = Time.deltaTime;
        
        if (dt > 0.001f)
        {
            // If movement is very slow, reset the swipe accumulation (speed < 0.1m/s)
            float frameSpeed = Mathf.Abs(frameDeltaX) / dt;
            if (frameSpeed < 0.1f)
            {
                swipeDistX = 0f;
                swipeTime = 0f;
            }
            else
            {
                // If changed direction, reset accumulation
                if ((frameDeltaX > 0 && swipeDistX < 0) || (frameDeltaX < 0 && swipeDistX > 0))
                {
                    swipeDistX = 0f;
                    swipeTime = 0f;
                }

                swipeDistX += frameDeltaX;
                swipeTime += dt;

                if (Mathf.Abs(swipeDistX) >= minSwipeDistance)
                {
                    if (swipeTime <= maxSwipeTime)
                    {
                        // Trigger action based on direction
                        // Swipe right (> 0) to open, Swipe left (< 0) to close
                        bool openTarget = swipeDistX > 0;
                        if (_isOpen != openTarget)
                        {
                            SetOpen(openTarget);
                        }
                        triggered = true;
                    }
                    else
                    {
                        // Took too long to reach distance, reset to allow trying again without releasing grip
                        swipeDistX = 0f;
                        swipeTime = 0f;
                    }
                }
            }
        }

        lastPos = currentPos;
    }

    // ════════════════════════════════════════════
    //  Shared helpers
    // ════════════════════════════════════════════

    private void CheckAutoClose()
    {
        float dist = Vector3.Distance(_localPlayer.GetPosition(), panelRoot.position);
        if (dist > maxDistance)
        {
            SetOpen(false);
        }
    }

    private void GetIdealPanelPose(out Vector3 pos, out Quaternion rot)
    {
        VRCPlayerApi.TrackingData head = _localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);

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