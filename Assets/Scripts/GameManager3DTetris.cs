using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class GameManager3DTetris : MonoBehaviour
{
    [Header("References & Settings")]
    [Tooltip("Reference to the FrameGridManager that defines the container dimensions.")]
    public FrameGridManager frameGrid;

    [Header("Piece Settings")]
    [Tooltip("Array of falling piece prefabs. A random prefab is selected each time a new piece spawns.")]
    public GameObject[] fallingPiecePrefabs;
    [Tooltip("Time (in seconds) between downward moves when not soft-dropping.")]
    public float fallInterval = 1f;
    [Tooltip("Multiplier to reduce the fall interval when soft-dropping (holding Down Arrow).")]
    public float softDropMultiplier = 0.2f;

    // Grid dimensions and cell size.
    private int gridWidth;    // Number of playable cells in X (e.g., 9)
    private int gridHeight;   // Vertical cell count
    private int gridDepth;    // Number of playable cells in Z (e.g., 9)
    private float cubeSize;

    // Playable area dimensions (inside the frame).
    private float playableWidth;
    private float playableDepth;

    // The grid stores a reference to the block (Transform) occupying each playable cell.
    private Transform[,,] grid;

    // The currently falling (active) piece.
    private GameObject currentPiece;
    private float fallTimer = 0f;

    // List of locked groups. When a piece locks, its blocks remain grouped.
    private List<GameObject> lockedGroups = new List<GameObject>();

    // Flag to disable user input during processing.
    private bool processingLockedBlocks = false;

    // The ghost piece (visual only) that shows where the current piece will land.
    private GameObject ghostPiece;

    //////////////////////////////////////////////////////////////////////////////
    // MOBILE INPUT VARIABLES (for piece movement)
    //////////////////////////////////////////////////////////////////////////////
    private Vector2 touchStartPos;       // Stores the starting position of a touch
    public float swipeThreshold = 100f;    // Swipe threshold in pixels

    //////////////////////////////////////////////////////////////////////////////
    // PUBLIC ACCESSORS FOR EXTERNAL SYSTEMS
    //////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// Returns the grid's vertical cell count.
    /// </summary>
    public int GridHeight
    {
        get { return gridHeight; }
    }

    /// <summary>
    /// Returns the center of the grid as (0, (gridHeight * cubeSize)/2, 0).
    /// Assumes the grid is centered horizontally (X and Z) and that the bottom is at Y = 0.
    /// </summary>
    public Vector3 GridCenter
    {
        get { return new Vector3(0, (gridHeight * cubeSize) / 2f, 0); }
    }

    /// <summary>
    /// Scans the grid and returns the highest (maximum Y) index that is occupied.
    /// </summary>
    public int GetMaxOccupiedY()
    {
        int maxY = 0;
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                for (int z = 0; z < gridDepth; z++)
                {
                    if (grid[x, y, z] != null)
                        maxY = Mathf.Max(maxY, y);
                }
            }
        }
        return maxY;
    }

    /// <summary>
    /// Returns the current ghost piece GameObject (if any), otherwise null.
    /// </summary>
    public GameObject GhostPiece
    {
        get { return ghostPiece; }
    }

    void Start()
    {
        if (frameGrid == null)
        {
            Debug.LogError("FrameGridManager reference not set on GameManager3DTetris!");
            return;
        }

        cubeSize = frameGrid.cubeSize;
        playableWidth = frameGrid.containerWidth - 2 * cubeSize;
        playableDepth = frameGrid.containerDepth - 2 * cubeSize;

        gridWidth = Mathf.RoundToInt(playableWidth / cubeSize);
        gridDepth = Mathf.RoundToInt(playableDepth / cubeSize);
        gridHeight = Mathf.RoundToInt(frameGrid.containerHeight / cubeSize);

        grid = new Transform[gridWidth, gridHeight, gridDepth];

        SpawnNewPiece();
    }

    void Update()
    {
        // Press G to log grid occupancy.
        if (Input.GetKeyDown(KeyCode.G))
            LogGridState();

        if (processingLockedBlocks)
            return;

        if (currentPiece != null)
        {
            HandleInput();

            UpdateGhostPiece();

            if (Input.GetKeyDown(KeyCode.Space))
            {
                SlamPiece();
                return;
            }

            fallTimer += Time.deltaTime;
            float effectiveFallInterval = fallInterval;
            if (Input.GetKey(KeyCode.DownArrow))
                effectiveFallInterval = fallInterval * softDropMultiplier;
            if (fallTimer >= effectiveFallInterval)
            {
                fallTimer = 0f;
                MovePiece(Vector3.down * cubeSize);
            }
        }
        else
        {
            if (ghostPiece != null)
            {
                Destroy(ghostPiece);
                ghostPiece = null;
            }
        }
    }

    /// <summary>
    /// Logs the occupancy of the grid to the Unity console.
    /// Each layer (Y) is printed from top (highest Y) to bottom (Y = 0).
    /// "X" indicates a locked cell, "A" indicates a block from the active piece, and "G" indicates a ghost block.
    /// Empty cells are denoted by ".".
    /// </summary>
    void LogGridState()
    {
        string log = "Grid Occupancy:\n";
        for (int y = gridHeight - 1; y >= 0; y--)
        {
            log += $"Layer {y}:\n";
            for (int z = 0; z < gridDepth; z++)
            {
                string row = "";
                for (int x = 0; x < gridWidth; x++)
                {
                    char symbol = '.';
                    if (grid[x, y, z] != null)
                        symbol = 'X';
                    if (currentPiece != null)
                    {
                        foreach (Transform block in currentPiece.transform)
                        {
                            Vector3Int cell = WorldToGrid(block.position);
                            if (cell.x == x && cell.y == y && cell.z == z)
                            {
                                symbol = 'A';
                                break;
                            }
                        }
                    }
                    if (ghostPiece != null)
                    {
                        foreach (Transform block in ghostPiece.transform)
                        {
                            Vector3Int cell = WorldToGrid(block.position);
                            if (cell.x == x && cell.y == y && cell.z == z && symbol != 'A')
                            {
                                symbol = 'G';
                                break;
                            }
                        }
                    }
                    row += symbol;
                }
                log += row + "\n";
            }
        }
        Debug.Log(log);
    }

    /// <summary>
    /// Handles input for moving, rotating, and slamming the active piece.
    /// Desktop input uses keyboard keys.
    /// On mobile:
    ///   - A tap on the left/right halves moves the piece left/right.
    ///   - A vertical swipe upward rotates the piece.
    ///   - A vertical swipe downward slams the piece down.
    /// </summary>
    void HandleInput()
    {
        if (Application.isMobilePlatform)
        {
            // Process mobile touch input.
            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Began)
                {
                    touchStartPos = touch.position;
                }
                else if (touch.phase == TouchPhase.Ended)
                {
                    Vector2 delta = touch.position - touchStartPos;
                    // Determine if vertical swipe is dominant.
                    if (Mathf.Abs(delta.y) > Mathf.Abs(delta.x) && Mathf.Abs(delta.y) >= swipeThreshold)
                    {
                        if (delta.y > 0)
                        {
                            // Swipe upward: rotate piece.
                            RotatePiece();
                        }
                        else
                        {
                            // Swipe downward: slam piece.
                            SlamPiece();
                        }
                    }
                    else if (Mathf.Abs(delta.x) < 10f && Mathf.Abs(delta.y) < 10f)
                    {
                        // Minimal movement = tap. Determine left/right based on tap position.
                        // Use the EffectiveRight from CameraController.
                        CameraController camController = Camera.main.GetComponent<CameraController>();
                        Vector3 effectiveRight;
                        if (camController != null)
                            effectiveRight = camController.EffectiveRight;
                        else
                        {
                            effectiveRight = Camera.main.transform.right;
                            effectiveRight.y = 0;
                            effectiveRight.Normalize();
                        }
                        if (touch.position.x < Screen.width / 2)
                            MovePiece(-effectiveRight * cubeSize);
                        else
                            MovePiece(effectiveRight * cubeSize);
                    }
                }
            }
        }
        else
        {
            // Desktop input: process piece movement if SHIFT is not held (to avoid conflicting with camera flips).
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                return;

            CameraController camController = Camera.main.GetComponent<CameraController>();
            Vector3 effectiveRight;
            if (camController != null)
                effectiveRight = camController.EffectiveRight;
            else
            {
                effectiveRight = Camera.main.transform.right;
                effectiveRight.y = 0;
                effectiveRight.Normalize();
            }

            if (Input.GetKeyDown(KeyCode.LeftArrow))
                MovePiece(-effectiveRight * cubeSize);
            else if (Input.GetKeyDown(KeyCode.RightArrow))
                MovePiece(effectiveRight * cubeSize);
            else if (Input.GetKeyDown(KeyCode.UpArrow))
                RotatePiece();
        }
    }

    void RotatePiece()
    {
        if (currentPiece == null)
            return;

        Quaternion oldRotation = currentPiece.transform.rotation;
        // Rotate the active piece 90° clockwise about the Y axis.
        currentPiece.transform.Rotate(0, 90, 0, Space.World);
        SnapPieceHorizontally();
        SnapPieceVertically();
        if (!IsValidPosition(currentPiece))
        {
            currentPiece.transform.rotation = oldRotation;
        }
        else
        {
            if (ghostPiece != null)
                Destroy(ghostPiece);
            UpdateGhostPiece();
        }
    }

    float GetPieceBottomUsingRenderers(GameObject piece)
    {
        Renderer[] renderers = piece.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return piece.transform.position.y;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds.min.y;
    }

    void MovePiece(Vector3 move)
    {
        currentPiece.transform.position += move;
        if (!IsValidPosition(currentPiece))
        {
            currentPiece.transform.position -= move;
            if (move == Vector3.down * cubeSize)
                LockPiece();
        }
        else
        {
            if (move != Vector3.down * cubeSize)
                SnapPieceHorizontally();
        }
    }

    void SnapPieceHorizontally()
    {
        Vector3 pos = currentPiece.transform.position;
        float halfPlayableWidth = playableWidth / 2f;
        float halfPlayableDepth = playableDepth / 2f;
        float newX = Mathf.Round((pos.x + halfPlayableWidth - cubeSize / 2f) / cubeSize) * cubeSize - halfPlayableWidth + cubeSize / 2f;
        float newZ = Mathf.Round((pos.z + halfPlayableDepth - cubeSize / 2f) / cubeSize) * cubeSize - halfPlayableDepth + cubeSize / 2f;
        currentPiece.transform.position = new Vector3(newX, pos.y, newZ);
    }

    void SnapPieceVertically()
    {
        Vector3 pos = currentPiece.transform.position;
        float newY = Mathf.Floor(pos.y / cubeSize) * cubeSize;
        currentPiece.transform.position = new Vector3(pos.x, newY, pos.z);
    }

    bool IsValidPosition(GameObject piece)
    {
        foreach (Transform block in piece.transform)
        {
            Vector3Int cell = WorldToGrid(block.position);
            if (cell.x < 0 || cell.x >= gridWidth || cell.z < 0 || cell.z >= gridDepth)
                return false;
            if (cell.y < 0)
                return false;
            if (cell.y < gridHeight && grid[cell.x, cell.y, cell.z] != null)
                return false;
        }
        return true;
    }

    Vector3Int WorldToGrid(Vector3 pos)
    {
        float halfPlayableWidth = playableWidth / 2f;
        float halfPlayableDepth = playableDepth / 2f;
        int x = Mathf.FloorToInt((pos.x + halfPlayableWidth) / cubeSize);
        int y = Mathf.FloorToInt(pos.y / cubeSize);
        int z = Mathf.FloorToInt((pos.z + halfPlayableDepth) / cubeSize);
        return new Vector3Int(x, y, z);
    }

    Vector3 GridToWorld(Vector3Int cell)
    {
        float halfPlayableWidth = playableWidth / 2f;
        float halfPlayableDepth = playableDepth / 2f;
        float x = cell.x * cubeSize - halfPlayableWidth + cubeSize / 2f;
        float y = cell.y * cubeSize;
        float z = cell.z * cubeSize - halfPlayableDepth + cubeSize / 2f;
        return new Vector3(x, y, z);
    }

    void LockPiece()
    {
        foreach (Transform block in currentPiece.transform)
        {
            Vector3Int cell = WorldToGrid(block.position);
            if (cell.x >= 0 && cell.x < gridWidth &&
                cell.y >= 0 && cell.y < gridHeight &&
                cell.z >= 0 && cell.z < gridDepth)
            {
                grid[cell.x, cell.y, cell.z] = block;
            }
        }
        lockedGroups.Add(currentPiece);
        currentPiece = null;
        if (ghostPiece != null)
        {
            Destroy(ghostPiece);
            ghostPiece = null;
        }
        StartCoroutine(ProcessLockedBlocks());
    }

    IEnumerator ProcessLockedBlocks()
    {
        processingLockedBlocks = true;
        bool changesMade;
        do
        {
            bool linesCleared = ClearHorizontalLines();
            if (linesCleared)
            {
                yield return new WaitForSeconds(0.5f);
                RebuildGrid();
            }
            bool gravityApplied = false;
            while (ApplyIterativeGravity())
            {
                gravityApplied = true;
                yield return new WaitForSeconds(0.1f);
            }
            changesMade = linesCleared || gravityApplied;
        } while (changesMade);
        RebuildGrid();
        processingLockedBlocks = false;
        SpawnNewPiece();
    }

    void RebuildGrid()
    {
        grid = new Transform[gridWidth, gridHeight, gridDepth];
        foreach (GameObject group in lockedGroups)
        {
            foreach (Transform block in group.transform)
            {
                Vector3Int cell = WorldToGrid(block.position);
                if (cell.x >= 0 && cell.x < gridWidth &&
                    cell.y >= 0 && cell.y < gridHeight &&
                    cell.z >= 0 && cell.z < gridDepth)
                {
                    grid[cell.x, cell.y, cell.z] = block;
                }
            }
        }
        GameObject[] detachedBlocks = GameObject.FindGameObjectsWithTag("Block");
        foreach (GameObject blockObj in detachedBlocks)
        {
            if (blockObj.transform.parent == null || !lockedGroups.Contains(blockObj.transform.parent.gameObject))
            {
                Vector3Int cell = WorldToGrid(blockObj.transform.position);
                if (cell.x >= 0 && cell.x < gridWidth &&
                    cell.y >= 0 && cell.y < gridHeight &&
                    cell.z >= 0 && cell.z < gridDepth)
                {
                    grid[cell.x, cell.y, cell.z] = blockObj.transform;
                }
            }
        }
    }

    bool ClearHorizontalLines()
    {
        bool clearedAny = false;
        HashSet<Vector3Int> cellsToClear = new HashSet<Vector3Int>();
        HashSet<GameObject> groupsToDetach = new HashSet<GameObject>();

        for (int y = 0; y < gridHeight; y++)
        {
            for (int z = 0; z < gridDepth; z++)
            {
                bool complete = true;
                for (int x = 0; x < gridWidth; x++)
                {
                    if (grid[x, y, z] == null)
                    {
                        complete = false;
                        break;
                    }
                }
                if (complete)
                {
                    for (int x = 0; x < gridWidth; x++)
                        cellsToClear.Add(new Vector3Int(x, y, z));
                }
            }
        }
        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                bool complete = true;
                for (int z = 0; z < gridDepth; z++)
                {
                    if (grid[x, y, z] == null)
                    {
                        complete = false;
                        break;
                    }
                }
                if (complete)
                {
                    for (int z = 0; z < gridDepth; z++)
                        cellsToClear.Add(new Vector3Int(x, y, z));
                }
            }
        }
        foreach (Vector3Int cell in cellsToClear)
        {
            if (grid[cell.x, cell.y, cell.z] != null)
            {
                Transform block = grid[cell.x, cell.y, cell.z];
                GameObject parentGroup = (block.parent != null) ? block.parent.gameObject : null;
                Destroy(block.gameObject);
                grid[cell.x, cell.y, cell.z] = null;
                clearedAny = true;
                if (parentGroup != null && lockedGroups.Contains(parentGroup))
                    groupsToDetach.Add(parentGroup);
            }
        }
        foreach (GameObject group in groupsToDetach)
        {
            List<Transform> children = new List<Transform>();
            foreach (Transform child in group.transform)
                children.Add(child);
            foreach (Transform child in children)
            {
                child.SetParent(null);
                child.gameObject.tag = "Block";
            }
            lockedGroups.Remove(group);
            Destroy(group);
        }
        return clearedAny;
    }

    bool ApplyIterativeGravity()
    {
        bool anyMoved = false;
        List<GameObject> groupsThatCanMove = new List<GameObject>();
        foreach (GameObject group in lockedGroups)
        {
            if (IsValidVirtualPosition(group, group.transform.position + Vector3.down * cubeSize))
                groupsThatCanMove.Add(group);
        }
        List<Transform> detachedThatCanMove = new List<Transform>();
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                for (int z = 0; z < gridDepth; z++)
                {
                    if (grid[x, y, z] != null)
                    {
                        Transform block = grid[x, y, z];
                        if (block.parent == null)
                        {
                            if (IsValidVirtualPositionForBlock(block, block.position + Vector3.down * cubeSize))
                                detachedThatCanMove.Add(block);
                        }
                    }
                }
            }
        }
        if (groupsThatCanMove.Count > 0 || detachedThatCanMove.Count > 0)
        {
            foreach (GameObject group in groupsThatCanMove)
                group.transform.position += Vector3.down * cubeSize;
            foreach (Transform block in detachedThatCanMove)
                block.position += Vector3.down * cubeSize;
            anyMoved = true;
            RebuildGrid();
        }
        return anyMoved;
    }

    bool IsValidVirtualPositionForBlock(Transform block, Vector3 candidatePos)
    {
        Vector3Int cell = WorldToGrid(candidatePos);
        if (cell.x < 0 || cell.x >= gridWidth || cell.z < 0 || cell.z >= gridDepth)
            return false;
        if (cell.y < 0)
            return false;
        if (cell.y < gridHeight && grid[cell.x, cell.y, cell.z] != null && grid[cell.x, cell.y, cell.z] != block)
            return false;
        return true;
    }

    bool IsValidVirtualPosition(GameObject piece, Vector3 candidatePos)
    {
        foreach (Transform block in piece.transform)
        {
            Vector3 worldPos = candidatePos + piece.transform.rotation * block.localPosition;
            Vector3Int cell = WorldToGrid(worldPos);
            Debug.Log($"[VirtualPos] Checking block of {piece.name} at candidatePos {worldPos} (cell {cell})");
            if (cell.x < 0 || cell.x >= gridWidth || cell.z < 0 || cell.z >= gridDepth)
                return false;
            if (cell.y < 0)
                return false;
            if (cell.y < gridHeight && grid[cell.x, cell.y, cell.z] != null)
            {
                if (!grid[cell.x, cell.y, cell.z].IsChildOf(piece.transform))
                {
                    Debug.Log($"[VirtualPos] Cell {cell} is occupied by {grid[cell.x, cell.y, cell.z].name} (not part of {piece.name})");
                    return false;
                }
            }
        }
        return true;
    }

    void UpdateGhostPiece()
    {
        if (currentPiece == null)
        {
            if (ghostPiece != null)
            {
                Destroy(ghostPiece);
                ghostPiece = null;
            }
            return;
        }
        if (ghostPiece == null)
        {
            ghostPiece = Instantiate(currentPiece);
            MakeGhost(ghostPiece);
        }
        ghostPiece.transform.rotation = currentPiece.transform.rotation;
        Vector3 ghostPos = CalculateGhostPosition(currentPiece);
        ghostPiece.transform.position = ghostPos;
        Transform lowestGhostChild = null;
        float lowestGhostY = float.MaxValue;
        foreach (Transform child in ghostPiece.transform)
        {
            if (child.position.y < lowestGhostY)
            {
                lowestGhostY = child.position.y;
                lowestGhostChild = child;
            }
        }
        if (lowestGhostChild != null)
        {
            Vector3 childWorldPos = lowestGhostChild.position;
            Vector3Int cell = WorldToGrid(childWorldPos);
            Debug.Log("Ghost Piece - Lowest block " + lowestGhostChild.name + " world position: " + childWorldPos + " falls into grid cell: " + cell);
        }
    }

    Vector3 CalculateGhostPosition(GameObject piece)
    {
        Vector3 candidatePos = piece.transform.position;
        while (IsValidVirtualPosition(piece, candidatePos))
        {
            candidatePos += Vector3.down * cubeSize;
        }
        candidatePos += Vector3.up * cubeSize;
        return candidatePos;
    }

    void MakeGhost(GameObject ghost)
    {
        Renderer[] renderers = ghost.GetComponentsInChildren<Renderer>();
        foreach (Renderer rend in renderers)
        {
            Material mat = new Material(rend.material);
            Color c = mat.color;
            c.a = 0.4f;
            mat.color = c;
            rend.material = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
        }
    }

    void SlamPiece()
    {
        if (currentPiece != null)
        {
            Vector3 slamPos = CalculateGhostPosition(currentPiece);
            currentPiece.transform.position = slamPos;
            LockPiece();
        }
    }

    void SpawnNewPiece()
    {
        if (fallingPiecePrefabs == null || fallingPiecePrefabs.Length == 0)
        {
            Debug.LogError("No falling piece prefabs specified!");
            return;
        }
        int randomIndex = Random.Range(0, fallingPiecePrefabs.Length);
        GameObject selectedPrefab = fallingPiecePrefabs[randomIndex];
        Vector3 spawnPosition = new Vector3(0, gridHeight * cubeSize + cubeSize, 0);
        currentPiece = Instantiate(selectedPrefab, spawnPosition, Quaternion.identity);
        SnapPieceHorizontally();
        SnapPieceVertically();
    }
}
