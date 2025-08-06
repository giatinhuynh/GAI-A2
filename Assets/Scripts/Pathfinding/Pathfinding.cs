// Adapted from: https://github.com/SebLague/Pathfinding-2D

using UnityEngine;
using System.Collections.Generic;
using System;
using System.Linq;

// Enum to define different heuristic types
public enum HeuristicType
{
    Manhattan,      // Sum of absolute differences in x and y
    Euclidean,      // Straight-line distance
    Chebyshev,      // Maximum of absolute differences
    Octile,         // Combination of diagonal and orthogonal movement
    Custom          // Custom heuristic that considers terrain
}

public class Pathfinding : MonoBehaviour
{
    // Singleton instance for easy access
    public static AStarGrid grid;
    static Pathfinding instance;

    // The type of heuristic to use for pathfinding
    public HeuristicType heuristicType = HeuristicType.Manhattan;
    
    // Movement costs for different directions
    public float orthogonalMovementCost = 1.0f; // Cost for moving horizontally or vertically
    public float diagonalMovementCost = 1.414f; // Cost for moving diagonally (approx. sqrt(2))

    // Path optimization settings
    public bool enablePathSmoothing = true; // Smooths the path using Bezier curves for more natural movement
    public bool enablePathOptimization = true; // Removes redundant waypoints from the path
    public float pathOptimizationThreshold = 0.1f; // Angle threshold (in degrees) for removing waypoints during optimization
    
    // Path caching settings
    private Dictionary<string, Node[]> pathCache = new Dictionary<string, Node[]>();
    public float cacheLifetime = 5f; // Cache paths for 5 seconds
    private Dictionary<string, float> cacheTimestamps = new Dictionary<string, float>();

    // Movement costs for different terrain types
    public float waterTerrainCost = 2.0f; // Cost multiplier for water terrain
    public float sandTerrainCost = 1.5f;  // Cost multiplier for sand terrain
    public float mudTerrainCost = 3.0f;   // Cost multiplier for mud terrain

    // Dynamic path recalculation settings
    public bool enableDynamicPathRecalculation = true;
    public float pathRecalculationInterval = 0.5f;
    private float nextPathRecalculationTime = 0f;
    private Dictionary<string, Node[]> activePaths = new Dictionary<string, Node[]>();

    // Additional path planning settings
    public bool enableCornerAvoidance = true; // Penalizes paths that cut corners too closely
    public float cornerAvoidanceCost = 4.0f; // Multiplier applied to the cost of corner nodes
    public float obstacleProximityPenalty = 1.5f; // Multiplier applied to nodes near obstacles
    public float obstacleProximityCheckRadius = 1.0f; // Radius to check for nearby obstacles

    // This is used instead of Start() so that the
    // A* grid is only greated once when the game is launched
    void Awake() // Called when the script instance is being loaded
    {
        grid = GetComponent<AStarGrid>();
        instance = this;
    }

    void Update()
    {
        // Clean up expired cache entries
        CleanupCache();

        // Update active paths if dynamic recalculation is enabled
        if (enableDynamicPathRecalculation && Time.time >= nextPathRecalculationTime)
        {
            UpdateActivePaths();
            nextPathRecalculationTime = Time.time + pathRecalculationInterval;
        }
    }

    private void UpdateActivePaths()
    // Periodically checks and recalculates paths stored in activePaths if they become invalid
    {
        var pathsToUpdate = new List<string>(activePaths.Keys);
        foreach (var pathKey in pathsToUpdate)
        {
            // Extract start and end positions from the path key
            string[] positions = pathKey.Split('_');
            Vector2 startPos = ParseVector2(positions[0]);
            Vector2 endPos = ParseVector2(positions[1]);

            // Check if the current path is still valid
            if (!IsPathValid(activePaths[pathKey]))
            {
                // Recalculate the path
                Node[] newPath = FindPath(startPos, endPos);
                if (newPath != null && newPath.Length > 0)
                {
                    activePaths[pathKey] = newPath;
                }
                else
                {
                    // If no valid path is found, remove it from active paths
                    activePaths.Remove(pathKey);
                }
            }
        }
    }

