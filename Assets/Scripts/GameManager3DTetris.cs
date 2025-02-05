using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class GameManager3DTetris : MonoBehaviour
{
    [Header("References & Settings")]
    [Tooltip("Reference to the FrameGridManager that defines the container dimensions.")]
    public FrameGridManager frameGrid;
    [Tooltip("Prefab for the falling piece (its children should be the individual cubes).")]
    public GameObject fallingPiecePrefab;
    [Tooltip("Time (in seconds) between downward moves when not soft-dropping.")]
    public float fallInterval = 1f;
    [Tooltip("Multiplier to reduce the fall interval when soft-dropping (holding Down Arrow).")]
    public float softDropMultiplier = 0.2f;

    // Grid dimensions and cell size.
    private int gridWidth;
    private int gridHeight;
    private int gridDepth;
    private float cubeSize;
    // The grid stores a reference to the block (Transform) that occupies each cell.
    private Transform[,,] grid;

    // The currently falling (user-controlled) piece.
    private GameObject currentPiece;
    private float fallTimer = 0f;

    // List of locked groups. When a piece locks, its blocks remain grouped under one GameObject.
    private List<GameObject> lockedGroups = new List<GameObject>();

    // While processing locked blocks (line clears and gravity), user input is disabled.
    private bool processingLockedBlocks = false;

    void Start()
    {
        if (frameGrid == null)
        {
            Debug.LogError("FrameGridManager reference not set on GameManager3DTetris!");
            return;
        }

        cubeSize = frameGrid.cubeSize;
        // Compute grid dimensions (assuming container dimensions are multiples of cubeSize).
        gridWidth = Mathf.RoundToInt(frameGrid.containerWidth / cubeSize);
        gridDepth = Mathf.RoundToInt(frameGrid.containerDepth / cubeSize);
        gridHeight = Mathf.RoundToInt(frameGrid.containerHeight / cubeSize);

        grid = new Transform[gridWidth, gridHeight, gridDepth];

        SpawnNewPiece();
    }

    void Update()
    {
        // Do not process user input while locked blocks are being processed.
        if (processingLockedBlocks)
            return;

        if (currentPiece != null)
        {
            HandleInput();

            // Determine the effective fall interval (soft drop if Down Arrow is held).
            float effectiveFallInterval = fallInterval;
            if (Input.GetKey(KeyCode.DownArrow))
                effectiveFallInterval = fallInterval * softDropMultiplier;

            fallTimer += Time.deltaTime;
            if (fallTimer >= effectiveFallInterval)
            {
                fallTimer = 0f;
                // Try to move the piece one cell down.
                MovePiece(Vector3.down * cubeSize);
            }
        }
    }

    /// <summary>
    /// Handles left/right movement input. (Shift–modified arrow keys are reserved for camera control.)
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
    }

    /// <summary>
    /// Attempts to move the current piece by the given vector.
    /// If the move is invalid (out-of-bounds or colliding), it is reverted.
    /// A failed downward move results in locking the piece.
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
    }

    /// <summary>
    /// Checks that every block in the given piece is within the playable area.
    /// For X and Z, valid cells have indices ≥ 1 (reserving index 0 for the frame).
    /// For Y, blocks below the container (cell.y < 0) are disallowed,
    /// but blocks above (cell.y ≥ gridHeight) are allowed.
    /// Also, if a cell within the container is already occupied, the position is invalid.
    /// </summary>
    bool IsValidPosition(GameObject piece)
    {
        foreach (Transform block in piece.transform)
        {
            Vector3Int cell = WorldToGrid(block.position);
            if (cell.x < 1 || cell.x >= gridWidth || cell.z < 1 || cell.z >= gridDepth)
                return false;
            if (cell.y < 0)
                return false;
            if (cell.y < gridHeight && grid[cell.x, cell.y, cell.z] != null)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Converts a world position to grid coordinates.
    /// Assumes the container extends in X from -containerWidth/2 to +containerWidth/2,
    /// in Z from -containerDepth/2 to +containerDepth/2.
    /// </summary>
    Vector3Int WorldToGrid(Vector3 pos)
    {
        int x = Mathf.FloorToInt((pos.x + frameGrid.containerWidth / 2f) / cubeSize);
        int y = Mathf.FloorToInt((pos.y + cubeSize / 2f) / cubeSize);
        int z = Mathf.FloorToInt((pos.z + frameGrid.containerDepth / 2f) / cubeSize);
        return new Vector3Int(x, y, z);
    }

    /// <summary>
    /// Converts grid coordinates back into a world-space position (the cell center).
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
    /// The piece remains grouped so that its blocks move as one unit,
    /// and the group is added to the list of locked groups.
    /// Then processing of line clears and gravity begins.
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
        StartCoroutine(ProcessLockedBlocks());
    }

    /// <summary>
    /// Processes locked blocks in separate transactions:
    /// (A) Clear all complete horizontal lines (X and Z axes only).
    /// (B) Then apply gravity to intact groups.
    /// (C) Then apply gravity to detached blocks.
    /// These steps are repeated until no further changes occur.
    /// During this processing, no new piece is spawned and user input is paused.
    /// </summary>
    IEnumerator ProcessLockedBlocks()
    {
        processingLockedBlocks = true;
        bool changesMade;
        do
        {
            changesMade = false;

            // (A) Clear horizontal lines.
            if (ClearHorizontalLines())
            {
                changesMade = true;
                yield return new WaitForSeconds(0.5f);
            }

            // (B) Apply gravity to intact locked groups.
            if (ApplyGravityToLockedGroups())
            {
                changesMade = true;
                yield return new WaitForSeconds(0.5f);
            }

            // (C) Apply gravity to detached blocks.
            if (ApplyGravityToDetachedBlocks())
            {
                changesMade = true;
                yield return new WaitForSeconds(0.5f);
            }

        } while (changesMade);

        processingLockedBlocks = false;
        SpawnNewPiece();
    }

    /// <summary>
    /// Checks for complete horizontal lines along the X and Z axes (for each Y level)
    /// and clears them. When any block in a group is cleared, the entire group is detached.
    /// </summary>
    bool ClearHorizontalLines()
    {
        bool clearedAny = false;
        HashSet<Vector3Int> cellsToClear = new HashSet<Vector3Int>();
        HashSet<GameObject> groupsToDetach = new HashSet<GameObject>();

        // Check rows along the X-axis (for each fixed Y and Z in the playable area).
        for (int y = 0; y < gridHeight; y++)
        {
            for (int z = 1; z < gridDepth; z++)
            {
                bool complete = true;
                for (int x = 1; x < gridWidth; x++)
                {
                    if (grid[x, y, z] == null)
                    {
                        complete = false;
                        break;
                    }
                }
                if (complete)
                {
                    for (int x = 1; x < gridWidth; x++)
                    {
                        cellsToClear.Add(new Vector3Int(x, y, z));
                    }
                }
            }
        }

        // Check rows along the Z-axis (for each fixed Y and X in the playable area).
        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 1; x < gridWidth; x++)
            {
                bool complete = true;
                for (int z = 1; z < gridDepth; z++)
                {
                    if (grid[x, y, z] == null)
                    {
                        complete = false;
                        break;
                    }
                }
                if (complete)
                {
                    for (int z = 1; z < gridDepth; z++)
                    {
                        cellsToClear.Add(new Vector3Int(x, y, z));
                    }
                }
            }
        }

        // Clear all marked cells.
        foreach (Vector3Int cell in cellsToClear)
        {
            if (grid[cell.x, cell.y, cell.z] != null)
            {
                Transform block = grid[cell.x, cell.y, cell.z];
                // If this block belongs to a locked group, mark that group for detachment.
                if (block.parent != null)
                {
                    GameObject parentGroup = block.parent.gameObject;
                    if (lockedGroups.Contains(parentGroup))
                    {
                        groupsToDetach.Add(parentGroup);
                    }
                }
                Destroy(block.gameObject);
                grid[cell.x, cell.y, cell.z] = null;
                clearedAny = true;
            }
        }

        // Detach entire groups for which any block was cleared.
        foreach (GameObject group in groupsToDetach)
        {
            // Detach all remaining blocks.
            List<Transform> children = new List<Transform>();
            foreach (Transform child in group.transform)
            {
                children.Add(child);
            }
            foreach (Transform child in children)
            {
                // Detach by setting the parent to null (or to an optional container if desired).
                child.SetParent(null);
            }
            // Remove the group from the lockedGroups list and destroy its container.
            lockedGroups.Remove(group);
            Destroy(group);
        }

        return clearedAny;
    }

    /// <summary>
    /// For each intact locked group, if every block in the group has an unoccupied cell immediately below,
    /// the entire group is moved downward by one cell. The grid is updated accordingly.
    /// </summary>
    bool ApplyGravityToLockedGroups()
    {
        bool movedAny = false;
        foreach (GameObject group in lockedGroups.ToArray())
        {
            bool canFall = true;
            foreach (Transform block in group.transform)
            {
                Vector3Int cell = WorldToGrid(block.position);
                if (cell.y == 0)
                {
                    canFall = false;
                    break;
                }
                Vector3Int below = new Vector3Int(cell.x, cell.y - 1, cell.z);
                if (below.y >= 0 && below.y < gridHeight)
                {
                    if (grid[below.x, below.y, below.z] != null)
                    {
                        // Allow fall only if the block below belongs to the same group.
                        Transform belowBlock = grid[below.x, below.y, below.z];
                        if (belowBlock.parent != group.transform)
                        {
                            canFall = false;
                            break;
                        }
                    }
                }
            }
            if (canFall)
            {
                group.transform.position += Vector3.down * cubeSize;
                // Update grid for each block in the group.
                foreach (Transform block in group.transform)
                {
                    Vector3Int newCell = WorldToGrid(block.position);
                    grid[newCell.x, newCell.y, newCell.z] = block;
                }
                movedAny = true;
            }
        }
        return movedAny;
    }

    /// <summary>
    /// Iterates over all grid cells. For any block that is not part of a locked group (i.e. detached),
    /// if the cell below is free, move the block down by one cell and update the grid.
    /// </summary>
    bool ApplyGravityToDetachedBlocks()
    {
        bool movedAny = false;
        for (int x = 1; x < gridWidth; x++)
        {
            for (int y = 1; y < gridHeight; y++) // bottom row (y = 0) cannot fall.
            {
                for (int z = 1; z < gridDepth; z++)
                {
                    if (grid[x, y, z] != null)
                    {
                        Transform block = grid[x, y, z];
                        // If the block is still part of a locked group, skip it.
                        bool inGroup = (block.parent != null && lockedGroups.Contains(block.parent.gameObject));
                        if (!inGroup)
                        {
                            Vector3Int cell = new Vector3Int(x, y, z);
                            if (cell.y > 0)
                            {
                                Vector3Int below = new Vector3Int(x, y - 1, z);
                                if (grid[below.x, below.y, below.z] == null)
                                {
                                    block.position += Vector3.down * cubeSize;
                                    grid[x, y, z] = null;
                                    grid[below.x, below.y, below.z] = block;
                                    movedAny = true;
                                }
                            }
                        }
                    }
                }
            }
        }
        return movedAny;
    }

    /// <summary>
    /// Spawns a new falling piece at the top-center of the container.
    /// Uses the FrameGridManager's GridCenter for the X and Z coordinates.
    /// </summary>
    void SpawnNewPiece()
    {
        Vector3 spawnPosition = new Vector3(
            frameGrid.GridCenter.x,
            gridHeight * cubeSize + cubeSize, // high enough above the container.
            frameGrid.GridCenter.z);
        currentPiece = Instantiate(fallingPiecePrefab, spawnPosition, Quaternion.identity);
    }
}
