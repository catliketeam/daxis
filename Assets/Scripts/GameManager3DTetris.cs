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
    private int gridWidth;
    private int gridHeight;
    private int gridDepth;
    private float cubeSize;
    // The grid stores a reference to the block (Transform) occupying each cell.
    private Transform[,,] grid;

    // The currently falling (user-controlled) piece.
    private GameObject currentPiece;
    private float fallTimer = 0f;

    // List of locked groups. When a piece locks, its blocks remain grouped.
    private List<GameObject> lockedGroups = new List<GameObject>();

    // Flag to disable user input while processing locked blocks.
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
        // Compute grid dimensions (assuming container dimensions are exact multiples of cubeSize).
        gridWidth = Mathf.RoundToInt(frameGrid.containerWidth / cubeSize);
        gridDepth = Mathf.RoundToInt(frameGrid.containerDepth / cubeSize);
        gridHeight = Mathf.RoundToInt(frameGrid.containerHeight / cubeSize);

        grid = new Transform[gridWidth, gridHeight, gridDepth];

        SpawnNewPiece();
    }

    void Update()
    {
        // Disable user input while processing locked blocks.
        if (processingLockedBlocks)
            return;

        if (currentPiece != null)
        {
            HandleInput();

            // Update ghost piece so that it always shows the landing position.
            UpdateGhostPiece();

            // Check for slam input (spacebar; add mobile swipe detection as needed).
            if (Input.GetKeyDown(KeyCode.Space))
            {
                SlamPiece();
                return; // Skip further processing this frame.
            }

            // Determine effective fall interval (use soft drop if Down Arrow is held).
            float effectiveFallInterval = fallInterval;
            if (Input.GetKey(KeyCode.DownArrow))
                effectiveFallInterval = fallInterval * softDropMultiplier;

            fallTimer += Time.deltaTime;
            if (fallTimer >= effectiveFallInterval)
            {
                fallTimer = 0f;
                // Attempt to move the piece one cell down.
                MovePiece(Vector3.down * cubeSize);
            }
        }
        else
        {
            // No current piece? Ensure the ghost piece is removed.
            if (ghostPiece != null)
            {
                Destroy(ghostPiece);
                ghostPiece = null;
            }
        }
    }

    /// <summary>
    /// Handles left/right movement input.
    /// (Shift-modified arrow keys are reserved for camera control.)
    /// </summary>
    void HandleInput()
    {
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            return;

        // Get the camera's horizontal right vector (projected onto the horizontal plane).
        Vector3 cameraRight = Camera.main.transform.right;
        cameraRight.y = 0;
        cameraRight.Normalize();

        if (Input.GetKeyDown(KeyCode.LeftArrow))
            MovePiece(-cameraRight * cubeSize);
        else if (Input.GetKeyDown(KeyCode.RightArrow))
            MovePiece(cameraRight * cubeSize);
    }

    /// <summary>
    /// Attempts to move the current piece by the given vector.
    /// If the move is invalid (e.g., out-of-bounds or colliding), it is reverted.
    /// If a downward move is invalid, the piece is locked.
    /// For horizontal moves, the piece's position is snapped to grid cells.
    /// </summary>
    void MovePiece(Vector3 move)
    {
        currentPiece.transform.position += move;
        if (!IsValidPosition(currentPiece))
        {
            currentPiece.transform.position -= move;
            if (move == Vector3.down * cubeSize)
            {
                LockPiece();
            }
        }
        else
        {
            // For horizontal moves (non-vertical), snap the piece's X and Z coordinates to the grid.
            if (move != Vector3.down * cubeSize)
            {
                SnapPieceHorizontally();
            }
        }
    }

    /// <summary>
    /// Snaps the current piece's X and Z coordinates to the nearest grid cell center.
    /// The Y coordinate remains unchanged.
    /// </summary>
    void SnapPieceHorizontally()
    {
        Vector3 pos = currentPiece.transform.position;
        float newX = Mathf.Round((pos.x + frameGrid.containerWidth / 2f - cubeSize / 2f) / cubeSize) * cubeSize - frameGrid.containerWidth / 2f + cubeSize / 2f;
        float newZ = Mathf.Round((pos.z + frameGrid.containerDepth / 2f - cubeSize / 2f) / cubeSize) * cubeSize - frameGrid.containerDepth / 2f + cubeSize / 2f;
        currentPiece.transform.position = new Vector3(newX, pos.y, newZ);
    }

    /// <summary>
    /// Checks that every block in the piece is within the playable area.
    /// For X and Z, valid cells have indices ≥ 0.
    /// For Y, blocks below the container (cell.y < 0) are invalid; blocks above (cell.y ≥ gridHeight) are allowed.
    /// Also, if a cell within the container is already occupied, the position is invalid.
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
    /// Converts a world-space position to grid coordinates.
    /// Assumes the container extends in X from -containerWidth/2 to +containerWidth/2 and in Z from -containerDepth/2 to +containerDepth/2.
    /// </summary>
    Vector3Int WorldToGrid(Vector3 pos)
    {
        int x = Mathf.FloorToInt((pos.x + frameGrid.containerWidth / 2f) / cubeSize);
        int y = Mathf.FloorToInt((pos.y + cubeSize / 2f) / cubeSize);
        int z = Mathf.FloorToInt((pos.z + frameGrid.containerDepth / 2f) / cubeSize);
        return new Vector3Int(x, y, z);
    }

    /// <summary>
    /// Converts grid coordinates back to a world-space position (the cell center).
    /// </summary>
    Vector3 GridToWorld(Vector3Int cell)
    {
        float x = cell.x * cubeSize - frameGrid.containerWidth / 2f + cubeSize / 2f;
        float y = cell.y * cubeSize;
        float z = cell.z * cubeSize - frameGrid.containerDepth / 2f + cubeSize / 2f;
        return new Vector3(x, y, z);
    }

    /// <summary>
    /// Locks the current falling piece in place.
    /// Its blocks remain grouped, and the group is added to lockedGroups.
    /// Then processing (line clearing and gravity) begins.
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
    /// Processes locked blocks in two phases:
    /// (A) Clear all complete horizontal lines (X and Z axes).
    /// (B) Apply iterative gravity in a single transaction—moving all pieces and blocks one cell down at a time until no further movement is possible.
    /// These phases are repeated until no further line clears or falling occur.
    /// During processing, user input is paused and no new piece is spawned.
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
                // Immediately rebuild the grid after line clears.
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
    /// Rebuilds the grid array by clearing it and then repopulating it from the positions of all locked blocks (both in groups and detached).
    /// Detached blocks are assumed to be tagged as "Block".
    /// </summary>
    void RebuildGrid()
    {
        grid = new Transform[gridWidth, gridHeight, gridDepth];
        // Add blocks from locked groups.
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
        // Also add detached blocks.
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
    /// Checks for complete horizontal lines along the X and Z axes (for each Y level) and clears them.
    /// When any block from a group is cleared, the entire group is detached.
    /// </summary>
    bool ClearHorizontalLines()
    {
        bool clearedAny = false;
        HashSet<Vector3Int> cellsToClear = new HashSet<Vector3Int>();
        HashSet<GameObject> groupsToDetach = new HashSet<GameObject>();

        // Check rows along the X-axis.
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

        // Check rows along the Z-axis.
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
                if (block.parent != null)
                {
                    GameObject parentGroup = block.parent.gameObject;
                    if (lockedGroups.Contains(parentGroup))
                        groupsToDetach.Add(parentGroup);
                }
                Destroy(block.gameObject);
                grid[cell.x, cell.y, cell.z] = null;
                clearedAny = true;
            }
        }

        foreach (GameObject group in groupsToDetach)
        {
            List<Transform> children = new List<Transform>();
            foreach (Transform child in group.transform)
                children.Add(child);
            foreach (Transform child in children)
                child.SetParent(null);
            lockedGroups.Remove(group);
            Destroy(group);
        }

        return clearedAny;
    }

    /// <summary>
    /// Applies gravity iteratively in one transaction. It checks all locked groups and detached blocks
    /// to see if they can move down one cell; if so, they are moved simultaneously and the grid is rebuilt.
    /// Returns true if any object moved.
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
            {
                group.transform.position += Vector3.down * cubeSize;
            }
            foreach (Transform block in detachedThatCanMove)
            {
                block.position += Vector3.down * cubeSize;
            }
            anyMoved = true;
            RebuildGrid();
        }
        return anyMoved;
    }

    /// <summary>
    /// Checks if a single block (treated as a one-cube piece) can be at candidatePos.
    /// </summary>
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

    /// <summary>
    /// Checks if the piece, if positioned at candidatePos (keeping its local positions),
    /// is in a valid position in the grid.
    /// Now, it ignores cells that are occupied by blocks that belong to the same piece.
    /// Debug logging is included to trace cell checks.
    /// </summary>
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

    /// <summary>
    /// Updates the ghost piece to show where the current falling piece would land.
    /// The ghost piece is a visual clone rendered semi-transparently and does not affect game logic.
    /// </summary>
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

    /// <summary>
    /// Calculates the final landing position for a given piece by simulating downward moves.
    /// </summary>
    Vector3 CalculateGhostPosition(GameObject piece)
    {
        Vector3 candidatePos = piece.transform.position;
        while (IsValidVirtualPosition(piece, candidatePos))
        {
            candidatePos += Vector3.down * cubeSize;
        }
        candidatePos += Vector3.up * cubeSize; // last valid position
        return candidatePos;
    }

    /// <summary>
    /// Makes the provided ghost piece semi-transparent.
    /// (Assumes the shader is set up to support transparency; alpha is set to 0.4.)
    /// </summary>
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

    /// <summary>
    /// Instantly drops the current falling piece to its landing position and locks it.
    /// Triggered when the user presses the spacebar (or swipes down on mobile).
    /// </summary>
    void SlamPiece()
    {
        if (currentPiece != null)
        {
            Vector3 slamPos = CalculateGhostPosition(currentPiece);
            currentPiece.transform.position = slamPos;
            LockPiece();
        }
    }

    /// <summary>
    /// Spawns a new falling piece at the top-center of the container.
    /// The X and Z coordinates are snapped to the center of the grid.
    /// A random prefab is selected from the fallingPiecePrefabs array.
    /// </summary>
    void SpawnNewPiece()
    {
        if (fallingPiecePrefabs == null || fallingPiecePrefabs.Length == 0)
        {
            Debug.LogError("No falling piece prefabs specified!");
            return;
        }

        int randomIndex = Random.Range(0, fallingPiecePrefabs.Length);
        GameObject selectedPrefab = fallingPiecePrefabs[randomIndex];
        Vector3 spawnPosition = new Vector3(
            frameGrid.GridCenter.x,
            gridHeight * cubeSize + cubeSize,
            frameGrid.GridCenter.z);
        currentPiece = Instantiate(selectedPrefab, spawnPosition, Quaternion.identity);
        SnapPieceHorizontally();
    }
}
