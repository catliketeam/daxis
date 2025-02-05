using UnityEngine;

public class FrameGridManager : MonoBehaviour
{
    [Header("Container Dimensions (multiples of cubeSize)")]
    [Tooltip("The overall width (x-direction) of the container.")]
    public float containerWidth = 10f;
    [Tooltip("The overall depth (z-direction) of the container.")]
    public float containerDepth = 10f;
    [Tooltip("The overall height (y-direction) of the container.")]
    public float containerHeight = 20f;
    
    [Header("Cube Settings")]
    [Tooltip("The size of each cube. This value is used as the spacing along the edges.")]
    public float cubeSize = 1f;
    [Tooltip("Assign your Cube Prefab here.")]
    public GameObject cubePrefab;

    // GridCenter represents the center of the container (horizontally and vertically).
    public Vector3 GridCenter { get; private set; }

    void Start()
    {
        // Calculate half dimensions for convenience.
        float halfWidth = containerWidth / 2f;
        float halfDepth = containerDepth / 2f;
        
        // Define the bottom corners (y = 0) of the container.
        Vector3 bottomFrontLeft  = new Vector3(-halfWidth, 0f,  halfDepth);
        Vector3 bottomFrontRight = new Vector3( halfWidth, 0f,  halfDepth);
        Vector3 bottomBackRight  = new Vector3( halfWidth, 0f, -halfDepth);
        Vector3 bottomBackLeft   = new Vector3(-halfWidth, 0f, -halfDepth);
        
        // Compute the horizontal center from the bottom corners.
        Vector3 bottomCenter = (bottomFrontLeft + bottomFrontRight + bottomBackRight + bottomBackLeft) / 4f;
        // Set GridCenter so that its vertical coordinate is at half the container's height.
        GridCenter = new Vector3(bottomCenter.x, containerHeight / 2f, bottomCenter.z);
        
        // Define the corresponding top corners (y = containerHeight).
        Vector3 topFrontLeft  = bottomFrontLeft  + Vector3.up * containerHeight;
        Vector3 topFrontRight = bottomFrontRight + Vector3.up * containerHeight;
        Vector3 topBackRight  = bottomBackRight  + Vector3.up * containerHeight;
        Vector3 topBackLeft   = bottomBackLeft   + Vector3.up * containerHeight;
        
        // --- Build the Bottom Frame (perimeter at y = 0) ---
        PlaceCubesAlongEdge(bottomFrontLeft,  bottomFrontRight);
        PlaceCubesAlongEdge(bottomFrontRight, bottomBackRight);
        PlaceCubesAlongEdge(bottomBackRight,  bottomBackLeft);
        PlaceCubesAlongEdge(bottomBackLeft,   bottomFrontLeft);
        
        // --- Build the Vertical Edges (corner columns) ---
        PlaceCubesAlongEdge(bottomFrontLeft,  topFrontLeft);
        PlaceCubesAlongEdge(bottomFrontRight, topFrontRight);
        PlaceCubesAlongEdge(bottomBackRight,  topBackRight);
        PlaceCubesAlongEdge(bottomBackLeft,   topBackLeft);
        
        // --- Build the Top Frame (perimeter at y = containerHeight) ---
        PlaceCubesAlongEdge(topFrontLeft,  topFrontRight);
        PlaceCubesAlongEdge(topFrontRight, topBackRight);
        PlaceCubesAlongEdge(topBackRight,  topBackLeft);
        PlaceCubesAlongEdge(topBackLeft,   topFrontLeft);
    }

    /// <summary>
    /// Instantiates cubes along the straight-line edge between two points.
    /// Both endpoints are included.
    /// </summary>
    /// <param name="start">Starting point of the edge.</param>
    /// <param name="end">Ending point of the edge.</param>
    void PlaceCubesAlongEdge(Vector3 start, Vector3 end)
    {
        float distance = Vector3.Distance(start, end);
        int segments = Mathf.RoundToInt(distance / cubeSize);

        // Ensure at least one cube is placed.
        if (segments <= 0)
        {
            Instantiate(cubePrefab, start, Quaternion.identity, transform);
            return;
        }

        // Place cubes along the edge, including both endpoints.
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            Vector3 position = Vector3.Lerp(start, end, t);
            Instantiate(cubePrefab, position, Quaternion.identity, transform);
        }
    }
}
