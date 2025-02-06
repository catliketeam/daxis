using UnityEngine;

public class FrameGridManager : MonoBehaviour
{
    [Header("Container Settings")]
    [Tooltip("The overall width of the container (in world units). Set to 11 for a 9×9 playable grid with cubeSize = 1.")]
    public float containerWidth = 11f;
    [Tooltip("The overall depth of the container (in world units). Set to 11 for a 9×9 playable grid with cubeSize = 1.")]
    public float containerDepth = 11f;
    [Tooltip("The overall height of the container (in world units).")]
    public float containerHeight = 20f;
    [Tooltip("The size of each cell (cube).")]
    public float cubeSize = 1f;
    [Tooltip("Prefab for a single frame cell.")]
    public GameObject cellPrefab;

    // The playable area is the container minus one cube on each side.
    // For example, if containerWidth = 11 and cubeSize = 1, playableWidth = 11 - 2 = 9.
    private float playableWidth;
    private float playableDepth;

    // GridCenter is defined as the center of the container (based on the overall container, not just playable area).
    // For a container with its bottom at y=0, the center is (0, containerHeight/2, 0).
    public Vector3 GridCenter { get; private set; }

    void Start()
    {
        // Compute GridCenter based on container height.
        GridCenter = new Vector3(0, containerHeight / 2f, 0);

        // Compute the playable area dimensions.
        playableWidth = containerWidth - 2 * cubeSize;
        playableDepth = containerDepth - 2 * cubeSize;

        // The playable area is centered at 0.
        // Its boundaries are at:
        //   X: -playableWidth/2 to +playableWidth/2   (i.e. -4.5 to 4.5 for playableWidth = 9)
        //   Z: -playableDepth/2 to +playableDepth/2       (i.e. -4.5 to 4.5 for playableDepth = 9)
        float halfPlayableWidth = playableWidth / 2f;   // e.g., 4.5
        float halfPlayableDepth = playableDepth / 2f;     // e.g., 4.5

        // To be flush with the playable area, the frame cells should have their inner face exactly at these boundaries.
        // Because a frame cell is cubeSize wide, its center should be offset by cubeSize/2.
        float leftX = -halfPlayableWidth - cubeSize / 2f;   // e.g., -4.5 - 0.5 = -5.0
        float rightX = halfPlayableWidth + cubeSize / 2f;     // e.g., 4.5 + 0.5 = 5.0
        float backZ = -halfPlayableDepth - cubeSize / 2f;     // e.g., -4.5 - 0.5 = -5.0
        float frontZ = halfPlayableDepth + cubeSize / 2f;       // e.g., 4.5 + 0.5 = 5.0

        // --- Generate the Bottom Frame (y = 0) ---
        // Frame cells along the front edge.
        for (float x = leftX; x <= rightX; x += cubeSize)
        {
            Vector3 pos = new Vector3(x, 0, frontZ);
            Instantiate(cellPrefab, pos, Quaternion.identity, transform);
        }
        // Frame cells along the back edge.
        for (float x = leftX; x <= rightX; x += cubeSize)
        {
            Vector3 pos = new Vector3(x, 0, backZ);
            Instantiate(cellPrefab, pos, Quaternion.identity, transform);
        }
        // Frame cells along the left edge.
        for (float z = backZ; z <= frontZ; z += cubeSize)
        {
            Vector3 pos = new Vector3(leftX, 0, z);
            Instantiate(cellPrefab, pos, Quaternion.identity, transform);
        }
        // Frame cells along the right edge.
        for (float z = backZ; z <= frontZ; z += cubeSize)
        {
            Vector3 pos = new Vector3(rightX, 0, z);
            Instantiate(cellPrefab, pos, Quaternion.identity, transform);
        }

        // --- Generate Vertical Frame Columns at the Corners ---
        // Front-Left column.
        for (float y = 0; y <= containerHeight; y += cubeSize)
        {
            Vector3 pos = new Vector3(leftX, y, frontZ);
            Instantiate(cellPrefab, pos, Quaternion.identity, transform);
        }
        // Front-Right column.
        for (float y = 0; y <= containerHeight; y += cubeSize)
        {
            Vector3 pos = new Vector3(rightX, y, frontZ);
            Instantiate(cellPrefab, pos, Quaternion.identity, transform);
        }
        // Back-Left column.
        for (float y = 0; y <= containerHeight; y += cubeSize)
        {
            Vector3 pos = new Vector3(leftX, y, backZ);
            Instantiate(cellPrefab, pos, Quaternion.identity, transform);
        }
        // Back-Right column.
        for (float y = 0; y <= containerHeight; y += cubeSize)
        {
            Vector3 pos = new Vector3(rightX, y, backZ);
            Instantiate(cellPrefab, pos, Quaternion.identity, transform);
        }

        // --- Generate the Top Frame (y = containerHeight) ---
        // Top frame along the front edge.
        for (float x = leftX; x <= rightX; x += cubeSize)
        {
            Vector3 pos = new Vector3(x, containerHeight, frontZ);
            Instantiate(cellPrefab, pos, Quaternion.identity, transform);
        }
        // Top frame along the back edge.
        for (float x = leftX; x <= rightX; x += cubeSize)
        {
            Vector3 pos = new Vector3(x, containerHeight, backZ);
            Instantiate(cellPrefab, pos, Quaternion.identity, transform);
        }
        // Top frame along the left edge.
        for (float z = backZ; z <= frontZ; z += cubeSize)
        {
            Vector3 pos = new Vector3(leftX, containerHeight, z);
            Instantiate(cellPrefab, pos, Quaternion.identity, transform);
        }
        // Top frame along the right edge.
        for (float z = backZ; z <= frontZ; z += cubeSize)
        {
            Vector3 pos = new Vector3(rightX, containerHeight, z);
            Instantiate(cellPrefab, pos, Quaternion.identity, transform);
        }
    }
}
