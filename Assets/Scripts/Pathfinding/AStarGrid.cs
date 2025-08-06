// Adapted from: https://github.com/SebLague/Pathfinding-2D

using UnityEngine;
using System.Collections.Generic;

public class AStarGrid : MonoBehaviour
{
    public bool displayGridGizmos; // Should the grid visualization be displayed in the editor?
    public LayerMask unwalkableMask; // Layer mask defining what constitutes an obstacle.
    public Vector2 gridWorldSize; // The total size of the grid in world units.
    public float gridSize; // The size of each individual node (cell) in the grid.
    public float overlapCircleRadius; // Radius used to check for obstacles around a node's center.

    // Now this will be used to enable diagonal movement
    public bool includeDiagonalNeighbours; // Allow pathfinding to move diagonally between nodes.
    
    // Grid recreation settings
    public bool enablePeriodicGridUpdate = false; // Should the grid be rebuilt periodically?
    public float gridUpdateInterval = 0.5f; // How often to rebuild the grid if periodic updates are enabled.
    private float nextGridUpdateTime = 0f; // Timer for the next periodic grid update.

    // Dynamic obstacle settings
    public bool enableDynamicObstacles = true; // Should the grid account for moving obstacles?
    public float dynamicObstacleUpdateInterval = 0.2f; // How often to check for dynamic obstacles.
    private float nextDynamicObstacleUpdateTime = 0f; // Timer for the next dynamic obstacle update.
    private Dictionary<Collider2D, DynamicObstacleData> dynamicObstacles = new Dictionary<Collider2D, DynamicObstacleData>(); // Stores data about detected dynamic obstacles.

    // Weighted node settings
    public bool enableWeightedNodes = true; // Should different terrain types have movement costs?
    public LayerMask weightedMask; // Layer mask for identifying weighted terrain types.
    public float weightedNodeCost = 2.0f; // Cost multiplier for weighted nodes (Note: This seems overridden by TerrainIdentifier)

    // Corner handling improvements
    public float cornerNodePenalty = 3.0f; // Additional cost applied to nodes identified as corners.
    public float nodeDistanceTolerance = 0.1f; // Tolerance for distance checks, e.g., in corner detection.
    private HashSet<Vector2Int> detectedCorners = new HashSet<Vector2Int>();

    public Node[,] grid;
    float nodeDiameter;
    int gridSizeX, gridSizeY;

    // Class to track dynamic obstacle data
    private class DynamicObstacleData
    { // Holds information about a single dynamic obstacle.
        public Vector2 position; // Current world position.
        public Vector2 velocity; // Current velocity.
        public float radius; // Estimated radius of the obstacle.
        public HashSet<Vector2Int> affectedGridPositions; // Grid cells currently marked as unwalkable by this obstacle.
        public float lastUpdateTime; // Time when this obstacle's data was last updated.

        public DynamicObstacleData(Vector2 pos, Vector2 vel, float rad)
        {
            position = pos;
            velocity = vel;
            radius = rad;
            affectedGridPositions = new HashSet<Vector2Int>();
            lastUpdateTime = Time.time;
        }
    }

    /// <summary>
    /// Called when the script instance is being loaded. Initializes grid parameters.
    /// </summary>
    void Awake()
    {
        nodeDiameter = gridSize * 2;
        gridSizeX = Mathf.RoundToInt(gridWorldSize.x / nodeDiameter);
        gridSizeY = Mathf.RoundToInt(gridWorldSize.y / nodeDiameter);

        CreateGrid();
        DetectCorners(); // Detect corners after creating the grid
    }
    
    /// <summary>
    /// Called every frame. Handles periodic grid updates and dynamic obstacle updates if enabled.
    /// </summary>
    void Update()
    {
        // Check if periodic grid updates are enabled and if it's time to update
        if (enablePeriodicGridUpdate && Time.time >= nextGridUpdateTime)
        {
            CreateGrid();
            nextGridUpdateTime = Time.time + gridUpdateInterval;
        }

        // Update dynamic obstacles
        if (enableDynamicObstacles && Time.time >= nextDynamicObstacleUpdateTime)
        {
            UpdateDynamicObstacles();
            nextDynamicObstacleUpdateTime = Time.time + dynamicObstacleUpdateInterval;
        }
    }

