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
    private int gridWidth;    // Number of playable cells in X (should be 9)
    private int gridHeight;   // Vertical cell count
    private int gridDepth;    // Number of playable cells in Z (should be 9)
    private float cubeSize;

    // Playable area dimensions (inside the frame).
    // For example, if containerWidth and containerDepth in FrameGridManager are 11 and cubeSize is 1, playableWidth and playableDepth become 9.
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

    void Start()
    {
        if (frameGrid == null)
        {
            Debug.LogError("FrameGridManager reference not set on GameManager3DTetris!");
            return;
        }

        cubeSize = frameGrid.cubeSize;
        // For a 9×9 playable grid, containerWidth and containerDepth in FrameGridManager should be 11.
        playableWidth = frameGrid.containerWidth - 2 * cubeSize;
        playableDepth = frameGrid.containerDepth - 2 * cubeSize;

        gridWidth = Mathf.RoundToInt(playableWidth / cubeSize);  // Expected to be 9.
        gridDepth = Mathf.RoundToInt(playableDepth / cubeSize);    // Expected to be 9.
        gridHeight = Mathf.RoundToInt(frameGrid.containerHeight / cubeSize);

        grid = new Transform[gridWidth, gridHeight, gridDepth];

        SpawnNewPiece();
    }

    void Update()
    {
        // Press G to log grid occupancy.
        if (Input.GetKeyDown(KeyCode.G))
        {
            LogGridState();
        }

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
    /// Each layer (Y value) is printed from top (highest Y) to bottom (Y = 0).
    /// "X" indicates a locked cell, "A" indicates a block from the active piece, and "G" indicates a ghost block.
    /// Empty cells are indicated by ".".
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
    /// Handles left/right movement and rotation input.
    /// Up arrow triggers a 90° rotation clockwise about the X axis.
    /// (Shift-modified keys remain reserved for camera control.)
    /// </summary>
    void HandleInput()
    {
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            return;

        Vector3 cameraRight = Camera.main.transform.right;
        cameraRight.y = 0;
        cameraRight.Normalize();

        if (Input.GetKeyDown(KeyCode.LeftArrow))
            MovePiece(-cameraRight * cubeSize);
        else if (Input.GetKeyDown(KeyCode.RightArrow))
            MovePiece(cameraRight * cubeSize);
        else if (Input.GetKeyDown(KeyCode.UpArrow))
            RotatePiece();
    }

    /// <summary>
    /// Rotates the active piece 90° clockwise about the X axis.
    /// After rotation, it snaps horizontally and vertically.
    /// The vertical snapping is now performed without a delta adjustment.
    /// If the new orientation is invalid, the rotation is reverted.
    /// </summary>
    void RotatePiece()
    {
        if (currentPiece == null)
            return;

        Quaternion oldRotation = currentPiece.transform.rotation;
        currentPiece.transform.Rotate(90, 0, 0, Space.World);

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

    /// <summary>
    /// Returns the bottom Y coordinate of the piece by combining the bounds of all its renderers.
    /// </summary>
    float GetPieceBottomUsingRenderers(GameObject piece)
    {
        Renderer[] renderers = piece.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return piece.transform.position.y;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }
        return bounds.min.y;
    }

    /// <summary>
    /// Moves the active piece by the specified vector.
    /// If the move is invalid, it is reverted.
    /// A failed downward move locks the piece.
    /// Horizontal moves are followed by snapping to playable grid cell centers.
    /// </summary>
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

    /// <summary>
    /// Snaps the active piece's X and Z coordinates to the nearest playable grid cell center.
    /// The playable area spans from -playableWidth/2 to +playableWidth/2.
    /// </summary>
    void SnapPieceHorizontally()
    {
        Vector3 pos = currentPiece.transform.position;
        float halfPlayableWidth = playableWidth / 2f;
        float halfPlayableDepth = playableDepth / 2f;

        float newX = Mathf.Round((pos.x + halfPlayableWidth - cubeSize / 2f) / cubeSize) * cubeSize - halfPlayableWidth + cubeSize / 2f;
        float newZ = Mathf.Round((pos.z + halfPlayableDepth - cubeSize / 2f) / cubeSize) * cubeSize - halfPlayableDepth + cubeSize / 2f;
        currentPiece.transform.position = new Vector3(newX, pos.y, newZ);
    }

    /// <summary>
    /// Snaps the active piece's Y coordinate to the nearest grid cell center vertically.
    /// </summary>
    void SnapPieceVertically()
    {
        Vector3 pos = currentPiece.transform.position;
        float newY = Mathf.Round((pos.y + cubeSize / 2f) / cubeSize) * cubeSize - cubeSize / 2f;
        currentPiece.transform.position = new Vector3(pos.x, newY, pos.z);
    }

    /// <summary>
    /// Checks that every block in the active piece is within the playable area.
    /// </summary>
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

    /// <summary>
    /// Converts a world-space position to playable grid coordinates.
    /// The playable area spans from -playableWidth/2 to +playableWidth/2 (and similarly for Z).
    /// </summary>
    Vector3Int WorldToGrid(Vector3 pos)
    {
        float halfPlayableWidth = playableWidth / 2f;
        float halfPlayableDepth = playableDepth / 2f;
        int x = Mathf.FloorToInt((pos.x + halfPlayableWidth) / cubeSize);
        int y = Mathf.FloorToInt((pos.y + cubeSize / 2f) / cubeSize);
        int z = Mathf.FloorToInt((pos.z + halfPlayableDepth) / cubeSize);
        return new Vector3Int(x, y, z);
    }

    /// <summary>
    /// Converts playable grid coordinates back to a world-space position (the cell center).
    /// </summary>
    Vector3 GridToWorld(Vector3Int cell)
    {
        float halfPlayableWidth = playableWidth / 2f;
        float halfPlayableDepth = playableDepth / 2f;
        float x = cell.x * cubeSize - halfPlayableWidth + cubeSize / 2f;
        float y = cell.y * cubeSize;
        float z = cell.z * cubeSize - halfPlayableDepth + cubeSize / 2f;
        return new Vector3(x, y, z);
    }

    /// <summary>
    /// Locks the active piece by recording its blocks in the grid and adding it to lockedGroups,
    /// then begins line-clear and gravity processing.
    /// </summary>
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

    /// <summary>
    /// Processes locked pieces by clearing complete lines and applying gravity, then spawns a new piece.
    /// </summary>
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

    /// <summary>
    /// Rebuilds the grid array from locked groups and detached blocks (tagged "Block").
    /// </summary>
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

    /// <summary>
    /// Checks for complete horizontal lines along the playable X and Z axes at each Y level and clears them.
    /// Only blocks in cleared cells are destroyed; groups that lose blocks are detached.
    /// </summary>
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

    /// <summary>
    /// Applies gravity iteratively until no further movement occurs.
    /// </summary>
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
            Vector3 worldPos = candidatePos + block.localPosition;
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
        // Spawn the new piece at the center of the playable area (X and Z = 0).
        Vector3 spawnPosition = new Vector3(0, gridHeight * cubeSize + cubeSize, 0);
        currentPiece = Instantiate(selectedPrefab, spawnPosition, Quaternion.identity);
        SnapPieceHorizontally();
        SnapPieceVertically();
    }
}
