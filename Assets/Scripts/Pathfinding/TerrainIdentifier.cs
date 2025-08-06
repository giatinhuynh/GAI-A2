using UnityEngine;

// This component identifies the type of terrain for a GameObject.
// It's used by the pathfinding system to determine movement costs.
public class TerrainIdentifier : MonoBehaviour
{
    // The type of terrain this object represents (e.g., Normal, Water, Sand).
    public TerrainType terrainType = TerrainType.Normal;

    // Multiplier affecting the cost to move through this terrain type.
    // Higher values mean it's more costly/slower to traverse.
    public float movementCostMultiplier = 1.0f;

    // This function is called in the Unity editor whenever the script is loaded or a value is changed in the Inspector.
    // It ensures the movement cost multiplier is automatically updated when the terrain type is changed via the editor.
    void OnValidate()
    {
        // Automatically set the movement cost multiplier based on the selected terrain type.
        // This provides default costs, which can still be manually overridden in the Inspector if needed.
        switch (terrainType)
        {
            // Standard terrain, no movement penalty.
            case TerrainType.Normal:
                movementCostMultiplier = 1.0f;
                break;
            // Water terrain, costs twice as much to move through.
            case TerrainType.Water:
                movementCostMultiplier = 2.0f;
                break;
            // Sand terrain, costs 1.5 times as much to move through.
            case TerrainType.Sand:
                movementCostMultiplier = 1.5f;
                break;
            // Mud terrain, costs three times as much to move through.
            case TerrainType.Mud:
                movementCostMultiplier = 3.0f;
                break;
        }
    }
} 