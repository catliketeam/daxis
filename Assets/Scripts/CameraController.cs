using UnityEngine;

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
    [Tooltip("Camera Y offset when the play area is empty (few blocks).")]
    public float emptyCameraY = 10f;
    [Tooltip("Camera Y offset when the play area is full.")]
    public float fullCameraY = 20f;
    [Tooltip("Camera distance from the target when the play area is empty (closer in).")]
    public float emptyDistance = 13f;
    [Tooltip("Camera distance from the target when the play area is full (further away).")]
    public float fullDistance = 15f;
    [Tooltip("Camera pitch (tilt angle) when the play area is empty (shallow view).")]
    public float emptyPitch = 10f;
    [Tooltip("Camera pitch when the play area is full (steeper, looking down).")]
    public float fullPitch = 60f;
    [Tooltip("Speed at which the camera moves and rotates.")]
    public float lerpSpeed = 2f;

    [Header("Viewport Alignment")]
    [Tooltip("World-space Y coordinate where the bottom edge of the camera's viewport should align. Typically, this is the top edge of the bottom row of the play area (e.g., 1).")]
    public float desiredBottomEdge = 1f;

    [Header("Vertical Margin")]
    [Tooltip("The minimum vertical margin above the ghost piece's Y position. The camera's Y will be at least ghostPiece.y + this value.")]
    public float minVerticalMargin = 5f;

    void Start()
    {
        // Automatically derive frameTransform if not assigned.
        if (frameTransform == null && gameManager != null && gameManager.frameGrid != null)
        {
            frameTransform = gameManager.frameGrid.transform;
        }
    }

    void Update()
    {
        if (gameManager == null)
        {
            Debug.LogWarning("CameraController: GameManager3DTetris reference is missing.");
            return;
        }

        // --- Determine the Effective Target Position ---
        // Prefer the ghost piece's position; otherwise, use the assigned target; if neither is available, default to Vector3.zero.
        Vector3 effectiveTargetPosition = Vector3.zero;
        if (gameManager.GhostPiece != null)
            effectiveTargetPosition = gameManager.GhostPiece.transform.position;
        else if (target != null)
            effectiveTargetPosition = target.position;

        // --- Occupancy-Based Adjustments ---
        int maxOccupiedY = gameManager.GetMaxOccupiedY();
        int gridH = gameManager.GridHeight;
        // Compute normalized occupancy value t:
        // t = (maxOccupiedY) / (gridH * 2f - 1), clamped between 0 and 1.
        float t = (gridH > 1) ? Mathf.Clamp01(((float)maxOccupiedY) / (gridH * 2f - 1)) : 0f;

        float desiredY = Mathf.Lerp(emptyCameraY, fullCameraY, t);
        float desiredDistance = Mathf.Lerp(emptyDistance, fullDistance, t);
        float desiredPitch = Mathf.Lerp(emptyPitch, fullPitch, t);

        // --- Automatic Yaw from the Frame Transform ---
        float baseYaw = (frameTransform != null) ? frameTransform.eulerAngles.y : 0f;
        float finalYaw = baseYaw; // No manual yaw adjustments.

        // --- Compute Preliminary Desired Camera Position ---
        Quaternion desiredRotationForPos = Quaternion.Euler(desiredPitch, finalYaw, 0);
        Vector3 offset = desiredRotationForPos * new Vector3(0, 0, -desiredDistance);
        Vector3 preliminaryPos = new Vector3(effectiveTargetPosition.x + offset.x,
                                             effectiveTargetPosition.y + desiredY,
                                             effectiveTargetPosition.z + offset.z);

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

        // --- Smoothly Update the Camera's Position and Rotation ---
        transform.position = Vector3.Lerp(transform.position, finalCameraPosition, Time.deltaTime * lerpSpeed);
        transform.rotation = Quaternion.Lerp(transform.rotation, finalRotation, Time.deltaTime * lerpSpeed);
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
        float fov = cam.fieldOfView; // vertical field of view in degrees
        float nearHeight = 2 * near * Mathf.Tan(0.5f * fov * Mathf.Deg2Rad);
        Vector3 localBottomCenter = new Vector3(0, -nearHeight / 2f, near);
        return camPos + camRot * localBottomCenter;
    }
}