    private bool IsPathValid(Node[] path)
    // Checks if a given path is still valid by verifying walkability and proximity to dynamic obstacles
    {
        if (path == null || path.Length == 0) return false;

        // Check each node in the path
        foreach (Node node in path)
        {
            // If any node is no longer walkable, the path is invalid
            if (!node.walkable)
            {
                return false;
            }

            // Check for dynamic obstacles in the vicinity
            Vector2Int gridPos = grid.WorldToGridPosition(node.worldPosition);
            if (grid.IsInDynamicObstacleArea(gridPos))
            {
                return false;
            }
        }

        return true;
    }

    private Vector2 ParseVector2(string vectorString)
    // Helper function to parse a Vector2 from a string format "x,y"
    {
        string[] components = vectorString.Split(',');
        return new Vector2(float.Parse(components[0]), float.Parse(components[1]));
    }

    // Public callable method
    public static Node[] RequestPath(Vector2 from, Vector2 to)
    // Public static method to request a path from one point to another. Handles caching and active path management.
    {
        Node[] path = instance.FindPath(from, to);
        if (path != null && path.Length > 0)
        {
            string pathKey = $"{from.x},{from.y}_{to.x},{to.y}";
            instance.activePaths[pathKey] = path;
        }
        return path;
    }

    // Internal private implementation
    Node[] FindPath(Vector2 from, Vector2 to)
    // Core A* pathfinding algorithm implementation
    {
        // Check cache first
        string cacheKey = $"{from.x},{from.y}_{to.x},{to.y}";
        if (IsPathCached(cacheKey))
        {
            return GetCachedPath(cacheKey);
        }

        // A* Waypoints to return
        Node[] waypoints = new Node[0]; // Initialize empty path

        // Set to true if a path is found
        bool pathSuccess = false;

        // Starting node point - selected from the A* Grid
        Node startNode = grid.NodeFromWorldPoint(from);

        // Goal node point - selected from the A* Grid
        Node targetNode = grid.NodeFromWorldPoint(to);

        // Ensure the starting node's parent is not null
        // This also helps detect the start node during path retracing
        startNode.parent = startNode;

        // Niceity check to ensure the start and target nodes are walkable
        // (e.g., if the user clicks on an unwalkable object)
        // If not, we find the closest walkable point in the grid
        if (!startNode.walkable)
        {
            startNode = grid.ClosestWalkableNode(startNode);
        }
        if (!targetNode.walkable)
        {
            targetNode = grid.ClosestWalkableNode(targetNode);
        }

        if (startNode.walkable && targetNode.walkable)
        { // Only proceed if both start and target nodes are valid and walkable
            // Track the open set of nodes to explore, as a heap sorted by the A* Cost
            Heap<Node> openSet = new Heap<Node>(grid.MaxSize);

            // Track closed set of all visited nodes
            HashSet<Node> closedSet = new HashSet<Node>();

            // Commence A* by adding the start node to the open set
            startNode.gCost = 0;
            startNode.hCost = Heuristic(startNode, targetNode);
            openSet.Add(startNode);

            // Stop if we have a path or run out of nodes to explore (means no path can be found!)
            while (!pathSuccess && openSet.Count > 0)
            {
                // Get the node with the lowest F cost from the open set
                // and add it to the closed set
                Node currentNode = openSet.RemoveFirst();
                closedSet.Add(currentNode);

                // If we have reached the target node, we have found a path!
                if (currentNode == targetNode)
                {
                    pathSuccess = true;
                }
                else
                {
                    // Otherwise, explore the neighbours of the current node
                    List<Node> neighbours = grid.GetNeighbours(currentNode);
                    foreach (Node neighbour in neighbours)
                    {
                        // If we can reach the neighbour and it is not in the closed set
                        if (neighbour.walkable && !closedSet.Contains(neighbour))
                        {
                            // Calculate the G Cost of the neighbour node
                            float newGCost = currentNode.gCost + GCost(currentNode, neighbour);
                            
                            // If enabled, apply corner avoidance
                            if (enableCornerAvoidance)
                            {
                                // Check if the node is in a corner
                                Vector2Int gridPos = new Vector2Int(neighbour.gridX, neighbour.gridY);
                                if (grid.IsInCorner(neighbour.worldPosition))
                                {
                                    newGCost *= cornerAvoidanceCost;
                                }
                                
                                // Also check for proximity to obstacles
                                if (IsNearObstacle(neighbour))
                                {
                                    newGCost *= obstacleProximityPenalty;
                                }
                            }

                            // If the neighbour is not in the open set OR
                            // the neighour was previously checked and the new G Cost is less than the previous G Cost
                            if (!openSet.Contains(neighbour) || newGCost < neighbour.gCost)
                            {
                                // Set neightbour G Cost
                                neighbour.gCost = newGCost;

                                // Compute and set the H Cost for the neighbour
                                neighbour.hCost = Heuristic(neighbour, targetNode);

                                // Set the parent of the neighbour to the current node
                                neighbour.parent = currentNode;

                                // Add neighbour to the open set or update it if already present
                                if (!openSet.Contains(neighbour))
                                {
                                    openSet.Add(neighbour);
                                }
                                else
                                {
                                    openSet.UpdateItem(neighbour);
                                }
                            }
                        }
                    }
                }
            }
        }

        // If we have a path, then actually get the path from the start to goal
        if (pathSuccess)
        {
            waypoints = RetracePath(startNode, targetNode);
            
            // Optimize the path if enabled
            if (enablePathOptimization)
            {
                waypoints = OptimizePath(waypoints);
            }
            
            // Smooth the path if enabled
            if (enablePathSmoothing)
            {
                waypoints = SmoothPath(waypoints);
            }
            
            // Cache the path
            CachePath(cacheKey, waypoints);
        }

        return waypoints;
    }