    /// <summary>
    /// Gets the maximum number of nodes the grid can hold.
    /// </summary>
    public int MaxSize
    {
        get
        {
            return gridSizeX * gridSizeY;
        }
    }

    /// <summary>
    /// Creates or recreates the grid of nodes based on the current settings.
    /// </summary>
    public void CreateGrid()
    {
        grid = new Node[gridSizeX, gridSizeY];
        Vector2 worldBottomLeft = (Vector2)transform.position - Vector2.right * gridWorldSize.x / 2 - Vector2.up * gridWorldSize.y / 2;

        for (int x = 0; x < gridSizeX; x++)
        {
            for (int y = 0; y < gridSizeY; y++)
            {
                Vector2 worldPoint = worldBottomLeft + Vector2.right * (x * nodeDiameter + gridSize) + Vector2.up * (y * nodeDiameter + gridSize);

                // Use slightly smaller overlap radius for better corner handling
                bool walkable = (Physics2D.OverlapCircle(worldPoint, overlapCircleRadius * 0.9f, unwalkableMask) == null);
                float weight = 1.0f;
                TerrainType terrainType = TerrainType.Normal;

                // Check for terrain types using OverlapCircle
                Collider2D[] colliders = Physics2D.OverlapCircleAll(worldPoint, overlapCircleRadius);
                foreach (var collider in colliders)
                {
                    // Check unwalkable mask first
                    if ((unwalkableMask.value & (1 << collider.gameObject.layer)) != 0)
                    {
                        walkable = false;
                        break;
                    }

                    TerrainIdentifier terrainIdentifier = collider.GetComponent<TerrainIdentifier>();
                    if (terrainIdentifier != null)
                    {
                        terrainType = terrainIdentifier.terrainType;
                        weight = terrainIdentifier.movementCostMultiplier;
                    }
                }

                // Create the node with the detected terrain type
                grid[x, y] = new Node(walkable, worldPoint, x, y, terrainType, weight);
            }
        }

        // After creating the grid, detect corners again
        DetectCorners();
    }

    /// <summary>
    /// Updates the walkability of nodes based on the current positions and predicted future positions of dynamic obstacles.
    /// </summary>
    private void UpdateDynamicObstacles()
    {
        // Clear previous dynamic obstacle positions
        foreach (var obstacleData in dynamicObstacles.Values)
        {
            foreach (var pos in obstacleData.affectedGridPositions)
            {
                if (InBounds(pos.x, pos.y))
                {
                    grid[pos.x, pos.y].walkable = true;
                }
            }
            obstacleData.affectedGridPositions.Clear();
        }

        // Find and update dynamic obstacles
        Collider2D[] currentObstacles = Physics2D.OverlapAreaAll(
            (Vector2)transform.position - gridWorldSize / 2,
            (Vector2)transform.position + gridWorldSize / 2,
            unwalkableMask
        );

        // Update existing obstacles and remove ones that no longer exist
        var keysToRemove = new List<Collider2D>();
        foreach (var key in dynamicObstacles.Keys)
        {
            if (!System.Array.Exists(currentObstacles, x => x == key))
            {
                keysToRemove.Add(key);
            }
        }
        foreach (var key in keysToRemove)
        {
            dynamicObstacles.Remove(key);
        }

        // Update or add new obstacles
        foreach (var obstacle in currentObstacles)
        {
            if (obstacle.attachedRigidbody != null && obstacle.attachedRigidbody.bodyType != RigidbodyType2D.Static)
            {
                Vector2 currentPos = obstacle.transform.position;
                Vector2 currentVel = obstacle.attachedRigidbody.linearVelocity;
                float radius = obstacle.bounds.extents.magnitude;

                if (dynamicObstacles.ContainsKey(obstacle))
                {
                    // Update existing obstacle
                    var data = dynamicObstacles[obstacle];
                    data.velocity = currentVel;
                    data.position = currentPos;
                    data.lastUpdateTime = Time.time;
                }
                else
                {
                    // Add new obstacle
                    dynamicObstacles[obstacle] = new DynamicObstacleData(currentPos, currentVel, radius);
                }

                // Mark affected grid positions
                MarkObstacleArea(dynamicObstacles[obstacle]);
            }
        }
    }

