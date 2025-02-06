using UnityEngine;

public class FrameGridManager : MonoBehaviour
{
    [Header("Container Settings")]
    [Tooltip("The overall width of the container (in world units).")]
    public float containerWidth = 10f;
    [Tooltip("The overall depth of the container (in world units).")]
    public float containerDepth = 10f;
    [Tooltip("The overall height of the container (in world units).")]
    public float containerHeight = 20f;
    [Tooltip("The size of each cell (cube).")]
    public float cubeSize = 1f;
    [Tooltip("Prefab for a single frame cell.")]
    public GameObject cellPrefab;

    [Header("Frame Appearance")]
    [Tooltip("Padding to offset the frame blocks outside the playable area (in world units).")]
    public float framePadding = 0.5f;  // This will place frame cell centers at -halfWidth - 0.5 and halfWidth + 0.5

    // The GridCenter is defined as the center of the container.
    // With the bottom centered at (0,0,0), the overall center is (0, containerHeight/2, 0).
    public Vector3 GridCenter { get; private set; }

    void Start()
    {
        // Calculate the center of the container.
        GridCenter = new Vector3(0, containerHeight / 2f, 0);

        // Calculate half dimensions.
        float halfWidth = containerWidth / 2f;
        float halfDepth = containerDepth / 2f;

        // Set frame block positions so that they lie outside the playable area.
        // For a containerWidth of 10 and framePadding of 0.5, the left edge will be at -5.5 and the right at 5.5.
        float leftX = -halfWidth - framePadding;   // e.g., -5 - 0.5 = -5.5
        float rightX = halfWidth + framePadding;     // e.g., 5 + 0.5 = 5.5
        float frontZ = halfDepth + framePadding;     // e.g., 5 + 0.5 = 5.5
        float backZ = -halfDepth - framePadding;     // e.g., -5 - 0.5 = -5.5

        // --- Generate the Bottom Frame (y = 0) ---
        // Front edge (along X axis)
        for (float x = leftX; x <= rightX; x += cubeSize)
        {
            Vector3 pos = new Vector3(x, 0, frontZ);
            Instantiate(cellPrefab, pos, Quaternion.identity, transform);
        }
        // Back edge
        for (float x = leftX; x <= rightX; x += cubeSize)
        {
            Vector3 pos = new Vector3(x, 0, backZ);
            Instantiate(cellPrefab, pos, Quaternion.identity, transform);
        }
        // Left edge (along Z axis)
        for (float z = backZ; z <= frontZ; z += cubeSize)
        {
            Vector3 pos = new Vector3(leftX, 0, z);
            Instantiate(cellPrefab, pos, Quaternion.identity, transform);
        }
        // Right edge
        for (float z = backZ; z <= frontZ; z += cubeSize)
        {
            Vector3 pos = new Vector3(rightX, 0, z);
            Instantiate(cellPrefab, pos, Quaternion.identity, transform);
        }

        // --- Generate Vertical Frame Columns (at the corners) ---
        // Front-Left
        for (float y = 0; y <= containerHeight; y += cubeSize)
        {
            Vector3 pos = new Vector3(leftX, y, frontZ);
            Instantiate(cellPrefab, pos, Quaternion.identity, transform);
        }
        // Front-Right
        for (float y = 0; y <= containerHeight; y += cubeSize)
        {
            Vector3 pos = new Vector3(rightX, y, frontZ);
            Instantiate(cellPrefab, pos, Quaternion.identity, transform);
        }
        // Back-Left
        for (float y = 0; y <= containerHeight; y += cubeSize)
        {
            Vector3 pos = new Vector3(leftX, y, backZ);
            Instantiate(cellPrefab, pos, Quaternion.identity, transform);
        }
        // Back-Right
        for (float y = 0; y <= containerHeight; y += cubeSize)
        {
            Vector3 pos = new Vector3(rightX, y, backZ);
            Instantiate(cellPrefab, pos, Quaternion.identity, transform);
        }

        // --- Generate the Top Frame (y = containerHeight) ---
        // Front edge
        for (float x = leftX; x <= rightX; x += cubeSize)
        {
            Vector3 pos = new Vector3(x, containerHeight, frontZ);
            Instantiate(cellPrefab, pos, Quaternion.identity, transform);
        }
        // Back edge
        for (float x = leftX; x <= rightX; x += cubeSize)
        {
            Vector3 pos = new Vector3(x, containerHeight, backZ);
            Instantiate(cellPrefab, pos, Quaternion.identity, transform);
        }
        // Left edge
        for (float z = backZ; z <= frontZ; z += cubeSize)
        {
            Vector3 pos = new Vector3(leftX, containerHeight, z);
            Instantiate(cellPrefab, pos, Quaternion.identity, transform);
        }
        // Right edge
        for (float z = backZ; z <= frontZ; z += cubeSize)
        {
            Vector3 pos = new Vector3(rightX, containerHeight, z);
            Instantiate(cellPrefab, pos, Quaternion.identity, transform);
        }
    }
}