    // Creates the actual A* Path from the start to the goal
    Node[] RetracePath(Node startNode, Node endNode)
    // Reconstructs the path by backtracking from the end node to the start node using parent references
    {
        // Store the computed path
        List<Node> path = new List<Node>();

        // Commence retracing the path from the end node
        Node currentNode = endNode;

        // Loop while the current node isn't the start node
        while (currentNode != startNode)
        {
            // Add the current node to the path
            path.Add(currentNode);

            // Set the current node to the parent of the current node
            currentNode = currentNode.parent;
        }

        // Convert this list to an array and reverse it
        Node[] waypoints = path.ToArray();
        Array.Reverse(waypoints);
        return waypoints;
    }

    // Optimize the path by removing unnecessary waypoints
    Node[] OptimizePath(Node[] path)
    // Simplifies the path by removing waypoints that don't significantly change direction or are not corners
    {
        if (path.Length <= 2) return path;

        List<Node> optimizedPath = new List<Node>();
        optimizedPath.Add(path[0]);

        for (int i = 1; i < path.Length - 1; i++)
        {
            // Calculate the direction vectors
            Vector2 prevToCurrent = (path[i].worldPosition - path[i - 1].worldPosition).normalized;
            Vector2 currentToNext = (path[i + 1].worldPosition - path[i].worldPosition).normalized;
            
            // Calculate the angle between the directions
            float angle = Vector2.Angle(prevToCurrent, currentToNext);
            
            // Always keep corner points or points with significant direction change
            bool isCorner = grid.IsInCorner(path[i].worldPosition);
            float distanceToPrev = Vector2.Distance(path[i].worldPosition, path[i - 1].worldPosition);
            float distanceToNext = Vector2.Distance(path[i].worldPosition, path[i + 1].worldPosition);
            
            if (angle > 15f || distanceToPrev > grid.gridSize * 1.5f || 
                distanceToNext > grid.gridSize * 1.5f || isCorner)
            {
                optimizedPath.Add(path[i]);
            }
        }

        optimizedPath.Add(path[path.Length - 1]);
        return optimizedPath.ToArray();
    }

