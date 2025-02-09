using UnityEngine;
using System.Collections.Generic;

public class CameraController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Reference to the GameManager3DTetris for grid occupancy info. Ensure GameManager3DTetris exposes GhostPiece.")]
    public GameManager3DTetris gameManager;
    [Tooltip("The target the camera should look at (typically the center of the play area). Defaults to Vector3.zero if unassigned.")]
    public Transform target;
    [Tooltip("Optional: The transform of the frame. The camera’s base yaw is derived from this.")]
    public Transform frameTransform;

    [Header("Dynamic Camera Settings (Empty -> Full)")]
    public float emptyCameraY = 10f;
    public float fullCameraY = 20f;
    public float emptyDistance = 13f;
    public float fullDistance = 15f;
    public float emptyPitch = 10f;
    public float fullPitch = 60f;
    public float lerpSpeed = 2f;

    [Header("Viewport Alignment")]
    [Tooltip("World-space Y coordinate where the bottom edge of the camera's viewport should align.")]
    public float desiredBottomEdge = 1f;

    [Header("Vertical Margin")]
    [Tooltip("The minimum vertical margin above the ghost piece's Y position.")]
    public float minVerticalMargin = 5f;

    [Header("Discrete Yaw Control")]
    [Tooltip("Discrete yaw step in degrees (typically 90°).")]
    public float discreteYawStep = 90f;
    [Tooltip("Speed at which the manual yaw offset transitions (for smooth discrete flipping).")]
    public float yawLerpSpeed = 5f;
    [Tooltip("Swipe threshold in pixels for mobile two‑finger swipe detection.")]
    public float twoFingerSwipeThreshold = 50f;

    [Header("Flip Zoom Effect")]
    [Tooltip("Extra distance added at the peak of a yaw flip (zoom out amount).")]
    public float flipZoomOutAmount = 5f;
    [Tooltip("Duration (in seconds) for each flip phase (zoom out, yaw shift, zoom in). Total flip duration is 3× this value (~0.5 s total).")]
    public float flipPhaseDuration = 0.166f;

    // Internal state for discrete yaw control.
    private float manualYaw = 0f; // current yaw offset applied to the camera
    private Queue<float> pendingFlipQueue = new Queue<float>();

    // Flip state machine.
    private enum FlipState { None, ZoomOut, YawShift, ZoomIn }
    private FlipState currentFlipState = FlipState.None;
    private float flipTimer = 0f;
    private float targetManualYaw = 0f;
    private float initialYawDuringFlip = 0f;

    // New flag: ensure we only register one two-finger swipe per gesture.
    private bool twoFingerSwipeRegistered = false;

    /// <summary>
    /// Returns the effective right vector based on the current final yaw (frame yaw plus manualYaw).
    /// This is used to constrain piece movement to the current horizontal axis.
    /// </summary>
    public Vector3 EffectiveRight {
        get {
            float baseYaw = (frameTransform != null) ? frameTransform.eulerAngles.y : 0f;
            float finalYaw = baseYaw + manualYaw;
            return Quaternion.Euler(0, finalYaw, 0) * Vector3.right;
        }
    }

    void Start()
    {
        // Automatically derive frameTransform if not assigned.
        if (frameTransform == null && gameManager != null && gameManager.frameGrid != null)
            frameTransform = gameManager.frameGrid.transform;
    }

    void Update()
    {
        if (gameManager == null)
        {
            Debug.LogWarning("CameraController: GameManager3DTetris reference missing.");
            return;
        }

        float inputDelta = 0f;

        // --- Desktop Input for Camera Flips (SHIFT + arrow keys) ---
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            if (Input.GetKeyDown(KeyCode.LeftArrow))
                inputDelta = discreteYawStep;
            if (Input.GetKeyDown(KeyCode.RightArrow))
                inputDelta = -discreteYawStep;
        }

        // --- Mobile Input: Two-Finger Swipe for Camera Flips ---
        // Only process if we have at least two touches and no flip is in progress
        if (Application.isMobilePlatform && Input.touchCount >= 2 && currentFlipState == FlipState.None)
        {
            // If we haven't yet registered this two-finger swipe for a flip:
            if (!twoFingerSwipeRegistered)
            {
                Touch touch0 = Input.GetTouch(0);
                Touch touch1 = Input.GetTouch(1);
                // Process if both touches are moving.
                if (touch0.phase == TouchPhase.Moved && touch1.phase == TouchPhase.Moved)
                {
                    float avgDeltaX = (touch0.deltaPosition.x + touch1.deltaPosition.x) / 2f;
                    if (Mathf.Abs(avgDeltaX) >= twoFingerSwipeThreshold)
                    {
                        inputDelta = (avgDeltaX > 0) ? discreteYawStep : -discreteYawStep;
                        twoFingerSwipeRegistered = true; // Register this swipe so we don't re-trigger until released.
                    }
                }
            }
        }
        else
        {
            // If fewer than two touches are present, reset the flag.
            twoFingerSwipeRegistered = false;
        }

        if (Mathf.Abs(inputDelta) > 0.001f)
        {
            pendingFlipQueue.Enqueue(inputDelta);
            if (currentFlipState == FlipState.None)
                StartNextFlip();
        }

        // --- Process the Flip State Machine ---
        float extraDistance = 0f;
        if (currentFlipState != FlipState.None)
        {
            flipTimer += Time.deltaTime;
            float u = Mathf.Clamp01(flipTimer / flipPhaseDuration);
            switch (currentFlipState)
            {
                case FlipState.ZoomOut:
                    extraDistance = flipZoomOutAmount * u;
                    if (flipTimer >= flipPhaseDuration)
                    {
                        currentFlipState = FlipState.YawShift;
                        flipTimer = 0f;
                        initialYawDuringFlip = manualYaw;
                    }
                    break;
                case FlipState.YawShift:
                    manualYaw = Mathf.Lerp(initialYawDuringFlip, targetManualYaw, u);
                    extraDistance = flipZoomOutAmount;
                    if (flipTimer >= flipPhaseDuration)
                    {
                        currentFlipState = FlipState.ZoomIn;
                        flipTimer = 0f;
                    }
                    break;
                case FlipState.ZoomIn:
                    extraDistance = flipZoomOutAmount * (1f - u);
                    if (flipTimer >= flipPhaseDuration)
                    {
                        currentFlipState = FlipState.None;
                        flipTimer = 0f;
                        if (pendingFlipQueue.Count > 0)
                            StartNextFlip();
                    }
                    break;
            }
        }
        else
        {
            extraDistance = 0f;
        }

        // --- Determine the Effective Target Position ---
        Vector3 effectiveTargetPosition = Vector3.zero;
        if (gameManager.GhostPiece != null)
            effectiveTargetPosition = gameManager.GhostPiece.transform.position;
        else if (target != null)
            effectiveTargetPosition = target.position;

        // --- Occupancy-Based Adjustments & Viewport Alignment ---
        int maxOccupiedY = gameManager.GetMaxOccupiedY();
        int gridH = gameManager.GridHeight;
        float tOccupancy = (gridH > 1) ? Mathf.Clamp01(((float)maxOccupiedY) / (gridH * 2f - 1)) : 0f;
        float desiredY = Mathf.Lerp(emptyCameraY, fullCameraY, tOccupancy);
        float desiredDistance = Mathf.Lerp(emptyDistance, fullDistance, tOccupancy) + extraDistance;
        float desiredPitch = Mathf.Lerp(emptyPitch, fullPitch, tOccupancy);

        float baseYaw = (frameTransform != null) ? frameTransform.eulerAngles.y : 0f;
        float finalYawForPos = baseYaw + manualYaw;
        Quaternion desiredRotationForPos = Quaternion.Euler(desiredPitch, finalYawForPos, 0);
        Vector3 offsetVec = desiredRotationForPos * new Vector3(0, 0, -desiredDistance);
        Vector3 preliminaryPos = new Vector3(effectiveTargetPosition.x + offsetVec.x,
                                             effectiveTargetPosition.y + desiredY,
                                             effectiveTargetPosition.z + offsetVec.z);

        Vector3 viewportBottomCenter = ComputeViewportBottomCenter(preliminaryPos, desiredRotationForPos);
        float yAdjustment = viewportBottomCenter.y - desiredBottomEdge;
        Vector3 finalCameraPosition = preliminaryPos;
        finalCameraPosition.y -= yAdjustment;

        // Freeze the Y position if no ghost piece is available.
        if (gameManager.GhostPiece == null)
            finalCameraPosition.y = transform.position.y;
        else
        {
            float ghostY = gameManager.GhostPiece.transform.position.y;
            finalCameraPosition.y = Mathf.Max(finalCameraPosition.y, ghostY + minVerticalMargin);
        }

        Vector3 direction = effectiveTargetPosition - finalCameraPosition;
        Vector3 horizontalDirection = new Vector3(direction.x, 0, direction.z);
        if (horizontalDirection.sqrMagnitude < 0.001f)
            horizontalDirection = Vector3.forward;
        Quaternion horizontalRotation = Quaternion.LookRotation(horizontalDirection);
        Quaternion finalRotation = Quaternion.Euler(desiredPitch, horizontalRotation.eulerAngles.y, 0);

        transform.position = Vector3.Lerp(transform.position, finalCameraPosition, Time.deltaTime * lerpSpeed);
        transform.rotation = Quaternion.Lerp(transform.rotation, finalRotation, Time.deltaTime * lerpSpeed);
    }

    void StartNextFlip()
    {
        if (pendingFlipQueue.Count > 0)
        {
            float delta = pendingFlipQueue.Dequeue();
            targetManualYaw = manualYaw + delta;
            currentFlipState = FlipState.ZoomOut;
            flipTimer = 0f;
        }
    }

    Vector3 ComputeViewportBottomCenter(Vector3 camPos, Quaternion camRot)
    {
        Camera cam = Camera.main;
        if (cam == null)
            return camPos;
        float near = cam.nearClipPlane;
        float fov = cam.fieldOfView;
        float nearHeight = 2 * near * Mathf.Tan(0.5f * fov * Mathf.Deg2Rad);
        Vector3 localBottomCenter = new Vector3(0, -nearHeight / 2f, near);
        return camPos + camRot * localBottomCenter;
    }
}
