using UnityEngine;
using System.Collections.Generic;

public class CameraController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Reference to the GameManager3DTetris for grid occupancy information. Ensure GameManager3DTetris exposes GhostPiece.")]
    public GameManager3DTetris gameManager;
    [Tooltip("The target the camera should look at (typically the center of the play area). If unassigned, defaults to Vector3.zero.")]
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
    [Tooltip("World-space Y coordinate where the bottom edge of the camera's viewport should align (e.g., the top edge of the bottom row, e.g., 1).")]
    public float desiredBottomEdge = 1f;

    [Header("Vertical Margin")]
    [Tooltip("The minimum vertical margin above the ghost piece's Y position.")]
    public float minVerticalMargin = 5f;

    [Header("Discrete Yaw Control")]
    [Tooltip("Discrete yaw step in degrees (typically 90°).")]
    public float discreteYawStep = 90f;
    [Tooltip("Speed at which the manual yaw offset transitions (for smooth discrete flipping).")]
    public float yawLerpSpeed = 5f;
    [Tooltip("Swipe threshold in pixels for mobile input.")]
    public float swipeThreshold = 100f;

    [Header("Flip Zoom Effect")]
    [Tooltip("Extra distance added at the peak of a yaw flip (zoom out amount).")]
    public float flipZoomOutAmount = 5f;
    [Tooltip("Duration (in seconds) for each flip phase (zoom out, yaw shift, zoom in). Total flip duration will be 3× this value (e.g., 0.166 s per phase for ~0.5 s total).")]
    public float flipPhaseDuration = 0.166f;

    // Internal state for discrete yaw control.
    private float manualYaw = 0f; // Current yaw offset applied to the camera.
    private Queue<float> pendingFlipQueue = new Queue<float>();

    // Flip state machine.
    private enum FlipState { None, ZoomOut, YawShift, ZoomIn }
    private FlipState currentFlipState = FlipState.None;
    private float flipTimer = 0f;
    private float targetManualYaw = 0f;
    private float initialYawDuringFlip = 0f;

    // For mobile swipe input.
    private Vector2 touchStartPos;
    private bool isSwiping = false;

    /// <summary>
    /// Returns the effective right vector based on the current final yaw (frame yaw + manualYaw).
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
        if (frameTransform == null && gameManager != null && gameManager.frameGrid != null)
        {
            frameTransform = gameManager.frameGrid.transform;
        }
    }

    void Update()
    {
        if (gameManager == null)
        {
            Debug.LogWarning("CameraController: GameManager3DTetris reference missing.");
            return;
        }

        // --- Handle Discrete Yaw Input (Desktop) ---
        float inputDelta = 0f;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            // Desktop input: SHIFT+LEFT adds a positive yaw change; SHIFT+RIGHT subtracts.
            if (Input.GetKeyDown(KeyCode.LeftArrow))
                inputDelta = discreteYawStep;
            if (Input.GetKeyDown(KeyCode.RightArrow))
                inputDelta = -discreteYawStep;
        }

        // --- Handle Swipe Input (Mobile) ---
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began)
            {
                touchStartPos = touch.position;
                isSwiping = true;
            }
            else if (touch.phase == TouchPhase.Ended && isSwiping)
            {
                Vector2 touchEndPos = touch.position;
                Vector2 delta = touchEndPos - touchStartPos;
                if (Mathf.Abs(delta.x) >= swipeThreshold)
                {
                    // Invert swipe: now swipe right adds a positive yaw change; swipe left subtracts.
                    if (delta.x > 0)
                        inputDelta = discreteYawStep;
                    else
                        inputDelta = -discreteYawStep;
                }
                isSwiping = false;
            }
        }

        // If an input was detected, enqueue it.
        if (Mathf.Abs(inputDelta) > 0.001f)
        {
            pendingFlipQueue.Enqueue(inputDelta);
            // If no flip is in progress, start the next flip.
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
                    extraDistance = flipZoomOutAmount; // maintain maximum zoom out.
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
                        // Only process the next flip if the queue is non-empty.
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

        // --- Occupancy-Based Adjustments ---
        int maxOccupiedY = gameManager.GetMaxOccupiedY();
        int gridH = gameManager.GridHeight;
        float tOccupancy = (gridH > 1) ? Mathf.Clamp01(((float)maxOccupiedY) / (gridH * 2f - 1)) : 0f;
        float desiredY = Mathf.Lerp(emptyCameraY, fullCameraY, tOccupancy);
        float desiredDistance = Mathf.Lerp(emptyDistance, fullDistance, tOccupancy) + extraDistance;
        float desiredPitch = Mathf.Lerp(emptyPitch, fullPitch, tOccupancy);

        // --- Automatic Yaw from Frame Transform and Discrete Input ---
        float baseYaw = (frameTransform != null) ? frameTransform.eulerAngles.y : 0f;
        float finalYawForPos = baseYaw + manualYaw;

        // --- Compute Preliminary Desired Camera Position ---
        Quaternion desiredRotationForPos = Quaternion.Euler(desiredPitch, finalYawForPos, 0);
        Vector3 offsetVec = desiredRotationForPos * new Vector3(0, 0, -desiredDistance);
        Vector3 preliminaryPos = new Vector3(effectiveTargetPosition.x + offsetVec.x,
                                             effectiveTargetPosition.y + desiredY,
                                             effectiveTargetPosition.z + offsetVec.z);

        // --- Adjust for Viewport Bottom Alignment ---
        Vector3 viewportBottomCenter = ComputeViewportBottomCenter(preliminaryPos, desiredRotationForPos);
        float yAdjustment = viewportBottomCenter.y - desiredBottomEdge;
        Vector3 finalCameraPosition = preliminaryPos;
        finalCameraPosition.y -= yAdjustment;

        // --- Ensure the Camera's Y is at least minVerticalMargin above the ghost piece ---
        if (gameManager.GhostPiece != null)
        {
            float ghostY = gameManager.GhostPiece.transform.position.y;
            finalCameraPosition.y = Mathf.Max(finalCameraPosition.y, ghostY + minVerticalMargin);
        }

        // --- Compute Final Camera Rotation (Looking Downward at the Target) ---
        Vector3 direction = effectiveTargetPosition - finalCameraPosition;
        Vector3 horizontalDirection = new Vector3(direction.x, 0, direction.z);
        if (horizontalDirection.sqrMagnitude < 0.001f)
            horizontalDirection = Vector3.forward;
        Quaternion horizontalRotation = Quaternion.LookRotation(horizontalDirection);
        Quaternion finalRotation = Quaternion.Euler(desiredPitch, horizontalRotation.eulerAngles.y, 0);

        // --- Smoothly Update the Camera's Transform ---
        transform.position = Vector3.Lerp(transform.position, finalCameraPosition, Time.deltaTime * lerpSpeed);
        transform.rotation = Quaternion.Lerp(transform.rotation, finalRotation, Time.deltaTime * lerpSpeed);
    }

    /// <summary>
    /// Starts the next flip from the pending queue, if available.
    /// </summary>
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

    /// <summary>
    /// Computes the world-space position of the bottom center of the camera's viewport,
    /// based on the given camera position and rotation. Uses the main camera's near clip plane and vertical FOV.
    /// </summary>
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