    // Smooth the path using Bezier curves for more natural movement
    Node[] SmoothPath(Node[] path)
    // Smooths the path using Bezier curves to create more natural-looking movement, avoiding obstacles and corners
    {
        if (path.Length <= 2) return path;

        List<Node> smoothedPath = new List<Node>();
        smoothedPath.Add(path[0]);

        for (int i = 1; i < path.Length - 1; i++)
        {
            Vector2 p0 = path[i - 1].worldPosition;
            Vector2 p1 = path[i].worldPosition;
            Vector2 p2 = path[i + 1].worldPosition;

            // Don't smooth corners - keep the original waypoint
            if (grid.IsInCorner(path[i].worldPosition))
            {
                smoothedPath.Add(path[i]);
                continue;
            }

            // Calculate control points for quadratic Bezier curve
            Vector2 controlPoint1 = p0 + (p1 - p0) * 0.5f;
            Vector2 controlPoint2 = p1 + (p2 - p1) * 0.5f;

            // Check if the direct path between points is valid (no obstacles and reasonable terrain)
            if (IsDirectPathValid(path[i - 1], path[i + 1]))
            {
                // Add intermediate points along the curve
                int segments = Mathf.CeilToInt(Vector2.Distance(p0, p2) / (grid.gridSize * 0.5f));
                for (int j = 1; j <= segments; j++)
                {
                    float t = j / (float)segments;
                    Vector2 point = CalculateBezierPoint(t, p0, controlPoint1, controlPoint2, p2);
                    
                    // Create a new node for the smoothed point
                    Node smoothedNode = new Node(true, point, -1, -1, GetAverageTerrainType(path[i - 1], path[i + 1]));
                    smoothedPath.Add(smoothedNode);
                }
            }
            else
            {
                // If direct path is not valid, keep the original waypoint
                smoothedPath.Add(path[i]);
            }
        }

        smoothedPath.Add(path[path.Length - 1]);
        return smoothedPath.ToArray();
    }

    private bool IsDirectPathValid(Node start, Node end)
    // Checks if a straight line path between two nodes is valid (no obstacles, difficult terrain, or corners)
    {
        // Check if there are any obstacles or difficult terrain between the points
        Vector2 direction = (end.worldPosition - start.worldPosition).normalized;
        float distance = Vector2.Distance(start.worldPosition, end.worldPosition);
        int steps = Mathf.CeilToInt(distance / grid.gridSize);
        
        // Increase steps for more precise checking
        steps = Mathf.Max(steps, 8);
        
        for (int i = 1; i < steps; i++)
        {
            Vector2 checkPoint = start.worldPosition + direction * (distance * i / steps);
            Node checkNode = grid.NodeFromWorldPoint(checkPoint);
            
            // Always check if the node is walkable
            if (!checkNode.walkable)
                return false;
                
            // If we hit difficult terrain, consider the path less desirable
            if (checkNode.terrainType == TerrainType.Mud || 
                checkNode.terrainType == TerrainType.Water ||
                checkNode.terrainType == TerrainType.Sand)
                return false;
                
            // Check for corners - don't smooth through corners
            if (grid.IsInCorner(checkPoint))
                return false;
            
            // Check for nearby obstacles with a smaller radius
            Collider2D[] obstacles = Physics2D.OverlapCircleAll(
                checkPoint, 
                obstacleProximityCheckRadius * 0.75f,
                grid.unwalkableMask
            );
            
            if (obstacles.Length > 0)
                return false;
        }
        
        return true;
    }

    private TerrainType GetAverageTerrainType(Node nodeA, Node nodeB)
    // Determines the representative terrain type between two nodes, prioritizing more difficult terrain
    {
        // Return the more difficult terrain type between the two nodes
        if (nodeA.terrainType == TerrainType.Mud || nodeB.terrainType == TerrainType.Mud)
            return TerrainType.Mud;
        if (nodeA.terrainType == TerrainType.Water || nodeB.terrainType == TerrainType.Water)
            return TerrainType.Water;
        if (nodeA.terrainType == TerrainType.Sand || nodeB.terrainType == TerrainType.Sand)
            return TerrainType.Sand;
        return TerrainType.Normal;
    }

