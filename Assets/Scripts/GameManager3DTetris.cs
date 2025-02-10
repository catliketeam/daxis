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

    // Playable area dimensions.
    private float playableWidth;
    private float playableDepth;

    // Grid to store block occupancy.
    private Transform[,,] grid;

    // Active piece.
    private GameObject currentPiece;
    private float fallTimer = 0f;

    // Locked groups.
    private List<GameObject> lockedGroups = new List<GameObject>();

    // Input processing flag.
    private bool processingLockedBlocks = false;

    // Ghost piece.
    private GameObject ghostPiece;

    // MOBILE INPUT VARIABLES (for piece movement)
    private Vector2 touchStartPos;
    public float swipeThreshold = 100f;

    // For press-and-hold slam gesture.
    private float touchStartTime = 0f;
    private bool isSlamTriggered = false;

    // PRIVATE FLAG to ensure two-finger gesture registers only once per gesture.
    private bool twoFingerSwipeRegistered = false;

    // PUBLIC ACCESSORS
    public int GridHeight { get { return gridHeight; } }
    public Vector3 GridCenter { get { return new Vector3(0, (gridHeight * cubeSize) / 2f, 0); } }
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
    public GameObject GhostPiece { get { return ghostPiece; } }

    // LogGridState: Logs the occupancy of the grid to the Unity console.
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

    // --- Modified Mobile Input Handling ---
    void HandleInput()
    {
        if (Application.isMobilePlatform)
        {
            // Process two-finger gesture for camera flip.
            if (Input.touchCount >= 2)
            {
                if (!twoFingerSwipeRegistered)
                {
                    Touch touch0 = Input.GetTouch(0);
                    Touch touch1 = Input.GetTouch(1);
                    if (touch0.phase == TouchPhase.Moved && touch1.phase == TouchPhase.Moved)
                    {
                        float avgDeltaX = (touch0.deltaPosition.x + touch1.deltaPosition.x) / 2f;
                        if (Mathf.Abs(avgDeltaX) >= 50f) // Threshold; adjust as needed.
                        {
                            CameraController camController = Camera.main.GetComponent<CameraController>();
                            if (camController != null)
                            {
                                float flipDelta = (avgDeltaX > 0) ? camController.discreteYawStep : -camController.discreteYawStep;
                                camController.EnqueueCameraFlip(flipDelta);
                            }
                            twoFingerSwipeRegistered = true;
                        }
                    }
                }
                return; // Do not process one-finger gestures if two or more touches are active.
            }
            else
            {
                // Reset flag when fewer than two touches.
                twoFingerSwipeRegistered = false;
            }

            // Process one-finger gesture.
            if (Input.touchCount == 1)
            {
                Touch touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Began)
                {
                    touchStartPos = touch.position;
                    touchStartTime = Time.time;
                    isSlamTriggered = false;
                }
                // Check for press-and-hold slam gesture.
                else if ((touch.phase == TouchPhase.Stationary || (touch.phase == TouchPhase.Moved && (touch.position - touchStartPos).magnitude < 10f)) && !isSlamTriggered)
                {
                    if (Time.time - touchStartTime >= 1.0f) // 1-second hold threshold.
                    {
                        SlamPiece();
                        isSlamTriggered = true;
                    }
                }
                else if (touch.phase == TouchPhase.Ended)
                {
                    // If slam already triggered via hold, do nothing.
                    if (isSlamTriggered)
                        return;

                    Vector2 delta = touch.position - touchStartPos;
                    // Instead of slamming, vertical swipes now move the piece along the depth axis.
                    if (Mathf.Abs(delta.y) > Mathf.Abs(delta.x) && Mathf.Abs(delta.y) >= swipeThreshold)
                    {
                        // Compute effective depth: project the camera's forward vector onto the XZ plane.
                        Vector3 effectiveDepth = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
                        if (delta.y > 0)
                            MovePiece(effectiveDepth * cubeSize);
                        else
                            MovePiece(-effectiveDepth * cubeSize);
                    }
                    // Horizontal swipe: move the piece.
                    else if (Mathf.Abs(delta.x) >= swipeThreshold && Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
                    {
                        CameraController camController = Camera.main.GetComponent<CameraController>();
                        Vector3 effectiveRight = (camController != null) ? camController.EffectiveRight : Camera.main.transform.right;
                        effectiveRight.y = 0;
                        effectiveRight.Normalize();
                        if (delta.x > 0)
                            MovePiece(effectiveRight * cubeSize);
                        else
                            MovePiece(-effectiveRight * cubeSize);
                    }
                    // Minimal movement: treat as a tap for rotation.
                    else if (Mathf.Abs(delta.x) < 10f && Mathf.Abs(delta.y) < 10f)
                    {
                        if (singleTapCoroutine == null)
                            singleTapCoroutine = StartCoroutine(HandleSingleTap());
                    }
                }
            }
        }
        else
        {
            // Desktop Input.
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                return;
            CameraController camController = Camera.main.GetComponent<CameraController>();
            Vector3 effectiveRight = (camController != null) ? camController.EffectiveRight : Camera.main.transform.right;
            effectiveRight.y = 0;
            effectiveRight.Normalize();
            if (Input.GetKeyDown(KeyCode.LeftArrow))
                MovePiece(-effectiveRight * cubeSize);
            else if (Input.GetKeyDown(KeyCode.RightArrow))
                MovePiece(effectiveRight * cubeSize);
            else if (Input.GetKeyDown(KeyCode.UpArrow))
                RotatePiece();
        }
    }

    private Coroutine singleTapCoroutine;
    private IEnumerator HandleSingleTap()
    {
        yield return new WaitForSeconds(0.3f);
        RotatePiece();
        singleTapCoroutine = null;
    }

    void RotatePiece()
    {
        if (currentPiece == null)
            return;
        Quaternion oldRotation = currentPiece.transform.rotation;
        currentPiece.transform.Rotate(0, 90, 0, Space.World);
        SnapPieceHorizontally();
        SnapPieceVertically();
        if (!IsValidPosition(currentPiece))
            currentPiece.transform.rotation = oldRotation;
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
        float newX = Mathf.Round((pos.x + halfPlayableWidth - cubeSize/2f)/cubeSize)*cubeSize - halfPlayableWidth + cubeSize/2f;
        float newZ = Mathf.Round((pos.z + halfPlayableDepth - cubeSize/2f)/cubeSize)*cubeSize - halfPlayableDepth + cubeSize/2f;
        currentPiece.transform.position = new Vector3(newX, pos.y, newZ);
    }

    void SnapPieceVertically()
    {
        Vector3 pos = currentPiece.transform.position;
        float newY = Mathf.Floor(pos.y/cubeSize)*cubeSize;
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
        int x = Mathf.FloorToInt((pos.x + halfPlayableWidth)/cubeSize);
        int y = Mathf.FloorToInt(pos.y/cubeSize);
        int z = Mathf.FloorToInt((pos.z + halfPlayableDepth)/cubeSize);
        return new Vector3Int(x, y, z);
    }

    Vector3 GridToWorld(Vector3Int cell)
    {
        float halfPlayableWidth = playableWidth / 2f;
        float halfPlayableDepth = playableDepth / 2f;
        float x = cell.x * cubeSize - halfPlayableWidth + cubeSize/2f;
        float y = cell.y * cubeSize;
        float z = cell.z * cubeSize - halfPlayableDepth + cubeSize/2f;
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
        }
        while (changesMade);
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
