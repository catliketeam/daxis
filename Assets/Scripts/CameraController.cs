using UnityEngine;
using System.Collections;

public class CameraController : MonoBehaviour
{
    [Header("Target and Distance")]
    [Tooltip("If left empty, the script will attempt to find a GridManager and use its GridCenter.")]
    public Transform target; // The camera will look at this target
    public float distance = 20f; // How far the camera is from the target

    [Header("Swipe Settings")]
    public float swipeThreshold = 50f; // Minimum swipe distance (in pixels) to count as a swipe

    private int currentFace = 0; // 0 = front, 1 = right, 2 = back, 3 = left
    private Vector3[] positions;
    private Quaternion[] rotations;

    // Variables for swipe detection
    private Vector2 touchStart;
    private bool isSwiping = false;

    void Start()
    {
        if (target == null)
        {
            // Try to find the FrameGridManager in the scene.
            FrameGridManager gridManager = FindObjectOfType<FrameGridManager>();
            if (gridManager != null)
            {
                // Use the computed GridCenter from the grid manager.
                GameObject dummyTarget = new GameObject("CameraTarget");
                dummyTarget.transform.position = gridManager.GridCenter;
                target = dummyTarget.transform;
            }
            else
            {
                Debug.LogError("No FrameGridManager found in the scene. Please add one or assign a target manually.");
            }
        }

        // Pre-calculate the camera positions and rotations for each side of the grid.
        positions = new Vector3[4];
        rotations = new Quaternion[4];

        // Face 0: Front (assume front is in the negative Z direction)
        positions[0] = target.position + new Vector3(0, 0, -distance);
        rotations[0] = Quaternion.LookRotation(target.position - positions[0]);

        // Face 1: Right (positive X)
        positions[1] = target.position + new Vector3(distance, 0, 0);
        rotations[1] = Quaternion.LookRotation(target.position - positions[1]);

        // Face 2: Back (positive Z)
        positions[2] = target.position + new Vector3(0, 0, distance);
        rotations[2] = Quaternion.LookRotation(target.position - positions[2]);

        // Face 3: Left (negative X)
        positions[3] = target.position + new Vector3(-distance, 0, 0);
        rotations[3] = Quaternion.LookRotation(target.position - positions[3]);

        // Set the initial camera position and rotation
        transform.position = positions[currentFace];
        transform.rotation = rotations[currentFace];
    }

    // Call this to move to the next face (e.g., on swipe right)
    public void NextFace()
    {
        currentFace = (currentFace + 1) % 4;
        StopAllCoroutines();
        StartCoroutine(MoveCameraTo(positions[currentFace], rotations[currentFace]));
    }

    // Call this to move to the previous face (e.g., on swipe left)
    public void PreviousFace()
    {
        currentFace = (currentFace - 1 + 4) % 4;
        StopAllCoroutines();
        StartCoroutine(MoveCameraTo(positions[currentFace], rotations[currentFace]));
    }

    // Smoothly transitions the camera from its current position/rotation to the new ones.
    IEnumerator MoveCameraTo(Vector3 newPosition, Quaternion newRotation)
    {
        float duration = 0.5f; // Duration of the transition in seconds
        float t = 0;
        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;
        while (t < duration)
        {
            t += Time.deltaTime;
            float fraction = t / duration;
            transform.position = Vector3.Lerp(startPos, newPosition, fraction);
            transform.rotation = Quaternion.Slerp(startRot, newRotation, fraction);
            yield return null;
        }
        transform.position = newPosition;
        transform.rotation = newRotation;
    }

    void Update()
    {
        // ---- Keyboard Input (Shift + Left/Right) ----
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                PreviousFace();
            }
            else if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                NextFace();
            }
        }

        // ---- Mobile Touch/Swipe Input ----
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began)
            {
                touchStart = touch.position;
                isSwiping = true;
            }
            else if (touch.phase == TouchPhase.Ended && isSwiping)
            {
                Vector2 touchEnd = touch.position;
                float deltaX = touchEnd.x - touchStart.x;
                if (Mathf.Abs(deltaX) > swipeThreshold)
                {
                    if (deltaX > 0)
                    {
                        NextFace();
                    }
                    else
                    {
                        PreviousFace();
                    }
                }
                isSwiping = false;
            }
        }
    }
}
