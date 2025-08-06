// Adapted from: https://github.com/SebLague/Pathfinding-2D

using UnityEngine;

// Enum to define different terrain types
public enum TerrainType
{
    Normal,     // Normal grass terrain (default)
    Water,      // Water terrain (slower)
    Sand,       // Sand terrain (slightly slower)
    Mud         // Mud terrain (much slower)
}

// This is a class to track a node in the A* Grid
// It tracks a number of properties that we need to compute A*
public class Node : IHeapItem<Node>
{
    // Can the node be reached (ie, no obstacle)
    public bool walkable;

    // The actual world position of the node
    public Vector2 worldPosition;

    // The grid cell of the node
    public int gridX;
    public int gridY;

    // G Cost to get to this node
    public float gCost;

    // H Cost for this node
    public float hCost;

    // Parent for this node (for reconstructing the path)
    public Node parent;

    // For heap management
    int heapIndex;

    // Weight of the node (affects movement cost)
    public float weight;

    // Terrain type of the node
    public TerrainType terrainType;

    // Movement penalty for pathfinding (for corners/special areas)
    public float movementPenalty = 1.0f;

    public Node(bool _walkable, Vector2 _worldPos, int _gridX, int _gridY, TerrainType _terrainType = TerrainType.Normal, float _weight = 1.0f)
    {
        walkable = _walkable;
        worldPosition = _worldPos;
        gridX = _gridX;
        gridY = _gridY;
        terrainType = _terrainType;
        weight = _weight;
        movementPenalty = 1.0f;
    }

    public Node Clone()
    {
        return new Node(walkable, worldPosition, gridX, gridY, terrainType, weight);
    }

    public float fCost
    {
        get
        {
            return gCost + hCost;
        }
    }

    public int HeapIndex
    {
        get
        {
            return heapIndex;
        }
        set
        {
            heapIndex = value;
        }
    }

    public int CompareTo(Node nodeToCompare)
    {
        int compare = fCost.CompareTo(nodeToCompare.fCost);
        if (compare == 0)
        {
            compare = hCost.CompareTo(nodeToCompare.hCost);
        }
        return -compare;
    }
}