    // Calculate a point on a cubic Bezier curve
    private Vector2 CalculateBezierPoint(float t, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
    // Calculates a point on a cubic Bezier curve given a time parameter 't' and four control points
    {
        float u = 1 - t;
        float tt = t * t;
        float uu = u * u;
        float uuu = uu * u;
        float ttt = tt * t;

        Vector2 p = uuu * p0;
        p += 3 * uu * t * p1;
        p += 3 * u * tt * p2;
        p += ttt * p3;

        return p;
    }

    private float GCost(Node nodeA, Node nodeB)
    // Calculates the movement cost (G cost) from node A to node B, considering terrain and movement penalties
    {
        // Base movement cost based on diagonal or orthogonal movement
        float baseCost = (nodeA.gridX != nodeB.gridX && nodeA.gridY != nodeB.gridY) ? diagonalMovementCost : orthogonalMovementCost;
        
        // Get terrain costs for both nodes
        float terrainCostA = GetTerrainCost(nodeA.terrainType);
        float terrainCostB = GetTerrainCost(nodeB.terrainType);
        
        // Average the terrain costs of both nodes
        float averageTerrainCost = (terrainCostA + terrainCostB) * 0.5f;
        
        // Apply the terrain cost multiplier to the base movement cost
        // Also consider movement penalties for special nodes like corners
        return baseCost * averageTerrainCost * nodeB.movementPenalty;
    }

    private float GetTerrainCost(TerrainType terrainType)
    {
        switch (terrainType)
        {
            case TerrainType.Water:
                return waterTerrainCost;
            case TerrainType.Sand:
                return sandTerrainCost;
            case TerrainType.Mud:
                return mudTerrainCost;
            default:
                return 1.0f; // Normal terrain has no cost multiplier
        }
    }

    private float Heuristic(Node nodeA, Node nodeB)
    // Calculates the estimated cost (H cost) from node A to node B using the selected heuristic method
    {
        switch (heuristicType)
        {
            case HeuristicType.Manhattan:
                return ManhattanDistance(nodeA, nodeB);
            case HeuristicType.Euclidean:
                return EuclideanDistance(nodeA, nodeB);
            case HeuristicType.Chebyshev:
                return ChebyshevDistance(nodeA, nodeB);
            case HeuristicType.Octile:
                return OctileDistance(nodeA, nodeB);
            case HeuristicType.Custom:
                return CustomTerrainHeuristic(nodeA, nodeB);
            default:
                return ManhattanDistance(nodeA, nodeB);
        }
    }

    // Manhattan distance heuristic - sum of absolute differences in x and y coordinates
    private float ManhattanDistance(Node nodeA, Node nodeB)
    // Calculates Manhattan distance (sum of absolute differences in coordinates)
    {
        return Mathf.Abs(nodeA.gridX - nodeB.gridX) + Mathf.Abs(nodeA.gridY - nodeB.gridY);
    }

    // Euclidean distance heuristic - straight-line distance between two points
    private float EuclideanDistance(Node nodeA, Node nodeB)
    // Calculates Euclidean distance (straight-line distance)
    {
        float dx = Mathf.Abs(nodeA.gridX - nodeB.gridX);
        float dy = Mathf.Abs(nodeA.gridY - nodeB.gridY);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    private float ChebyshevDistance(Node nodeA, Node nodeB)
    // Calculates Chebyshev distance (maximum of absolute differences in coordinates)
    {
        float dx = Mathf.Abs(nodeA.gridX - nodeB.gridX);
        float dy = Mathf.Abs(nodeA.gridY - nodeB.gridY);
        return Mathf.Max(dx, dy);
    }

    private float OctileDistance(Node nodeA, Node nodeB)
    // Calculates Octile distance (considers diagonal and orthogonal moves)
    {
        float dx = Mathf.Abs(nodeA.gridX - nodeB.gridX);
        float dy = Mathf.Abs(nodeA.gridY - nodeB.gridY);
        return (dx + dy) + (Mathf.Sqrt(2) - 2) * Mathf.Min(dx, dy);
    }

    private float CustomTerrainHeuristic(Node nodeA, Node nodeB)
    // Custom heuristic that factors in terrain difficulty along with distance
    {
        // Base distance calculation
        float dx = Mathf.Abs(nodeA.gridX - nodeB.gridX);
        float dy = Mathf.Abs(nodeA.gridY - nodeB.gridY);
        float baseDistance = Mathf.Sqrt(dx * dx + dy * dy);
        
        // Consider terrain types in the heuristic
        float terrainFactor = 1.0f;
        if (nodeA.terrainType == TerrainType.Water || nodeB.terrainType == TerrainType.Water)
            terrainFactor *= 1.5f;
        if (nodeA.terrainType == TerrainType.Mud || nodeB.terrainType == TerrainType.Mud)
            terrainFactor *= 2.0f;
        if (nodeA.terrainType == TerrainType.Sand || nodeB.terrainType == TerrainType.Sand)
            terrainFactor *= 1.2f;
            
        return baseDistance * terrainFactor;
    }

    // Method to switch between different heuristics (can be called from UI or other scripts)
    public void SetHeuristicType(HeuristicType type)
    {
        heuristicType = type;
        Debug.Log("Heuristic changed to: " + type.ToString());
    }

    // Path caching methods
    private bool IsPathCached(string key)
    // Checks if a path with the given key exists in the cache and hasn't expired
    {
        return pathCache.ContainsKey(key) && Time.time - cacheTimestamps[key] < cacheLifetime;
    }

    private Node[] GetCachedPath(string key)
    // Retrieves a cached path using its key
    {
        return pathCache[key];
    }

    private void CachePath(string key, Node[] path)
    // Stores a calculated path in the cache with a timestamp
    {
        pathCache[key] = path;
        cacheTimestamps[key] = Time.time;
    }

    private void CleanupCache()
    // Removes expired entries from the path cache
    {
        var expiredKeys = cacheTimestamps.Where(kvp => Time.time - kvp.Value > cacheLifetime)
                                       .Select(kvp => kvp.Key)
                                       .ToList();
        
        foreach (var key in expiredKeys)
        {
            pathCache.Remove(key);
            cacheTimestamps.Remove(key);
        }
    }

    // Check if a node is close to obstacles
    private bool IsNearObstacle(Node node)
    // Checks if a node is within a specified radius of any unwalkable obstacles
    {
        // Cast a circle around the node position to check for obstacles
        Collider2D[] colliders = Physics2D.OverlapCircleAll(
            node.worldPosition, 
            obstacleProximityCheckRadius,
            grid.unwalkableMask
        );
        
        return colliders.Length > 0;
    }

    // Method to toggle dynamic path recalculation
    public void ToggleDynamicPathRecalculation(bool enable)
    {
        enableDynamicPathRecalculation = enable;
        Debug.Log("Dynamic path recalculation " + (enable ? "enabled" : "disabled"));
    }
    
    // Method to toggle path smoothing
    public void TogglePathSmoothing(bool enable)
    {
        enablePathSmoothing = enable;
        Debug.Log("Path smoothing " + (enable ? "enabled" : "disabled"));
    }
    
    // Method to toggle path optimization
    public void TogglePathOptimization(bool enable)
    {
        enablePathOptimization = enable;
        Debug.Log("Path optimization " + (enable ? "enabled" : "disabled"));
    }
    
    // Method to toggle corner avoidance
    public void ToggleCornerAvoidance(bool enable)
    {
        enableCornerAvoidance = enable;
        Debug.Log("Corner avoidance " + (enable ? "enabled" : "disabled"));
    }
}