    /// <summary>
    /// Marks grid nodes within the predicted area of a dynamic obstacle as unwalkable.
    /// </summary>
    /// <param name="obstacleData">Data of the dynamic obstacle.</param>
    private void MarkObstacleArea(DynamicObstacleData obstacleData)
    {
        // Calculate the area that will be affected by the obstacle
        Vector2 predictedPos = obstacleData.position + obstacleData.velocity * dynamicObstacleUpdateInterval;
        float radius = obstacleData.radius;

        // Get grid positions within the obstacle's radius
        Vector2Int centerGridPos = WorldToGridPosition(predictedPos);
        int radiusInGrid = Mathf.CeilToInt(radius / gridSize);

        for (int x = -radiusInGrid; x <= radiusInGrid; x++)
        {
            for (int y = -radiusInGrid; y <= radiusInGrid; y++)
            {
                int checkX = centerGridPos.x + x;
                int checkY = centerGridPos.y + y;

                if (InBounds(checkX, checkY))
                {
                    Vector2 gridWorldPos = grid[checkX, checkY].worldPosition;
                    if (Vector2.Distance(gridWorldPos, predictedPos) <= radius)
                    {
                        grid[checkX, checkY].walkable = false;
                        obstacleData.affectedGridPositions.Add(new Vector2Int(checkX, checkY));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Converts a world position to its corresponding grid coordinates (Vector2Int).
    /// </summary>
    /// <param name="worldPosition">The position in world space.</param>
    /// <returns>The grid coordinates (x, y).</returns>
    public Vector2Int WorldToGridPosition(Vector2 worldPosition)
    {
        float percentX = (worldPosition.x + gridWorldSize.x / 2) / gridWorldSize.x;
        float percentY = (worldPosition.y + gridWorldSize.y / 2) / gridWorldSize.y;
        percentX = Mathf.Clamp01(percentX);
        percentY = Mathf.Clamp01(percentY);

        int x = Mathf.RoundToInt((gridSizeX - 1) * percentX);
        int y = Mathf.RoundToInt((gridSizeY - 1) * percentY);
        return new Vector2Int(x, y);
    }

    /// <summary>
    /// Checks if a specific grid position is currently marked as unwalkable due to a dynamic obstacle.
    /// </summary>
    /// <param name="gridPos">The grid position to check.</param>
    /// <returns>True if the position is affected by a dynamic obstacle, false otherwise.</returns>
    public bool IsInDynamicObstacleArea(Vector2Int gridPos)
    {
        if (!InBounds(gridPos.x, gridPos.y)) return false;

        foreach (var obstacleData in dynamicObstacles.Values)
        {
            if (obstacleData.affectedGridPositions.Contains(gridPos))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Gets the neighboring nodes for a given node. Considers diagonal movement and corner cutting rules.
    /// </summary>
    /// <param name="node">The node to find neighbors for.</param>
    /// <returns>A list of neighboring nodes.</returns>
    public List<Node> GetNeighbours(Node node)
    {
        List<Node> neighbours = new List<Node>();

        // Check all 8 neighboring cells (4 orthogonal + 4 diagonal)
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                // Skip the current node (0,0)
                if (x == 0 && y == 0)
                    continue;
                
                // Skip diagonal neighbors if diagonal movement is not enabled
                if (!includeDiagonalNeighbours && Mathf.Abs(x) + Mathf.Abs(y) == 2)
                    continue;
                
                int checkX = node.gridX + x;
                int checkY = node.gridY + y;
                
                // Check if the neighbor is within grid bounds
                if (checkX >= 0 && checkX < gridSizeX && checkY >= 0 && checkY < gridSizeY)
                {
                    // For diagonal movement, make sure we can actually reach this node
                    // by checking if the two adjacent orthogonal nodes are walkable
                    if (Mathf.Abs(x) + Mathf.Abs(y) == 2)
                    {
                        // Check if the orthogonal paths are blocked
                        if (!grid[node.gridX + x, node.gridY].walkable && !grid[node.gridX, node.gridY + y].walkable)
                        {
                            // Skip this diagonal neighbor if we can't get to it
                            continue;
                        }
                    }
                    
                    neighbours.Add(grid[checkX, checkY]);
                }
            }
        }

        return neighbours;
    }

    /// <summary>
    /// Converts a world position to the corresponding Node object in the grid.
    /// </summary>
    /// <param name="worldPosition">The position in world space.</param>
    /// <returns>The Node at the specified world position.</returns>
    public Node NodeFromWorldPoint(Vector2 worldPosition)
    {
        float percentX = (worldPosition.x + gridWorldSize.x / 2) / gridWorldSize.x;
        float percentY = (worldPosition.y + gridWorldSize.y / 2) / gridWorldSize.y;
        percentX = Mathf.Clamp01(percentX);
        percentY = Mathf.Clamp01(percentY);

        int x = Mathf.RoundToInt((gridSizeX - 1) * percentX);
        int y = Mathf.RoundToInt((gridSizeY - 1) * percentY);
        return grid[x, y];
    }

    // Public method to get terrain type at a world position
    public TerrainType GetTerrainTypeAt(Vector2 worldPosition)
    {
        Node node = NodeFromWorldPoint(worldPosition);
        return node.terrainType;
    }

    /// <summary>
    /// Finds the closest walkable node to a given node by searching outwards in increasing radii.
    /// </summary>
    /// <param name="node">The starting node (potentially unwalkable).</param>
    /// <returns>The closest walkable node, or null if none is found within the search radius.</returns>
    public Node ClosestWalkableNode(Node node)
    {
        int maxRadius = Mathf.Max(gridSizeX, gridSizeY) / 2;
        for (int i = 1; i < maxRadius; i++)
        {
            Node n = FindWalkableInRadius(node.gridX, node.gridY, i);
            if (n != null)
            {
                return n;
            }
        }
        return null;
    }

    /// <summary>
    /// Helper method to search for a walkable node within a specific radius around a center point.
    /// </summary>
    /// <param name="centreX">The grid X coordinate of the search center.</param>
    /// <param name="centreY">The grid Y coordinate of the search center.</param>
    /// <param name="radius">The search radius (distance from the center).</param>
    /// <returns>A walkable node if found within the radius boundary, otherwise null.</returns>
    Node FindWalkableInRadius(int centreX, int centreY, int radius)
    {
        for (int i = -radius; i <= radius; i++)
        {
            int verticalSearchX = i + centreX;
            int horizontalSearchY = i + centreY;

            // Top
            if (InBounds(verticalSearchX, centreY + radius))
            {
                if (grid[verticalSearchX, centreY + radius].walkable)
                {
                    return grid[verticalSearchX, centreY + radius];
                }
            }

            // Bottom
            if (InBounds(verticalSearchX, centreY - radius))
            {
                if (grid[verticalSearchX, centreY - radius].walkable)
                {
                    return grid[verticalSearchX, centreY - radius];
                }
            }

            // Right
            if (InBounds(centreX + radius, horizontalSearchY))
            {
                if (grid[centreX + radius, horizontalSearchY].walkable)
                {
                    return grid[centreX + radius, horizontalSearchY];
                }
            }

            // Left
            if (InBounds(centreX - radius, horizontalSearchY))
            {
                if (grid[centreX - radius, horizontalSearchY].walkable)
                {
                    return grid[centreX - radius, horizontalSearchY];
                }
            }

        }

        return null;
    }

    /// <summary>
    /// Checks if given grid coordinates are within the bounds of the grid.
    /// </summary>
    /// <param name="x">The grid X coordinate.</param>
    /// <param name="y">The grid Y coordinate.</param>
    /// <returns>True if the coordinates are within the grid, false otherwise.</returns>
    bool InBounds(int x, int y)
    {
        return x >= 0 && x < gridSizeX && y >= 0 && y < gridSizeY;
    }

    /// <summary>
    /// Detects nodes that are likely corners based on the number of adjacent unwalkable neighbors. Applies a movement penalty to these nodes.
    /// </summary>
    // Detect corners in the grid and mark them with higher movement cost
    private void DetectCorners()
    {
        detectedCorners.Clear();
        
        // Skip edge nodes to avoid array bounds issues
        for (int x = 1; x < gridSizeX - 1; x++)
        {
            for (int y = 1; y < gridSizeY - 1; y++)
            {
                if (!grid[x, y].walkable) continue; // Skip unwalkable nodes
                
                // Check surrounding nodes for pattern that indicates a corner
                int unwalkableCount = 0;
                int adjacentWalls = 0;
                
                // Check the 8 neighboring cells
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue; // Skip self
                        
                        Node neighbor = grid[x + dx, y + dy];
                        if (!neighbor.walkable)
                        {
                            unwalkableCount++;
                            
                            // Check if adjacent (not diagonal)
                            if (dx == 0 || dy == 0)
                            {
                                adjacentWalls++;
                            }
                        }
                    }
                }
                
                // If node has 5+ unwalkable neighbors and at least 2 adjacent walls, it's likely in a corner
                if (unwalkableCount >= 5 && adjacentWalls >= 2)
                {
                    // Mark as a corner
                    detectedCorners.Add(new Vector2Int(x, y));
                    
                    // Apply corner penalty to the node
                    grid[x, y].movementPenalty = cornerNodePenalty;
                    
                    // Debug visualization
                    Debug.DrawRay(grid[x, y].worldPosition, Vector3.up * 0.5f, Color.magenta, 5f);
                }
            }
        }
        
        // Debug output
        Debug.Log($"Detected {detectedCorners.Count} corners in the A* grid");
    }

    // Public method to check if a position is in a corner
    public bool IsInCorner(Vector2 worldPosition)
    {
        Vector2Int gridPos = WorldToGridPosition(worldPosition);
        return detectedCorners.Contains(gridPos);
    }

    /// <summary>
    /// Draws visualization gizmos in the Unity editor (if enabled). Shows grid lines, node walkability, terrain types, and corners.
    /// </summary>
    void OnDrawGizmos()
    {
        Gizmos.DrawWireCube(transform.position, new Vector2(gridWorldSize.x, gridWorldSize.y));

        if (grid != null && displayGridGizmos)
        {
            foreach (Node n in grid)
            {
                // Check if node is a corner
                Vector2Int gridPos = new Vector2Int(n.gridX, n.gridY);
                bool isCorner = detectedCorners != null && detectedCorners.Contains(gridPos);
                
                // Color based on terrain type
                if (!n.walkable)
                {
                    Gizmos.color = Color.red;
                }
                else if (isCorner)
                {
                    // Corner nodes are drawn in purple
                    Gizmos.color = Color.magenta;
                }
                else
                {
                    switch (n.terrainType)
                    {
                        case TerrainType.Normal:
                            Gizmos.color = Color.grey;
                            break;
                        case TerrainType.Water:
                            Gizmos.color = Color.blue;
                            break;
                        case TerrainType.Sand:
                            Gizmos.color = Color.yellow;
                            break;
                        case TerrainType.Mud:
                            Gizmos.color = new Color(0.4f, 0.2f, 0.0f); // Brown
                            break;
                        default:
                            Gizmos.color = Color.grey;
                            break;
                    }
                }

                Gizmos.DrawCube(n.worldPosition, Vector3.one * (nodeDiameter - 0.1f));
            }
        }
    }

    /// <summary>
    /// Enables or disables diagonal movement for pathfinding.
    /// </summary>
    /// <param name="enable">True to enable, false to disable.</param>
    // Toggle diagonal movement
    public void ToggleDiagonalMovement(bool enable)
    {
        includeDiagonalNeighbours = enable;
        Debug.Log("Diagonal movement " + (enable ? "enabled" : "disabled"));
    }
    
    /// <summary>
    /// Enables or disables the handling of dynamic obstacles.
    /// </summary>
    /// <param name="enable">True to enable, false to disable.</param>
    // Toggle dynamic obstacles
    public void ToggleDynamicObstacles(bool enable)
    {
        enableDynamicObstacles = enable;
        Debug.Log("Dynamic obstacles " + (enable ? "enabled" : "disabled"));
    }
    
    /// <summary>
    /// Enables or disables the use of weighted nodes (terrain costs).
    /// </summary>
    /// <param name="enable">True to enable, false to disable.</param>
    // Toggle weighted nodes
    public void ToggleWeightedNodes(bool enable)
    {
        enableWeightedNodes = enable;
        Debug.Log("Weighted nodes " + (enable ? "enabled" : "disabled"));
    }
    
    /// <summary>
    /// Enables or disables periodic updates of the entire grid.
    /// </summary>
    /// <param name="enable">True to enable, false to disable.</param>
    // Toggle periodic grid updates
    public void TogglePeriodicGridUpdate(bool enable)
    {
        enablePeriodicGridUpdate = enable;
        Debug.Log("Periodic grid updates " + (enable ? "enabled" : "disabled"));
        
        // Reset timer when enabling
        if (enable)
        {
            nextGridUpdateTime = Time.time + gridUpdateInterval;
        }
    }
}

