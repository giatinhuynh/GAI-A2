using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SteeringCalcs; // Namespace containing steering behavior calculations.
using Globals; // Namespace for global constants or utility functions.
using System;
using UnityEngine.SceneManagement; // Required for scene management functionalities (e.g., restarting level).
using UnityEngine.UI; // Required for interacting with UI elements (e.g., health bar).
using UnityEngine.EventSystems; // Required for handling input events on UI elements.

public class Frog : MonoBehaviour
{
    // Enum defining the available steering behaviors for the frog's movement.
    public enum FrogSteeringType : int
    {
        Seek = 0,   // Move directly towards the target at maximum speed.
        Arrive = 1  // Move towards the target, slowing down upon approach within a radius.
    }

    // Enum defining the movement method used (direct steering or pathfinding).
    public enum MovementType : int
    {
        Direct = 0, // Move in a straight line using steering behaviors (Seek/Arrive).
        AStar = 1   // Use the A* algorithm to navigate around obstacles.
    }

    // Enum defining who or what controls the frog.
    public enum ControlMode : int
    {
        Human = 0,        // Player controls the frog via input (e.g., mouse clicks).
        AI = 1,           // Frog is controlled by basic AI logic (potentially deprecated or simple).
        BehaviorTree = 2  // Frog is controlled by a more complex Behavior Tree AI system.
    }

    // Determines the current control system for the frog (Player, AI, BT).
    public ControlMode controlMode = ControlMode.Human;

    // --- Frog Status Variables ---
    public int Health; // Current health points of the frog. Decreases upon taking damage.
    public int FliesCollected;  // Counter for the number of flies the frog has eaten.
    public const int MaxFlies = 10;  // The maximum number of flies required (e.g., to complete a level objective).

    // --- Steering & Movement Parameters ---
    public FrogSteeringType SteeringType; // Specifies which steering behavior (Seek or Arrive) to use when MovementType is Direct.
    public MovementType CurrentMovementType = MovementType.Direct; // Current movement logic (Direct steering or A* pathfinding). Defaults to Direct.
    public float MaxSpeed;      // The absolute maximum speed the frog can move at.
    public float MaxAccel;      // The maximum rate at which the frog can change its velocity.
    public float AccelTime;     // Time parameter potentially used by steering calculations (e.g., time to reach max acceleration).
    public AvoidanceParams AvoidParams;  // Configuration parameters for the obstacle avoidance behavior.

    // --- Arrival Behavior Configuration ---
    // The arrival radius dynamically changes based on the distance to the target click point.
    // See the Update() method for the calculation logic.
    public float ArrivePct;         // Percentage of the distance to target used for calculating the dynamic arrival radius.
    public float MinArriveRadius;   // The smallest possible radius for the Arrive behavior.
    public float MaxArriveRadius;   // The largest possible radius for the Arrive behavior.
    private float _arriveRadius;    // The dynamically calculated radius currently used by the Arrive steering behavior.

    // --- Visual Settings ---
    // Option to hide the target flag visual aid once the frog reaches it.
    // Useful for debugging Seek behavior (to see overshooting).
    public bool HideFlagOnceReached;

    // --- Bubble Shooting Parameters ---
    public GameObject BubblePrefab; // The prefab used to instantiate bubble projectiles.
    public float BubbleOffset = 0.5f; // Distance from the frog's center where bubbles are spawned.
    private float bubbleCooldown = 1.5f; // Minimum time interval (seconds) between consecutive bubble shots.
    private float lastBubbleTime = 0f; // Stores the Time.time when the last bubble was fired.
    public float bubbleRange = 7f; // The maximum distance at which the frog will automatically shoot bubbles at snakes.
    private bool autoShootEnabled = true; // Controls whether the frog automatically shoots at nearby snakes (currently always enabled).

    // --- AI Decision Parameters (Potentially for Decision/Behavior Trees - WEEK6) ---
    // These variables likely feed into the AI's decision-making process.
    public float scaredRange;       // Distance within which the frog might consider a snake a threat and flee.
    public float huntRange;         // Distance within which the frog might start actively hunting a fly.
    public Fly closestFly;          // Reference to the currently closest Fly object.
    public Snake closestSnake;      // Reference to the currently closest Snake object.
    public float distanceToClosestFly; // Cached distance to the closest fly.
    public float distanceToClosestSnake; // Cached distance to the closest snake.
    public List<Snake> nearbySnakes = new List<Snake>(); // List storing all snakes detected within a certain proximity.
    public float anchorWeight;      // A weighting factor likely used in a steering behavior to keep the frog near an anchor point.
    public Vector2 AnchorDims;      // Dimensions defining the area for the anchor behavior.

    // --- Scene Object References ---
    // Cached references to important GameObjects and Components in the scene.
    private Transform _flag;        // The Transform component of the visual flag marker (target indicator).
    private SpriteRenderer _flagSr; // The SpriteRenderer of the flag, used to toggle its visibility.
    private DrawGUI _drawGUIScript; // Reference to a script managing UI drawing or updates.
    private Animator _animator;     // The Animator component used to control frog animations (e.g., idle, moving).
    private Rigidbody2D _rb;        // The Rigidbody2D component handling physics simulation and movement.
    private CircleCollider2D _collider; // The CircleCollider2D component for detecting collisions with obstacles or triggers.

    // --- Movement State ---
    // Stores the world position of the last right-click input from the player. Used as the target in Human control mode. Null if no click has occurred.
    private Vector2? _lastClickPos;

    // --- Game State ---
    // Flag indicating if the game has entered a game over condition.
    private bool _isGameOver = false;

    // --- A* Pathfinding Variables ---
    private Pathfinding _pathfinding; // Reference to the main Pathfinding system component.
    private Node[] _currentPath;      // Array of nodes representing the calculated path from the A* algorithm.
    private int _currentPathIndex = 0; // Index pointing to the next waypoint node in the _currentPath array that the frog is moving towards.
    private float _waypointReachedDistance = 0.5f; // Threshold distance to consider a waypoint as reached, triggering movement towards the next waypoint.
    private float _pathUpdateInterval = 0.5f; // Frequency (in seconds) at which the A* path is recalculated if periodic updates are enabled.
    private float _pathUpdateTimer = 0f;    // Timer used to track elapsed time for periodic path updates.

    // --- A* Pathfinding Settings ---
    // Toggle to enable/disable recalculating the A* path periodically while the frog is moving. Useful for dynamic environments.
    public bool enablePeriodicPathUpdates = true;

    // Method to enable or disable periodic A* path updates via code.
    public void TogglePeriodicPathUpdates(bool enable)
    {
        enablePeriodicPathUpdates = enable;
        Debug.Log("Periodic path updates " + (enable ? "enabled" : "disabled"));

        // Reset the timer when toggling to prevent immediate recalculation.
        _pathUpdateTimer = 0f;
    }

    // --- Stuck Detection Variables ---
    // Logic to detect if the frog has become stuck (e.g., against an obstacle) and potentially trigger corrective actions.
    private float _stuckTimer = 0f; // Accumulates time the frog might be stuck.
    private float _stuckCheckInterval = 0.2f; // How often (in seconds) the stuck check is performed.
    private float _stuckThreshold = 1.5f; // Duration (in seconds) the frog needs to be potentially stuck before triggering stuck logic.
    private Vector2 _lastPosition;       // Stores the frog's position from the previous stuck check.
    private float _stuckDistanceThreshold = 0.1f; // Minimum distance the frog must move between checks to reset the stuck timer.
    private int _pathRecalculationAttempts = 0; // Counts how many times path recalculation was attempted due to being stuck.
    private int _maxPathRecalculationAttempts = 3; // Maximum number of times to try recalculating the path when stuck.

    // --- Stuck Detection Settings ---
    // Toggle to enable or disable the stuck detection mechanism.
    public bool enableStuckDetection = true;

    // Method to enable or disable stuck detection via code.
    public void ToggleStuckDetection(bool enable)
    {
        enableStuckDetection = enable;
        Debug.Log("Frog stuck detection " + (enable ? "enabled" : "disabled"));

        // Reset stuck state variables when toggling the feature.
        _stuckTimer = 0f;
        _pathRecalculationAttempts = 0;
    }

    // --- Heuristic Comparison Mode ---
    // Enables a debug mode to visualize and compare paths generated by different A* heuristics.
    public void SetHeuristicComparisonMode(bool enable)
    {
        _heuristicComparisonMode = enable;

        // Clear any stored comparison paths when disabling the mode.
        if (!enable)
        {
            _comparisonPaths.Clear();
        }

        Debug.Log("Heuristic comparison mode " + (enable ? "enabled" : "disabled"));
    }

    // --- Terrain Interaction ---
    // Defines movement speed multipliers based on the terrain type the frog is currently on.
    private readonly Dictionary<TerrainType, float> terrainSpeedModifiers = new Dictionary<TerrainType, float>()
    {
        { TerrainType.Normal, 1.0f }, // Full speed on normal ground.
        { TerrainType.Water, 0.5f },  // Half speed in water.
        { TerrainType.Sand, 0.7f },   // Slightly reduced speed on sand.
        { TerrainType.Mud, 0.3f }     // Significantly reduced speed in mud.
    };

    // Cached reference to the AStarGrid component for quick access to grid information (like terrain type).
    private AStarGrid _astarGrid;

    // --- Visual Feedback ---
    // Toggle to show/hide visual feedback related to terrain effects (e.g., text above the frog).
    public bool showTerrainEffects = true;
    private TextMesh _terrainEffectText; // Reference to the TextMesh component used for displaying terrain info.
    private Color _normalColor;         // Stores the frog's original sprite color for resetting after terrain effects.
    private SpriteRenderer _spriteRenderer; // Reference to the frog's main SpriteRenderer component.

    // --- Decision Tree Debugging ---
    // Toggle to enable/disable decision tree debug information. Note: Visual display might be handled by a separate GUI panel now.
    public bool showDecisionTreeDebug = false; // Defaulted to false, assuming overhead text is replaced by UI.
    private float _lastDecisionTime = 0f;   // Timestamp of the last AI decision calculation.
    private float _decisionInterval = 0.2f; // Minimum time between AI decision updates to improve performance.
    public string _lastDecision = "";       // Stores a string representation of the last AI decision made (for display).
    private TextMesh _decisionDebugText;    // Reference to the TextMesh for overhead debug info (may be unused).

    // --- Heuristic Comparison Visualization ---
    // Flag indicating if the A* heuristic comparison mode is currently active.
    private bool _heuristicComparisonMode = false;
    // Stores the paths calculated using different heuristics for comparison visualization. Maps HeuristicType to the path (Node array).
    private Dictionary<HeuristicType, Node[]> _comparisonPaths = new Dictionary<HeuristicType, Node[]>();
    // Defines the colors used to draw the paths for each heuristic type during comparison mode.
    private Dictionary<HeuristicType, Color> _heuristicColors = new Dictionary<HeuristicType, Color>();

    // --- Movement Smoothing ---
    // Variables used to smoothly transition the frog's target when the AI changes objectives.
    private Vector2 _previousTargetPosition; // The target position the frog was moving towards before the current one.
    private Vector2 _currentTargetPosition;  // The current target position determined by AI or input.
    private float _transitionStartTime = 0f; // Time.time when the current target transition began.
    private float _transitionDuration = 0.3f; // Duration (seconds) over which to interpolate between the previous and current target positions.
    public string _currentBehaviorState = "None"; // String describing the current high-level AI behavior (e.g., "Idle", "Hunting Fly", "Fleeing").

    // --- Behavior State Tracking ---
    // Specific flag indicating if the frog's current primary goal is to hunt a fly.
    public bool _isHuntingFly = false;

    // Flag to differentiate between targets set by player input versus targets set by AI logic.
    private bool _isUserTarget = false;

    // Called once when the script instance is first enabled.
    void Start()
    {
        // --- Initialization Block ---

        // Log initial health for debugging purposes.
        Debug.Log("Frog Start called - Initial health: " + Health);

        // Log the MaxFlies constant value for verification.
        Debug.Log("Frog initialized with MaxFlies = " + MaxFlies);

        // Ensure that the frog starts with auto-shooting enabled.
        autoShootEnabled = true;

        // Initialize heuristic colors dictionary for path visualization
        _heuristicColors[HeuristicType.Manhattan] = new Color(0.8f, 0.2f, 0.2f); // Reddish
        _heuristicColors[HeuristicType.Euclidean] = new Color(0.2f, 0.8f, 0.2f); // Greenish
        _heuristicColors[HeuristicType.Chebyshev] = new Color(0.2f, 0.2f, 0.8f); // Bluish
        _heuristicColors[HeuristicType.Octile] = new Color(0.8f, 0.8f, 0.2f); // Yellow
        _heuristicColors[HeuristicType.Custom] = new Color(0.8f, 0.2f, 0.8f); // Magenta

        // --- Cache Component References ---
        // Find the "Flag" GameObject used as a visual target marker.
        _flag = GameObject.Find("Flag").transform;
        // Get the SpriteRenderer component of the flag.
        _flagSr = _flag.GetComponent<SpriteRenderer>();
        // Initially hide the flag sprite.
        _flagSr.enabled = false;

        // Find the UIManager GameObject in the scene.
        GameObject uiManager = GameObject.Find("UIManager");
        // If found, get the DrawGUI script component attached to it.
        if (uiManager != null)
        {
            _drawGUIScript = uiManager.GetComponent<DrawGUI>();
        }
        // else: Consider adding a Debug.LogWarning if DrawGUI is essential.

        // Get the Animator component attached to this GameObject.
        _animator = GetComponent<Animator>();

        // Get the Rigidbody2D component attached to this GameObject.
        _rb = GetComponent<Rigidbody2D>();

        // Get the CircleCollider2D component.
        _collider = GetComponent<CircleCollider2D>();
        if (_collider != null)
        {
            // Ensure the collider is configured for physics collisions (not as a trigger).
            _collider.isTrigger = false;
        }
        else
        {
            // Add a collider if doesn't exist
            _collider = gameObject.AddComponent<CircleCollider2D>();
            _collider.radius = 0.4f;
        }

        // --- Initialize State ---
        // Reset the last clicked position (no target initially).
        _lastClickPos = null;
        // Set the initial arrival radius to its minimum configured value.
        _arriveRadius = MinArriveRadius;

        // Define which layers contain obstacles for the avoidance behavior.
        // The frog will attempt to steer around objects on the "Obstacle" and "Snake" layers.
        AvoidParams.ObstacleMask = LayerMask.GetMask("Obstacle", "Snake");

        // Explicitly set the frog's starting health to 3.
        Health = 3;
        Debug.Log("Frog health set to 3 in Start()");

        // Find the Pathfinding system component in the scene. Crucial for A* movement.
        _pathfinding = FindFirstObjectByType<Pathfinding>();
        if (_pathfinding == null)
        {
            Debug.LogError("Pathfinding component not found in scene!");
            return;
        }

        // Find the AStarGrid component in the scene. Needed for pathfinding node information and terrain data.
        _astarGrid = FindFirstObjectByType<AStarGrid>();
        if (_astarGrid == null)
        {
            Debug.LogError("AStarGrid component not found in scene!");
            return;
        }

        // Ensure the A* grid is configured to allow diagonal movement between nodes.
        _astarGrid.includeDiagonalNeighbours = true;

        // Initialize stuck detection
        _lastPosition = transform.position;
        _stuckTimer = 0f;

        // Set up terrain effect visualization
        _spriteRenderer = GetComponent<SpriteRenderer>();
        if (_spriteRenderer != null)
        {
            // Store the original color to revert back to after applying effects.
            _normalColor = _spriteRenderer.color;
        }
        // else: Log warning if sprite renderer is missing and visual feedback is desired.

        // If enabled, create and configure the TextMesh for displaying terrain effects above the frog.
        if (showTerrainEffects)
        {
            // Create a new empty GameObject to hold the TextMesh.
            GameObject textObj = new GameObject("TerrainEffectText");
            // Parent the text object to the frog so it moves along with it.
            textObj.transform.parent = transform;
            textObj.transform.localPosition = new Vector3(0, 1.5f, 0); // Position higher above the frog
            _terrainEffectText = textObj.AddComponent<TextMesh>();
            _terrainEffectText.alignment = TextAlignment.Center;
            _terrainEffectText.anchor = TextAnchor.LowerCenter;
            _terrainEffectText.characterSize = 0.15f; // Increased character size
            _terrainEffectText.fontSize = 36; // Increased font size
            _terrainEffectText.fontStyle = FontStyle.Bold; // Make text bold
            // Add outline component for better visibility
        }

        // Decision tree debug text setup is omitted here, assuming it's handled by a UI panel instead of overhead text.
        // The _decisionDebugText variable remains declared but might not be assigned or used if showDecisionTreeDebug is false or UI is preferred.
    }


    void Update()
    {
        // Check for restart if game is over
        if (_isGameOver && Input.GetKeyDown(KeyCode.R))
        {
            // Unpause the game
            Time.timeScale = 1;
            
            // Reload the current scene
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            return;
        }

        // Check health first - this should work in all modes
        if (Health <= 0)
        {
            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = new Color(1f, 0.2f, 0.2f);
            }
            return;
        }

        // Only process input if game is not over
        if (!_isGameOver)
        {
            // Cycle between Human, AI (Decision Tree), and Behavior Tree modes with the A key
            if (Input.GetKeyDown(KeyCode.A))
            {
                controlMode = (ControlMode)(((int)controlMode + 1) % 3);
                Debug.Log("Control mode set to: " + controlMode.ToString());
                
                // Clear path when switching modes
                _currentPath = null;
                _lastClickPos = null;
                _flagSr.enabled = false;
            }

            // Toggle between A* and direct movement with the T key - moved outside of control mode blocks
            if (Input.GetKeyDown(KeyCode.T))
            {
                CurrentMovementType = (CurrentMovementType == MovementType.Direct) ? 
                    MovementType.AStar : MovementType.Direct;
                
                Debug.Log("Movement type set to: " + CurrentMovementType.ToString());
                
                // Clear path when switching modes
                _currentPath = null;
                
                // If we're in the middle of following a path, recalculate it with the new method
                if (_lastClickPos.HasValue)
                {
                    if (CurrentMovementType == MovementType.AStar)
                    {
                        CalculatePathToDestination((Vector2)_lastClickPos);
                    }
                }
            }

            // Check for shooting bubbles at snakes (works in all control modes)
            CheckAndShootAtSnakes();

            if (controlMode == ControlMode.Human)
            {
                // Check whether the player right-clicked (mouse button #1).
                if (Input.GetMouseButtonDown(1))
                {
                    _lastClickPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);

                    // Set the arrival radius dynamically.
                    _arriveRadius = Mathf.Clamp(ArrivePct * ((Vector2)_lastClickPos - (Vector2)transform.position).magnitude, MinArriveRadius, MaxArriveRadius);

                    _flag.position = (Vector2)_lastClickPos + new Vector2(0.55f, 0.55f);
                    _flagSr.enabled = true;
                    
                    // Generate A* path to the clicked position if using A* movement
                    if (CurrentMovementType == MovementType.AStar)
                    {
                        CalculatePathToDestination((Vector2)_lastClickPos);
                    }
                }
            }
            else // AI Control
            {
                // Update the decision tree every frame
                UpdateDecisionTree();
            }
            
            // Check if spacebar is pressed to shoot a bubble manually
            if (Input.GetKeyDown(KeyCode.Space) && BubblePrefab != null)
            {
                ShootBubble();
            }
            
            // Update the path periodically if we're still following a path with A* movement
            if (CurrentMovementType == MovementType.AStar && _currentPath != null && _currentPath.Length > 0)
            {
                // Only update path if periodic updates are enabled
                if (enablePeriodicPathUpdates)
                {
                    _pathUpdateTimer -= Time.deltaTime;
                    if (_pathUpdateTimer <= 0)
                    {
                        _pathUpdateTimer = _pathUpdateInterval;
                        
                        // Only recalculate if we still have a target
                        if (_lastClickPos.HasValue)
                        {
                            CalculatePathToDestination((Vector2)_lastClickPos);
                        }
                    }
                }
            }
            
            // Show debug lines for closest fly and snake
            if (closestFly != null)
                Debug.DrawLine(transform.position, closestFly.transform.position, Color.black);
            if (closestSnake != null)
                Debug.DrawLine(transform.position, closestSnake.transform.position, Color.red);
        }
        
        // Update terrain effect visualization
        if (showTerrainEffects)
        {
            UpdateTerrainEffectVisual();
        }

        // Decision tree debug text is now shown in the GUI panel instead
        // We no longer update the overhead text
        if (showDecisionTreeDebug && _decisionDebugText != null)
        {
            _decisionDebugText.text = _lastDecision;
            _decisionDebugText.transform.up = Vector3.up; // Keep text upright
        }
    }

    void FixedUpdate()
    {
        // Check health first - this should work in all modes
        if (Health <= 0)
        {
            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = new Color(1f, 0.2f, 0.2f);
            }
            // Stop all movement
            _rb.linearVelocity = Vector2.zero;
            _rb.angularVelocity = 0f;
            return;
        }

        Vector2 desiredVel = Vector2.zero;
        float currentMaxSpeed = GetTerrainModifiedSpeed(); // Get terrain-modified speed for both modes

        if (CurrentMovementType == MovementType.AStar)
        {
            // A* pathfinding movement
            if (_currentPath != null && _currentPath.Length > 0 && _currentPathIndex < _currentPath.Length)
            {
                // Get the current waypoint position
                Vector2 currentWaypoint = _currentPath[_currentPathIndex].worldPosition;
                
                // Calculate distance to the current waypoint
                float distanceToWaypoint = Vector2.Distance(transform.position, currentWaypoint);
                
                // Check if we've reached the waypoint
                if (distanceToWaypoint < _waypointReachedDistance)
                {
                    // Move to the next waypoint
                    _currentPathIndex++;
                    
                    // If we've reached the last waypoint, clear the path
                    if (_currentPathIndex >= _currentPath.Length)
                    {
                        _currentPath = null;
                        _lastClickPos = null;
                        _pathRecalculationAttempts = 0; // Reset path recalculation attempts
                        _isHuntingFly = false; // Reset hunting flag when path is complete
                        
                        if (HideFlagOnceReached)
                        {
                            _flagSr.enabled = false;
                        }
                        
                        return;
                    }
                    
                    // Update current waypoint
                    currentWaypoint = _currentPath[_currentPathIndex].worldPosition;
                }
                
                // Calculate desired velocity towards waypoint
                Vector2 toWaypoint = (currentWaypoint - (Vector2)transform.position).normalized;
                desiredVel = toWaypoint * currentMaxSpeed;
                
                // Check if frog is stuck
                CheckIfStuck();
            }
            else if (_lastClickPos != null)
            {
                // If target exists but no path (likely because target is outside grid),
                // Use direct movement instead to reach the target
                Vector2 targetPos = (Vector2)_lastClickPos;
                
                // Use Seek behavior when hunting flies to maintain speed, otherwise use Arrive
                if (_isHuntingFly)
                {
                    desiredVel = Steering.Seek(transform.position, targetPos, currentMaxSpeed, AvoidParams);
                }
                else
                {
                    desiredVel = Steering.Arrive(transform.position, targetPos, _arriveRadius, currentMaxSpeed, AvoidParams);
                }
                
                // If we're getting close to the grid bounds, check if we should recalculate the path
                if (!IsPointInGrid(transform.position) && IsPointInGrid(targetPos))
                {
                    // We've moved outside the grid but target is in grid - recalculate path
                    CalculatePathToDestination(targetPos);
                }
                else if (IsPointInGrid(transform.position) && !IsPointInGrid(targetPos) && 
                         Vector2.Distance(transform.position, targetPos) < 2.0f)
                {
                    // If we're close to the target but it's outside the grid
                    // Use even more direct movement (seek) for the final approach
                    desiredVel = Steering.Seek(transform.position, targetPos, currentMaxSpeed, AvoidParams);
                }
                
                // If we've reached the target position using direct movement
                if (Vector2.Distance(transform.position, targetPos) < Constants.TARGET_REACHED_TOLERANCE)
                {
                    _lastClickPos = null;
                    _isHuntingFly = false; // Reset hunting flag when target is reached
                    if (HideFlagOnceReached)
                    {
                        _flagSr.enabled = false;
                    }
                }
            }
        }
        else
        {
            // Original direct movement
            if (_lastClickPos != null)
            {
                if (((Vector2)_lastClickPos - (Vector2)transform.position).magnitude > Constants.TARGET_REACHED_TOLERANCE)
                {
                    if (_isHuntingFly || SteeringType == FrogSteeringType.Seek)
                    {
                        // Use Seek behavior for hunting flies regardless of the steering type setting
                        desiredVel = Steering.Seek(transform.position, (Vector2)_lastClickPos, currentMaxSpeed, AvoidParams);
                    }
                    else if (SteeringType == FrogSteeringType.Arrive)
                    {
                        // Use terrain-modified speed for arrive - only for non-fly targets
                        desiredVel = Steering.Arrive(transform.position, (Vector2)_lastClickPos, _arriveRadius, currentMaxSpeed, AvoidParams);
                    }
                }
                else
                {
                    _lastClickPos = null;
                    _isHuntingFly = false; // Reset hunting flag when target is reached

                    if (HideFlagOnceReached)
                    {
                        _flagSr.enabled = false;
                    }
                }
            }
        }
        
        // Apply steering forces to the frog
        Vector2 steering = Steering.DesiredVelToForce(desiredVel, _rb, AccelTime, MaxAccel);
        _rb.AddForce(steering);

        UpdateAppearance();
    }

    private void UpdateAppearance()
    {
        // Get current terrain speed modifier
        TerrainType currentTerrain = GetCurrentTerrain();
        float speedModifier = terrainSpeedModifiers[currentTerrain];
        
        // Scale the minimum speed threshold based on terrain
        float adjustedMinSpeed = Constants.MIN_SPEED_TO_ANIMATE * speedModifier;
        
        // Update animation based on adjusted speed threshold
        if (_rb.linearVelocity.magnitude > adjustedMinSpeed)
        {
            _animator.SetBool("Walking", true);
            transform.up = _rb.linearVelocity;
        }
        else
        {
            _animator.SetBool("Walking", false);
        }
    }

    public void TakeDamage()
    {
        if (Health > 0)
        {
            Health--;
            // Set color to red and game over state immediately when health reaches 0
            if (Health <= 0)
            {
                if (_spriteRenderer != null)
                {
                    _spriteRenderer.color = new Color(1f, 0.2f, 0.2f);
                }
                ShowGameOver(false);
            }
        }
    }

    // Calculate the A* path to the destination
    private void CalculatePathToDestination(Vector2 destination)
    {
        // Sanity check for pathfinding system
        if (_pathfinding == null || _astarGrid == null) return;
        
        Vector2 start = transform.position;
        
        // Check if both points are within the grid
        if (!IsPointInGrid(start) || !IsPointInGrid(destination))
        {
            // One of the points is outside the grid - fall back to direct movement
            _currentPath = null;
            return;
        }
        
        // Check for potential snake intersections along direct path (only if dynamic recalculation is enabled)
        bool potentialSnakeIntersection = false;
        if (_pathfinding.enableDynamicPathRecalculation)
        {
            foreach (Snake snake in nearbySnakes)
            {
                if (snake.State == Snake.SnakeState.Benign || snake.State == Snake.SnakeState.Fleeing)
                    continue; // Ignore non-threatening snakes
                    
                // Check if snake might intercept our path
                Vector2 snakePos = snake.transform.position;
                Vector2 snakeVel = Vector2.zero;
                Rigidbody2D snakeRb = snake.GetComponent<Rigidbody2D>();
                if (snakeRb != null)
                {
                    snakeVel = snakeRb.linearVelocity;
                }
                
                // Calculate distance to the line segment from start to destination
                float distanceToPath = DistancePointToLineSegment(snakePos, start, destination);
                
                // Check potential collision in the next few seconds
                for (float t = 0.5f; t <= 2.0f; t += 0.5f)
                {
                    Vector2 predictedSnakePos = snakePos + snakeVel * t;
                    float predictedDistance = DistancePointToLineSegment(predictedSnakePos, start, destination);
                    
                    // If snake will be close to our path, mark as potential intersection
                    if (predictedDistance < 2.0f)
                    {
                        potentialSnakeIntersection = true;
                        break;
                    }
                }
                
                if (potentialSnakeIntersection)
                    break;
            }
        }
        
        // In comparison mode, calculate paths for all heuristics
        if (_heuristicComparisonMode)
        {
            // Store the original heuristic
            HeuristicType originalHeuristic = _pathfinding.heuristicType;
            
            // Clear previous comparison paths
            _comparisonPaths.Clear();
            
            // Calculate paths with each heuristic type
            foreach (HeuristicType heuristicType in System.Enum.GetValues(typeof(HeuristicType)))
            {
                // Set the heuristic type temporarily
                _pathfinding.SetHeuristicType(heuristicType);
                
                // Request path from A* system
                Node[] path = Pathfinding.RequestPath(start, destination);
                
                if (path != null && path.Length > 0)
                {
                    // Store the path for this heuristic
                    _comparisonPaths[heuristicType] = path;
                }
            }
            
            // Restore the original heuristic
            _pathfinding.SetHeuristicType(originalHeuristic);
            
            // Use the path from the current heuristic as the main path
            if (_comparisonPaths.ContainsKey(originalHeuristic))
            {
                _currentPath = _comparisonPaths[originalHeuristic];
            }
            else
            {
                _currentPath = null;
            }
        }
        else
        {
            // Regular path calculation (original behavior)
            
            // Request path from A* system
            Node[] path = Pathfinding.RequestPath(start, destination);
            
            // If path is null or empty, try direct movement
            if (path == null || path.Length == 0)
            {
                _currentPath = null;
                return;
            }
            
            // Apply path modifications if snake intersection detected and dynamic recalculation is enabled
            if (potentialSnakeIntersection && path.Length > 1 && _pathfinding.enableDynamicPathRecalculation)
            {
                // Add a slight detour to avoid potential collision
                List<Node> modifiedPath = new List<Node>(path);
                
                // Find a safe intermediate waypoint
                Vector2 alternateDirection = FindSafeDirectionAwayFromSnakes(start, destination);
                Vector2 midPoint = Vector2.Lerp(start, destination, 0.5f) + alternateDirection * 3.0f;
                
                // Ensure midpoint is in grid
                if (IsPointInGrid(midPoint))
                {
                    Node midNode = _astarGrid.NodeFromWorldPoint(midPoint);
                    if (midNode != null && midNode.walkable)
                    {
                        // Insert the mid point in the path
                        modifiedPath.Insert(modifiedPath.Count / 2, midNode);
                        path = modifiedPath.ToArray();
                    }
                }
            }
            
            // Store path
            _currentPath = path;
        }
        
        // Reset pathfinding-related variables
        _currentPathIndex = 0;
        _pathUpdateTimer = 0f;
        _pathRecalculationAttempts = 0;
        
        // Debug visualization
        DebugDrawPath();
    }
    
    // Helper function to find distance from point to line segment
    private float DistancePointToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
    {
        Vector2 line = lineEnd - lineStart;
        float lineLength = line.magnitude;
        if (lineLength < 0.0001f) return Vector2.Distance(point, lineStart);
        
        // Project point onto line
        Vector2 lineDir = line / lineLength;
        float projection = Vector2.Dot(point - lineStart, lineDir);
        
        // If projection is outside the line segment, return distance to nearest endpoint
        if (projection <= 0) return Vector2.Distance(point, lineStart);
        if (projection >= lineLength) return Vector2.Distance(point, lineEnd);
        
        // If projection is on the line segment, return perpendicular distance
        Vector2 projectedPoint = lineStart + lineDir * projection;
        return Vector2.Distance(point, projectedPoint);
    }
    
    // Find a safe direction away from snakes for path modification
    private Vector2 FindSafeDirectionAwayFromSnakes(Vector2 start, Vector2 destination)
    {
        Vector2 pathDirection = (destination - start).normalized;
        Vector2 bestDirection = Vector2.zero;
        float maxSafetyScore = 0;
        
        // Try different directions perpendicular to the path
        Vector2 perpendicular = new Vector2(-pathDirection.y, pathDirection.x);
        
        // Check both perpendicular directions
        for (int i = -1; i <= 1; i += 2) // -1 and 1
        {
            Vector2 testDirection = perpendicular * i;
            float safetyScore = 1.0f;
            
            // Reduce score if direction leads toward snakes
            foreach (Snake snake in nearbySnakes)
            {
                Vector2 toSnake = snake.transform.position - (Vector3)start;
                float distanceToSnake = toSnake.magnitude;
                
                // Only consider nearby snakes that could be a threat
                if (distanceToSnake < scaredRange * 2 && 
                    snake.State != Snake.SnakeState.Benign && 
                    snake.State != Snake.SnakeState.Fleeing)
                {
                    // Calculate how much this direction leads toward the snake
                    float dot = Vector2.Dot(testDirection, toSnake.normalized);
                    
                    // If positive dot product, the direction leads somewhat toward the snake
                    if (dot > 0)
                    {
                        // Reduce safety score based on how directly it points to the snake and how close the snake is
                        safetyScore -= dot * (1.0f - distanceToSnake / (scaredRange * 2));
                    }
                }
            }
            
            // If this direction is safer than the best one found so far, update
            if (safetyScore > maxSafetyScore)
            {
                maxSafetyScore = safetyScore;
                bestDirection = testDirection;
            }
        }
        
        // If no safe direction was found, use a random perpendicular
        if (bestDirection == Vector2.zero)
        {
            bestDirection = perpendicular * (UnityEngine.Random.value > 0.5f ? 1 : -1);
        }
        
        return bestDirection;
    }

    // Method to check if a point is within the A* grid
    private bool IsPointInGrid(Vector2 point)
    {
        if (_astarGrid == null) return false;
        
        // Get grid bounds
        Vector2 gridWorldSize = _astarGrid.gridWorldSize;
        Vector2 gridCenter = (Vector2)_astarGrid.transform.position;
        Vector2 gridMin = gridCenter - gridWorldSize / 2;
        Vector2 gridMax = gridCenter + gridWorldSize / 2;
        
        // Check if point is within grid bounds
        return (point.x >= gridMin.x && point.x <= gridMax.x && 
                point.y >= gridMin.y && point.y <= gridMax.y);
    }

    // Draw the path for debugging
    private void DebugDrawPath()
    {
        // In comparison mode, draw all paths with their respective colors
        if (_heuristicComparisonMode && _comparisonPaths.Count > 0)
        {
            // Draw all comparison paths
            foreach (var kvp in _comparisonPaths)
            {
                HeuristicType heuristic = kvp.Key;
                Node[] path = kvp.Value;
                
                if (path == null || path.Length == 0)
                    continue;
                
                // Get color for this heuristic
                Color pathColor = _heuristicColors.ContainsKey(heuristic) ? 
                    _heuristicColors[heuristic] : Color.white;
                
                // Apply offset to each path to make them all visible
                Vector2 offset = GetHeuristicOffset(heuristic);
                
                // Draw path with offset
                DrawPathWithOffset(path, pathColor, offset);
            }
            
            return; // Early return after drawing all comparison paths
        }
        
        // Original path drawing code for non-comparison mode
        if (_currentPath != null && _currentPath.Length > 0)
        {
            // Choose color based on active settings
            Color pathColor;
            float displayDuration;
            
            // Reference to the DrawGUI to check if heuristic colors are enabled
            if (_drawGUIScript != null && _drawGUIScript.showHeuristicColors && _pathfinding != null)
            {
                // Set color based on heuristic type
                switch (_pathfinding.heuristicType)
                {
                    case HeuristicType.Manhattan:
                        pathColor = new Color(0.8f, 0.2f, 0.2f); // Reddish
                        break;
                    case HeuristicType.Euclidean:
                        pathColor = new Color(0.2f, 0.8f, 0.2f); // Greenish
                        break;
                    case HeuristicType.Chebyshev:
                        pathColor = new Color(0.2f, 0.2f, 0.8f); // Bluish
                        break;
                    case HeuristicType.Octile:
                        pathColor = new Color(0.8f, 0.8f, 0.2f); // Yellow
                        break;
                    case HeuristicType.Custom:
                        pathColor = new Color(0.8f, 0.2f, 0.8f); // Magenta
                        break;
                    default:
                        pathColor = Color.white;
                        break;
                }
                
                // Use longer display time when showing heuristic colors
                displayDuration = 1.0f;
            }
            else
            {
                // Set color based on which features are enabled (original behavior)
                if (!enablePeriodicPathUpdates && !_pathfinding.enableDynamicPathRecalculation)
                {
                    // Raw A* path - use bright red for visibility
                    pathColor = Color.red;
                    // Use longer display time when not recalculating (show until next manual calculation)
                    displayDuration = 5.0f;
                }
                else if (_pathfinding.enableDynamicPathRecalculation)
                {
                    // Dynamic recalculation - use yellow
                    pathColor = Color.yellow;
                    displayDuration = _pathUpdateInterval;
                }
                else
                {
                    // Periodic updates only - use green
                    pathColor = Color.green;
                    displayDuration = _pathUpdateInterval;
                }
            }
            
            // Draw the path with no offset
            DrawPathWithOffset(_currentPath, pathColor, Vector2.zero);
        }
    }
    
    // Helper method to draw a path with an offset
    private void DrawPathWithOffset(Node[] path, Color pathColor, Vector2 offset)
    {
        if (path == null || path.Length == 0)
            return;
            
        float displayDuration = 1.0f;
        
        // Draw a line from the frog to the first waypoint with offset
        Vector2 offsetStart = (Vector2)transform.position + offset;
        Vector2 offsetFirstWaypoint = (Vector2)path[0].worldPosition + offset;
        Debug.DrawLine(offsetStart, offsetFirstWaypoint, pathColor, displayDuration);
        
        // Draw lines between waypoints with offset
        for (int i = 0; i < path.Length - 1; i++)
        {
            Vector2 offsetCurrentWaypoint = (Vector2)path[i].worldPosition + offset;
            Vector2 offsetNextWaypoint = (Vector2)path[i + 1].worldPosition + offset;
            
            Debug.DrawLine(offsetCurrentWaypoint, offsetNextWaypoint, pathColor, displayDuration);
        }
    }
    
    // Helper method to get offset for each heuristic type
    private Vector2 GetHeuristicOffset(HeuristicType heuristic)
    {
        // Apply different offsets based on heuristic type
        switch (heuristic)
        {
            case HeuristicType.Manhattan:
                return new Vector2(-0.2f, -0.2f);
            case HeuristicType.Euclidean:
                return new Vector2(0.2f, 0.2f);
            case HeuristicType.Chebyshev:
                return new Vector2(-0.2f, 0.2f);
            case HeuristicType.Octile:
                return new Vector2(0.2f, -0.2f);
            case HeuristicType.Custom:
                return new Vector2(0.0f, 0.3f);
            default:
                return Vector2.zero;
        }
    }

    // Helper to get the current movement type as a string for UI display
    public string GetMovementTypeString()
    {
        return CurrentMovementType == MovementType.AStar ? "A* Pathfinding" : "Direct Movement";
    }

    //TODO Implement the following Decision Tree
    // 1. no health <= 0 --> set speed to 0 and color to red (1, 0.2, 0.2)
    // 2. user clicked --> go to that click
    // 3. nearby/outside of screen --> go towards screen center
    // 4. closest snake nearby --> flee from snake within the screen
    // 5. closest fly within hunt range --> go towards that fly
    // 6. otherwise --> go to center of the screen (already implemented as default behavior)

    //TODO SUGGESTED IMPROVEMENTS:
    //go to the center of mass of flies within screen
    //if 2 snake nearby -> freeze
    //Handle shooting bubbles
    //Come up with a better DT, for example: find flies that are within a circle around the frog that doesnt include any snake
    //Extra0 shoot bubble?
    //Extra1 update your code so that: 
    //Extra2 update your code with a better DT (find flies that are within a circle around the frog that doesnt include any snake)
    //Gameplay: tweak speed, range, acceleration and anchoring

    private Vector2 decideMovement()
    {
        // If health is 0, set color to red and stop moving
        if (Health <= 0)
        {
            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = new Color(1f, 0.2f, 0.2f);
            }
            return Vector2.zero;
        }

        // If user clicked, go to that position
        if (_lastClickPos != null)
        {
            return getVelocityTowardsFlag();
        }

        // Find closest fly and snake
        findClosestFly();
        findClosestSnake();

        // If out of screen bounds, return to center
        if (isOutOfScreen(transform))
        {
            return Steering.Seek(transform.position, Vector2.zero, MaxSpeed, AvoidParams);
        }

        // If snake is nearby, flee
        if (closestSnake != null && distanceToClosestSnake < scaredRange)
        {
            // Avoid ALL snakes regardless of their state
            Vector2 fleeDirection = ((Vector2)transform.position - (Vector2)closestSnake.transform.position).normalized;
            Vector2 fleeTarget = (Vector2)transform.position + fleeDirection * scaredRange;
            

            
            // Use Steering.Seek without returning
            Steering.Seek(transform.position, fleeTarget, MaxSpeed, AvoidParams);
        }

        // If fly is nearby, go to it (only if fly is on screen)
        if (closestFly != null && distanceToClosestFly < huntRange)
        {
            // Skip if fly is out of screen or heading out of screen
            bool flyOutOfScreen = isOutOfScreen(closestFly.transform);
            
            // Calculate predicted position to check if it's heading out of screen
            Vector2 predictedPosition = (Vector2)closestFly.transform.position;
            Rigidbody2D flyRb = closestFly.GetComponent<Rigidbody2D>();
            if (flyRb != null)
            {
                predictedPosition += flyRb.linearVelocity * 0.5f;
            }
            
            // Check if predicted position is out of screen
            Vector2 screenPos = Camera.main.WorldToViewportPoint(predictedPosition);
            bool predictedPosOutOfScreen = screenPos.x < 0 || screenPos.x > 1 || 
                                          screenPos.y < 0 || screenPos.y > 1;
            
            // Only chase the fly if it's on screen and not heading out
            if (!flyOutOfScreen && !predictedPosOutOfScreen)
            {
                return Steering.Seek(transform.position, closestFly.transform.position, MaxSpeed, AvoidParams);
            }
        }

        // Default: go to center
        return Steering.Seek(transform.position, Vector2.zero, MaxSpeed, AvoidParams);
    }

    private Vector2 getVelocityTowardsFlag()
    {
        Vector2 desiredVel = Vector2.zero;
        if (_lastClickPos != null)
        {
            if (((Vector2)_lastClickPos - (Vector2)gameObject.transform.position).magnitude > Constants.TARGET_REACHED_TOLERANCE)
            {
                // Use terrain-modified speed here too
                float currentMaxSpeed = GetTerrainModifiedSpeed();
                desiredVel = Steering.ArriveDirect(gameObject.transform.position, (Vector2)_lastClickPos, _arriveRadius, currentMaxSpeed);
            }
            else
            {
                _lastClickPos = null;

                if (HideFlagOnceReached)
                {
                    _flagSr.enabled = false;
                }
            }
        }
        return desiredVel;
    }

    private void findClosestFly()
    {
        distanceToClosestFly = Mathf.Infinity;
        closestFly = null;

        Fly[] allFlies = FindObjectsByType<Fly>(FindObjectsSortMode.None);
        foreach (Fly fly in allFlies)
        {
            if (fly.State != Fly.FlyState.Dead)
            {
                // Skip flies that are out of screen
                if (isOutOfScreen(fly.transform))
                {
                    continue;
                }
                
                // Calculate predicted position based on fly's velocity
                Vector2 predictedPosition = (Vector2)fly.transform.position;
                Rigidbody2D flyRb = fly.GetComponent<Rigidbody2D>();
                if (flyRb != null)
                {
                    // Predict position 0.5 seconds ahead
                    predictedPosition += flyRb.linearVelocity * 0.5f;
                }
                
                // Check if predicted position is out of screen
                Vector2 screenPos = Camera.main.WorldToViewportPoint(predictedPosition);
                bool predictedPosOutOfScreen = screenPos.x < 0 || screenPos.x > 1 || 
                                              screenPos.y < 0 || screenPos.y > 1;
                if (predictedPosOutOfScreen)
                {
                    continue;
                }

                float distanceToFly = Vector2.Distance(transform.position, predictedPosition);
                if (distanceToFly < distanceToClosestFly)
                {
                    closestFly = fly;
                    distanceToClosestFly = distanceToFly;
                }
            }
        }
    }

    private void findClosestSnake()
    {
        distanceToClosestSnake = Mathf.Infinity;
        closestSnake = null;
        nearbySnakes.Clear(); // Clear the list first

        Snake[] allSnakes = FindObjectsByType<Snake>(FindObjectsSortMode.None);
        foreach (Snake snake in allSnakes)
        {
            float distanceToSnake = (snake.transform.position - transform.position).magnitude;
            
            // Track the closest snake
            if (distanceToSnake < distanceToClosestSnake)
            {
                closestSnake = snake;
                distanceToClosestSnake = distanceToSnake;
            }
            
            // Add any snake within extended scared range to our list of threats
            if (distanceToSnake < scaredRange * 1.5f)
            {
                nearbySnakes.Add(snake);
            }
        }
        
        // Sort nearby snakes by distance (closest first)
        nearbySnakes.Sort((a, b) => 
            Vector2.Distance(transform.position, a.transform.position)
            .CompareTo(Vector2.Distance(transform.position, b.transform.position)));
    }

    //TODO Check wether the current transform is out of screen (true) or not (false)
    public bool isOutOfScreen(Transform transform)
    {
        Vector2 screenPos = Camera.main.WorldToViewportPoint(transform.position);
        return screenPos.x < 0 || screenPos.x > 1 || screenPos.y < 0 || screenPos.y > 1;
    }

    // Method to check for snakes and auto-shoot at them - only called from AI control mode
    private void CheckAndShootAtSnakes()
    {
        // Don't shoot bubbles when in "Freeze" state or in Human control mode
        if (_currentBehaviorState == "Freeze" || controlMode == ControlMode.Human)
        {
            return;
        }
        
        // Find the closest snake if we don't have one
        if (closestSnake == null)
        {
            findClosestSnake();
            if (closestSnake == null)
            {
                return;
            }
        }
        
        // Check if we can shoot a bubble
        if (closestSnake != null && Time.time - lastBubbleTime > bubbleCooldown)
        {
            bool shouldShootBubble = false;
            
            // Check snake behavior based on control mode
            if (closestSnake.controlMode == Snake.ControlMode.FSM)
            {
                // For FSM-controlled snakes, check their state
                // We should shoot at snakes in PatrolAway, PatrolHome, or Attack states
                shouldShootBubble = (closestSnake.State == Snake.SnakeState.PatrolAway || 
                                    closestSnake.State == Snake.SnakeState.PatrolHome || 
                                    closestSnake.State == Snake.SnakeState.Attack);
            }
            else
            {
                // For BehaviorTree-controlled snakes, always shoot unless they're too far
                shouldShootBubble = true;
            }
            
            // Check if snake is within range and we should shoot
            if (shouldShootBubble && distanceToClosestSnake <= bubbleRange)
            {
                // Get snake's rigidbody to predict movement
                Rigidbody2D snakeRb = closestSnake.GetComponent<Rigidbody2D>();
                Vector2 targetPosition = (Vector2)closestSnake.transform.position;
                
                // Predict snake movement if it has velocity
                if (snakeRb != null && snakeRb.linearVelocity.magnitude > 0.1f)
                {
                    // Lead the target based on distance
                    float leadTime = distanceToClosestSnake / 10f; // Adjust the divisor based on bubble speed
                    targetPosition += snakeRb.linearVelocity * leadTime;
                }
                
                // Shoot the bubble
                ShootBubbleAt(targetPosition);
            }
        }
    }
    
    // Helper method to configure bubble components
    private void ConfigureBubble(GameObject bubble)
    {
        // Ensure the bubble script is properly configured
        Bubble bubbleScript = bubble.GetComponent<Bubble>();
        if (bubbleScript != null)
        {
            // Set default values if not already set
            if (bubbleScript.Speed <= 0)
                bubbleScript.Speed = 5f;
            if (bubbleScript.LifeTime <= 0)
                bubbleScript.LifeTime = 2f;
        }
        else
        {
            // Add the Bubble script if it doesn't exist
            bubbleScript = bubble.AddComponent<Bubble>();
            bubbleScript.Speed = 5f;
            bubbleScript.LifeTime = 2f;
        }
        
        // Make sure the bubble has a collider
        if (!bubble.GetComponent<Collider2D>())
        {
            CircleCollider2D collider = bubble.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = 0.3f;
        }
        
        // Make sure the bubble has a rigidbody
        if (!bubble.GetComponent<Rigidbody2D>())
        {
            Rigidbody2D rb = bubble.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0;
        }
    }

    // Shoot a bubble at a specific target position
    private void ShootBubbleAt(Vector3 targetPosition)
    {
        if (BubblePrefab == null) return;
        
        // Calculate direction to target
        Vector2 direction = ((Vector2)targetPosition - (Vector2)transform.position).normalized;
        
        // Calculate spawn position (in front of the frog's mouth)
        Vector2 spawnPosition = (Vector2)transform.position + direction * BubbleOffset;
        
        // Calculate rotation towards target
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        Quaternion rotation = Quaternion.Euler(0, 0, angle);
        
        // Instantiate the bubble
        GameObject bubble = Instantiate(BubblePrefab, spawnPosition, rotation);
        
        // Configure the bubble
        ConfigureBubble(bubble);
        
        // Update the last bubble time
        lastBubbleTime = Time.time;
        
        // Debug message
        Debug.Log("Frog shot bubble at position: " + targetPosition);
    }

    // Shoot a bubble in the direction the frog is facing (manual)
    private void ShootBubble()
    {
        // Don't shoot bubbles when in "Freeze" state
        if (_currentBehaviorState == "Freeze")
        {
            return;
        }
        
        // Calculate spawn position (in front of the frog's mouth)
        Vector2 direction = transform.up.normalized;
        Vector2 spawnPosition = (Vector2)transform.position + direction * BubbleOffset;
        
        // Instantiate the bubble
        GameObject bubble = Instantiate(BubblePrefab, spawnPosition, transform.rotation);
        
        // Configure the bubble
        ConfigureBubble(bubble);
        
        // Update the last bubble time
        lastBubbleTime = Time.time;
        
        // Debug message
        Debug.Log("Frog shot bubble manually");
    }
    
    // Coroutine to restore the frog's orientation after shooting
    private IEnumerator RestoreOrientationAfterDelay(Vector3 originalUp, float delay)
    {
        yield return new WaitForSeconds(delay);
        
        // Only restore if the frog isn't moving much
        if (_rb.linearVelocity.magnitude < 0.8f)
        {
            transform.up = originalUp;
        }
        // Otherwise let the movement code handle orientation
    }

    // Method for game win/loss conditions
    public void ShowGameOver(bool isWin)
    {
        Debug.Log("Game over! " + (isWin ? "Player won!" : "Player lost!") + " Current health: " + Health);
        
        // Set game over state
        _isGameOver = true;
        
        // Hide the flag if it's visible
        if (_flagSr != null)
        {
            _flagSr.enabled = false;
        }
        
        // Just pause the game - let the GUI handle showing game over state
        Time.timeScale = 0;
    }

    // Get the current terrain type at the frog's position
    public TerrainType GetCurrentTerrain()
    {
        if (_astarGrid != null)
        {
            Node currentNode = _astarGrid.NodeFromWorldPoint(transform.position);
            return currentNode.terrainType;
        }
        return TerrainType.Normal;
    }

    // Get the modified speed based on current terrain
    public float GetTerrainModifiedSpeed()
    {
        TerrainType currentTerrain = GetCurrentTerrain();
        float speedModifier = terrainSpeedModifiers[currentTerrain];
        return MaxSpeed * speedModifier;
    }

    private void UpdateTerrainEffectVisual()
    {
        TerrainType currentTerrain = GetCurrentTerrain();
        float speedModifier = terrainSpeedModifiers[currentTerrain];

        // Update text
        if (_terrainEffectText != null)
        {
            // Make the text more descriptive and format the percentage better
            string speedText = (speedModifier * 100).ToString("F0"); // Convert to percentage with no decimals
            string movementType = CurrentMovementType == MovementType.AStar ? "[A*]" : "[Direct]";
            _terrainEffectText.text = $"{movementType}\n{currentTerrain}\n{speedText}% Speed";
            
            // Set text color based on terrain with higher contrast
            switch (currentTerrain)
            {
                case TerrainType.Normal:
                    _terrainEffectText.color = new Color(1f, 1f, 1f, 0.9f); // Bright white
                    break;
                case TerrainType.Water:
                    _terrainEffectText.color = new Color(0f, 1f, 1f, 0.9f); // Bright cyan
                    break;
                case TerrainType.Sand:
                    _terrainEffectText.color = new Color(1f, 0.9f, 0f, 0.9f); // Bright yellow
                    break;
                case TerrainType.Mud:
                    _terrainEffectText.color = new Color(0.8f, 0.4f, 0f, 0.9f); // Bright brown
                    break;
            }

            // Make text always face up regardless of frog rotation
            _terrainEffectText.transform.up = Vector3.up;
        }

        // Update frog tint based on terrain
        if (_spriteRenderer != null)
        {
            Color terrainTint = _normalColor;
            switch (currentTerrain)
            {
                case TerrainType.Water:
                    terrainTint *= new Color(0.8f, 0.8f, 1f);
                    break;
                case TerrainType.Sand:
                    terrainTint *= new Color(1f, 1f, 0.8f);
                    break;
                case TerrainType.Mud:
                    terrainTint *= new Color(0.8f, 0.7f, 0.6f);
                    break;
            }
            _spriteRenderer.color = Color.Lerp(_spriteRenderer.color, terrainTint, Time.deltaTime * 5f);
        }
    }

    private void UpdateDecisionTree()
    {
        // Check for user clicks first - allow manual overrides even in AI mode
        // Check whether the player right-clicked (mouse button #1)
        if (Input.GetMouseButtonDown(1))
        {
            _lastClickPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);

            // Set the arrival radius dynamically.
            _arriveRadius = Mathf.Clamp(ArrivePct * ((Vector2)_lastClickPos - (Vector2)transform.position).magnitude, MinArriveRadius, MaxArriveRadius);

            _flag.position = (Vector2)_lastClickPos + new Vector2(0.55f, 0.55f);
            _flagSr.enabled = true;
            
            // Generate A* path to the clicked position if using A* movement
            if (CurrentMovementType == MovementType.AStar)
            {
                CalculatePathToDestination((Vector2)_lastClickPos);
            }
            
            // Mark this as a user target
            _isUserTarget = true;
            
            // Set decision text to show user override
            _lastDecision = "User clicked - overriding AI";
            SetBehaviorState("User Override");
            _lastDecisionTime = Time.time; // Reset decision timer
            return; // Skip AI decision making if user clicked
        }

        // If we have a user target, continue going to it until reached
        if (_isUserTarget && _lastClickPos.HasValue)
        {
            // Check if we've reached the target
            float distanceToTarget = Vector2.Distance(transform.position, _lastClickPos.Value);
            if (distanceToTarget <= Constants.TARGET_REACHED_TOLERANCE)
            {
                // Target reached, clear user target flag
                _isUserTarget = false;
                
                if (HideFlagOnceReached)
                {
                    _flagSr.enabled = false;
                }
            }
            else
            {
                // Still going to user target, skip AI decisions
                _lastDecision = "Following user target";
                SetBehaviorState("User Override");
                return;
            }
        }

        // Only update decisions at fixed intervals to reduce overhead
        if (Time.time - _lastDecisionTime < _decisionInterval)
        {
            return;
        }
        _lastDecisionTime = Time.time;

        // Find closest fly and snake (and nearby snakes)
        findClosestFly();
        findClosestSnake();
        
        // Auto bubble shooting logic has been moved to Update and CheckAndShootAtSnakes methods

        // Decision Tree Logic:
        // 1. If health is 0, don't move and turn red
        if (Health <= 0)
        {
            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = new Color(1f, 0.2f, 0.2f);
            }
            _lastClickPos = null;
            _flagSr.enabled = false;
            _isHuntingFly = false;
            _lastDecision = "Health 0: Stop";
            SetBehaviorState("Dead");
            return;
        }

        // 2. User clicked is now handled at the beginning of the function
        
        // 3. If multiple snakes are nearby (2 or more), freeze
        if (nearbySnakes.Count >= 2)
        {
            _lastClickPos = null;
            _flagSr.enabled = false;
            _isHuntingFly = false;
            _lastDecision = "Multiple snakes: Freeze";
            SetBehaviorState("Freeze");
            return;
        }

        // 4. If any snake is nearby, flee with improved logic
        if (nearbySnakes.Count > 0 && distanceToClosestSnake < scaredRange)
        {
            // Calculate weighted flee position considering all nearby snakes
            Vector2 fleeTarget = CalculateFleePosition();
            
            // Set the flee target as our destination and use seek behavior
            SetSmoothTarget(fleeTarget);
            _isHuntingFly = false;
            _lastDecision = $"Fleeing from {nearbySnakes.Count} snake(s)";
            SetBehaviorState("Fleeing");
            return;
        }

        // 5. Find the best fly to target
        if (closestFly != null)
        {
            // Find all flies in hunt range with a more sophisticated scoring system
            Fly[] allFlies = FindObjectsByType<Fly>(FindObjectsSortMode.None);
            Fly bestFly = null;
            float bestScore = float.NegativeInfinity;
            
            // Calculate center of mass of all flies
            Vector2 centerOfMass = Vector2.zero;
            int validFlies = 0;
            
            foreach (Fly fly in allFlies)
            {
                if (fly.State != Fly.FlyState.Dead)
                {
                    centerOfMass += (Vector2)fly.transform.position;
                    validFlies++;
                }
            }
            
            if (validFlies > 0)
            {
                centerOfMass /= validFlies;
            }
            
            // Count flies inside grid vs outside grid
            int fliesInGrid = 0;
            int fliesOutsideGrid = 0;
            
            // Pre-check which flies are in grid
            foreach (Fly fly in allFlies)
            {
                if (fly.State != Fly.FlyState.Dead)
                {
                    Vector2 flyPos = fly.transform.position;
                    if (IsPointInGrid(flyPos))
                    {
                        fliesInGrid++;
                    }
                    else
                    {
                        fliesOutsideGrid++;
                    }
                }
            }
            
            // If there are flies in the grid and we're using A*, prefer them strongly
            bool preferInGridFlies = fliesInGrid > 0 && CurrentMovementType == MovementType.AStar;
            
            foreach (Fly fly in allFlies)
            {
                if (fly.State != Fly.FlyState.Dead)
                {
                    // Calculate predicted position based on fly's velocity with better prediction
                    Vector2 predictedPosition = PredictFlyPosition(fly);
                    
                    float distanceToFly = Vector2.Distance(transform.position, predictedPosition);
                    
                    // Skip if too far
                    if (distanceToFly > huntRange * 1.5f) continue;
                    
                    // Skip flies that are out of screen or heading out of screen
                    bool flyOutOfScreen = isOutOfScreen(fly.transform);
                    bool predictedPosOutOfScreen = false;
                    
                    // Check if the predicted position is out of screen
                    Vector2 screenPos = Camera.main.WorldToViewportPoint(predictedPosition);
                    predictedPosOutOfScreen = screenPos.x < 0 || screenPos.x > 1 || screenPos.y < 0 || screenPos.y > 1;
                    
                    // Skip this fly if it's out of screen or heading out of screen
                    if (flyOutOfScreen || predictedPosOutOfScreen)
                    {
                        continue;
                    }
                    
                    // Skip flies that are too close to snakes to avoid accidental collisions
                    bool tooCloseToSnake = false;
                    foreach (Snake snake in nearbySnakes) 
                    {
                        if (snake.State == Snake.SnakeState.Attack) // Only worry about attacking snakes
                        {
                            float snakeToFlyDistance = Vector2.Distance(predictedPosition, snake.transform.position);
                            if (snakeToFlyDistance < 2.5f) // Increased buffer to be safer
                            {
                                tooCloseToSnake = true;
                                break;
                            }
                            
                            // Also check if path to fly passes near a snake
                            Vector2 directionToFly = (predictedPosition - (Vector2)transform.position).normalized;
                            Vector2 directionToSnake = ((Vector2)snake.transform.position - (Vector2)transform.position).normalized;
                            float dot = Vector2.Dot(directionToFly, directionToSnake);
                            
                            // If the fly is in roughly the same direction as the snake AND the snake is closer
                            float distanceToSnake = Vector2.Distance(transform.position, snake.transform.position);
                            if (dot > 0.7f && distanceToSnake < distanceToFly) // More than ~45 degree alignment
                            {
                                tooCloseToSnake = true;
                                break;
                            }
                        }
                    }
                    
                    if (tooCloseToSnake)
                    {
                        continue; // Skip this fly as it's too dangerous
                    }
                    
                    // 1. Calculate safety score (distance to nearest snake)
                    float safetyScore = 0f;
                    if (nearbySnakes.Count > 0)
                    {
                        // Calculate minimum distance to any snake (all are threats)
                        float minSnakeDistance = float.MaxValue;
                        foreach (Snake snake in nearbySnakes)
                        {
                            // Consider snake state in threat assessment
                            float threatLevel = 1.0f; // Base threat level
                            if (snake.State == Snake.SnakeState.Attack)
                            {
                                threatLevel = 1.5f; // Attacking snakes are more dangerous
                            }
                            else if (snake.State == Snake.SnakeState.Benign || snake.State == Snake.SnakeState.Fleeing)
                            {
                                threatLevel = 0.5f; // Less dangerous states
                            }
                            
                            float distToSnake = Vector2.Distance(predictedPosition, snake.transform.position);
                            
                            // Apply threat level to effective distance
                            float adjustedDistance = distToSnake / threatLevel;
                            minSnakeDistance = Mathf.Min(minSnakeDistance, adjustedDistance);
                        }
                        
                        // Score based on distance to closest snake
                        if (minSnakeDistance < float.MaxValue)
                        {
                            safetyScore = Mathf.Clamp01(minSnakeDistance / (scaredRange * 2f));
                            
                            // Apply stronger curve to safety score - make it drop off faster when getting closer to danger
                            safetyScore = Mathf.Pow(safetyScore, 2f);
                        }
                        else
                        {
                            safetyScore = 1.0f; // No threatening snakes
                        }
                    }
                    else
                    {
                        safetyScore = 1.0f; // No snakes nearby
                    }
                    
                    // 2. Calculate path score (check terrain along path)
                    float pathScore = 1f;
                    
                    // Only check terrain if the position is within the grid
                    if (IsPointInGrid(predictedPosition))
                    {
                        Node flyNode = _astarGrid.NodeFromWorldPoint(predictedPosition);
                        if (flyNode != null)
                        {
                            switch (flyNode.terrainType)
                            {
                                case TerrainType.Water:
                                    pathScore *= 0.7f;
                                    break;
                                case TerrainType.Sand:
                                    pathScore *= 0.8f;
                                    break;
                                case TerrainType.Mud:
                                    pathScore *= 0.5f;
                                    break;
                            }
                        }
                    }
                    
                    // 3. Calculate center of mass influence
                    float centerInfluence = 0f;
                    if (validFlies > 1)
                    {
                        float distToCenter = Vector2.Distance(predictedPosition, centerOfMass);
                        centerInfluence = Mathf.Clamp01(1f - (distToCenter / huntRange));
                    }
                    
                    // 4. Calculate interception score (how well we can intercept the fly)
                    float interceptionScore = 0f;
                    Rigidbody2D flyRb = fly.GetComponent<Rigidbody2D>();
                    if (flyRb != null && flyRb.linearVelocity.magnitude > 0.1f)
                    {
                        // Get relative velocity between frog and fly
                        Vector2 relativeVelocity = flyRb.linearVelocity - _rb.linearVelocity;
                        
                        // Calculate time to intercept based on distance and velocities
                        float timeToIntercept = distanceToFly / (GetTerrainModifiedSpeed() + 0.1f);
                        
                        // Score faster interception higher
                        interceptionScore = Mathf.Clamp01(1f - (timeToIntercept / 2f));
                    }
                    
                    // 5. Grid position factor - for A* pathfinding
                    float gridPositionFactor = 1.0f;
                    if (CurrentMovementType == MovementType.AStar)
                    {
                        bool isInGrid = IsPointInGrid(predictedPosition);
                        
                        if (!isInGrid)
                        {
                            if (preferInGridFlies)
                            {
                                // If there are flies inside the grid, strongly penalize outside flies
                                gridPositionFactor = 0.3f;
                            }
                            else
                            {
                                // If no flies in grid, milder penalty
                                gridPositionFactor = 0.7f;
                            }
                        }
                        else
                        {
                            // Small bonus for being in grid
                            gridPositionFactor = 1.1f;
                        }
                    }
                    
                    // 6. Combine scores with adjusted weights
                    float distanceWeight = 0.3f;
                    float safetyWeight = 0.25f;
                    float pathWeight = 0.15f;
                    float centerWeight = 0.15f;
                    float interceptionWeight = 0.15f;
                    
                    float score = (1f - (distanceToFly / huntRange)) * distanceWeight + 
                                 safetyScore * safetyWeight + 
                                 pathScore * pathWeight + 
                                 centerInfluence * centerWeight + 
                                 interceptionScore * interceptionWeight;
                    
                    // Apply grid position factor
                    score *= gridPositionFactor;
                    
                    // Debug logs for flies outside grid when using A*
                    if (!IsPointInGrid(predictedPosition) && CurrentMovementType == MovementType.AStar)
                    {
                        Debug.Log($"Fly outside grid received score {score} with factor {gridPositionFactor}");
                    }
                    
                    // Current best score comparison with hysteresis (stick with current target unless new one is significantly better)
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestFly = fly;
                    }
                }
            }
            
            if (bestFly != null)
            {
                // Calculate final target position with prediction
                Vector2 targetPosition = PredictFlyPosition(bestFly);
                
                SetSmoothTarget(targetPosition);
                _isHuntingFly = true; // Set this flag to true when hunting flies
                
                // Update decision text based on whether the fly is in grid or not
                if (CurrentMovementType == MovementType.AStar && !IsPointInGrid(targetPosition))
                {
                    _lastDecision = "Hunting fly outside grid (direct movement)";
                }
                else
                {
                    _lastDecision = "Hunting best fly";
                }
                SetBehaviorState("Hunting");
                return;
            }
        }

        // 6. If out of screen bounds, move towards center with smooth wandering
        if (isOutOfScreen(transform))
        {
            // Instead of directly going to center, add slight randomness for natural movement
            float wanderRadius = 0.5f;
            float wanderNoise = Mathf.PerlinNoise(Time.time * 0.1f, 0) * 2.0f - 1.0f;
            float wanderAngle = wanderNoise * 30f; // 30 degree variation max
            Vector2 wanderOffset = SteeringCalcs.Steering.rotate(Vector2.right, wanderAngle) * wanderRadius;
            Vector2 targetPos = wanderOffset; // This adds slight offset to center position (0,0)
            
            SetSmoothTarget(targetPos);
            _isHuntingFly = false;
            _lastDecision = "Returning to center";
            SetBehaviorState("Returning");
            return;
        }

        // 7. Default behavior: go to center of the screen with wandering
        // Only wander if close to center, otherwise just go to center
        Vector2 centerTarget;
        float distanceToCenterPoint = Vector2.Distance(transform.position, Vector2.zero);
        
        if (distanceToCenterPoint < 2.0f)
        {
            // Near center, apply gentle wandering behavior
            float centerWanderRadius = 3.0f;
            float centerWanderNoise = Mathf.PerlinNoise(Time.time * 0.05f, 0) * 2.0f - 1.0f;
            float centerWanderAngle = centerWanderNoise * 180f;
            Vector2 centerWanderOffset = SteeringCalcs.Steering.rotate(Vector2.right, centerWanderAngle) * centerWanderRadius;
            centerTarget = centerWanderOffset;
        }
        else
        {
            // Far from center, just go to center
            centerTarget = Vector2.zero;
        }
        
        SetSmoothTarget(centerTarget);
        _isHuntingFly = false;
        _lastDecision = "Moving to center (no other actions)";
        SetBehaviorState("Center");
    }

    // Added method to set a target position with smooth transitions
    private void SetSmoothTarget(Vector2 target)
    {
        // Only update if this is a new target that's significantly different
        if (_currentBehaviorState == "None" || Vector2.Distance(_currentTargetPosition, target) > 0.5f)
        {
            _previousTargetPosition = _currentTargetPosition;
            _currentTargetPosition = target;
            _transitionStartTime = Time.time;
            
            // Calculate actual target factoring in smooth transitions
            Vector2 smoothedTarget = GetSmoothedTarget();
            SetTargetPosition(smoothedTarget);
        }
        else
        {
            // Small updates to existing target (like predicted fly position updates)
            _currentTargetPosition = target;
            Vector2 smoothedTarget = GetSmoothedTarget();
            SetTargetPosition(smoothedTarget);
        }
    }

    // Added helper to get smooth transitions between targets
    private Vector2 GetSmoothedTarget()
    {
        float transitionProgress = (Time.time - _transitionStartTime) / _transitionDuration;
        if (transitionProgress < 1.0f)
        {
            // During transition, blend between previous and current target
            return Vector2.Lerp(_previousTargetPosition, _currentTargetPosition, transitionProgress);
        }
        
        // After transition completes, just use current target
        return _currentTargetPosition;
    }

    // Added helper to set and track behavior state
    private void SetBehaviorState(string state)
    {
        if (_currentBehaviorState != state)
        {
            _currentBehaviorState = state;
            
            // We could add state-specific logic here if needed
            // This function mainly helps with tracking the current behavior for debugging
        }
    }

    // Added helper to predict fly position with better accuracy
    private Vector2 PredictFlyPosition(Fly fly)
    {
        if (fly == null) return Vector2.zero;
        
        Vector2 predictedPosition = (Vector2)fly.transform.position;
        Rigidbody2D flyRb = fly.GetComponent<Rigidbody2D>();
        
        if (flyRb != null && flyRb.linearVelocity.magnitude > 0.1f)
        {
            // Calculate intercept time based on relative speeds
            float distanceToFly = Vector2.Distance(transform.position, fly.transform.position);
            float mySpeed = GetTerrainModifiedSpeed();
            float flySpeed = flyRb.linearVelocity.magnitude;
            
            // Predict further ahead for faster flies and when further away
            float predictionTime = Mathf.Clamp(distanceToFly / (mySpeed + 0.1f), 0.1f, 1.0f);
            
            // Check if the predicted position would be out of screen
            Vector2 tentativePosition = predictedPosition + flyRb.linearVelocity * predictionTime;
            Vector2 screenPos = Camera.main.WorldToViewportPoint(tentativePosition);
            bool wouldBeOutOfScreen = screenPos.x < 0 || screenPos.x > 1 || screenPos.y < 0 || screenPos.y > 1;
            
            // If the predicted position would be out of screen, reduce prediction time
            if (wouldBeOutOfScreen)
            {
                // Try different prediction times to find one that stays on screen
                for (float reducedTime = predictionTime * 0.75f; reducedTime >= 0.1f; reducedTime *= 0.75f)
                {
                    Vector2 adjustedPosition = predictedPosition + flyRb.linearVelocity * reducedTime;
                    Vector2 adjustedScreenPos = Camera.main.WorldToViewportPoint(adjustedPosition);
                    
                    if (adjustedScreenPos.x >= 0 && adjustedScreenPos.x <= 1 && 
                        adjustedScreenPos.y >= 0 && adjustedScreenPos.y <= 1)
                    {
                        // Found a prediction time that keeps the fly on screen
                        predictionTime = reducedTime;
                        break;
                    }
                }
                
                // If we couldn't find a good prediction time, just use the current position
                if (wouldBeOutOfScreen)
                {
                    return (Vector2)fly.transform.position;
                }
            }
            
            // Apply prediction
            predictedPosition += flyRb.linearVelocity * predictionTime;
        }
        
        return predictedPosition;
    }

    // Check if the frog is stuck and handle it
    private void CheckIfStuck()
    {
        // Skip stuck detection if disabled
        if (!enableStuckDetection) return;
        
        // Only check every few frames to save performance
        _stuckTimer += Time.fixedDeltaTime;
        
        if (_stuckTimer >= _stuckCheckInterval)
        {
            // Calculate distance moved since last check
            float distanceMoved = Vector2.Distance(_lastPosition, transform.position);
            
            // If the frog hasn't moved much and we have a path
            if (distanceMoved < _stuckDistanceThreshold && _rb.linearVelocity.magnitude < 0.2f && 
                _currentPath != null && _lastClickPos.HasValue)
            {
                _stuckTimer += _stuckCheckInterval; // Increment the timer faster when not moving
                
                // If stuck for too long, try to unstick
                if (_stuckTimer >= _stuckThreshold)
                {
                    Debug.Log("Frog appears to be stuck. Attempting to unstick...");
                    UnstickFrog();
                    _stuckTimer = 0f;
                }
            }
            else
            {
                // Reset the timer if we've moved
                _stuckTimer = 0f;
            }
            
            // Update the last position
            _lastPosition = transform.position;
        }
    }
    
    // Try to unstick the frog
    private void UnstickFrog()
    {
        if (_pathRecalculationAttempts < _maxPathRecalculationAttempts)
        {
            // Increment the counter
            _pathRecalculationAttempts++;
            
            // Try to find an alternative path with more lenient settings
            if (_lastClickPos.HasValue)
            {
                Vector2 targetPos = (Vector2)_lastClickPos;
                
                // Try to find an intermediate target
                Vector2 intermediateTarget = FindIntermediateTargetAwayFromObstacles();
                
                if (intermediateTarget != Vector2.zero)
                {
                    // First path to intermediate point
                    CalculatePathToDestination(intermediateTarget);
                    Debug.Log($"Using intermediate target to unstick: {intermediateTarget}");
                }
                else
                {
                    // Try recalculating the path with different parameters
                    // This will use whatever default pathfinding algorithm is in place
                    CalculatePathToDestination(targetPos);
                    Debug.Log("Recalculating path to original target");
                }
                
                // Apply a small random force to help unstick
                ApplyUnstickingForce();
            }
        }
        else
        {
            // If we've tried too many times, give up on this path
            Debug.Log("Failed to unstick after multiple attempts. Abandoning path.");
            _currentPath = null;
            _lastClickPos = null;
            _pathRecalculationAttempts = 0;
            
            if (HideFlagOnceReached)
            {
                _flagSr.enabled = false;
            }
        }
    }
    
    // Find an intermediate target away from obstacles
    private Vector2 FindIntermediateTargetAwayFromObstacles()
    {
        // Get current obstacle avoidance direction
        Vector2 position = transform.position;
        
        // Sample points in different directions
        float sampleRadius = 2.0f; // Distance to sample
        int numSamples = 8; // Number of directions to sample
        
        for (int i = 0; i < numSamples; i++)
        {
            float angle = (360f / numSamples) * i;
            Vector2 direction = SteeringCalcs.Steering.rotate(Vector2.right, angle);
            Vector2 samplePoint = position + direction * sampleRadius;
            
            // Check if this point is walkable
            Node node = _astarGrid.NodeFromWorldPoint(samplePoint);
            if (node != null && node.walkable)
            {
                // Check if there's no obstacle between us and this point
                RaycastHit2D hit = Physics2D.CircleCast(
                    position,
                    _collider.radius * 0.8f, // Slightly smaller to allow movement
                    direction,
                    sampleRadius,
                    AvoidParams.ObstacleMask
                );
                
                if (!hit)
                {
                    return samplePoint;
                }
            }
        }
        
        return Vector2.zero; // No valid point found
    }
    
    // Apply a small force to help unstick the frog
    private void ApplyUnstickingForce()
    {
        // Apply a small force in a random direction, but prefer forwards
        Vector2 unstickDirection = UnityEngine.Random.insideUnitCircle.normalized;
        
        // Bias towards the current path direction if available
        if (_currentPath != null && _currentPathIndex < _currentPath.Length)
        {
            Vector2 pathDirection = (_currentPath[_currentPathIndex].worldPosition - (Vector2)transform.position).normalized;
            unstickDirection = (unstickDirection + pathDirection * 2).normalized;
        }
        
        // Apply a small impulse force
        float unstickForce = _rb.mass * 5f; // Adjust as needed
        _rb.AddForce(unstickDirection * unstickForce, ForceMode2D.Impulse);
        
        Debug.DrawRay(transform.position, unstickDirection * 2, Color.magenta, 1.0f);
        Debug.Log("Applied unsticking force in direction: " + unstickDirection);
    }

    // Public method to set target position from other components
    public void SetTargetPositionPublic(Vector2 position)
    {
        _lastClickPos = position;
        _flagSr.enabled = true;
        _flag.position = position;
        
        // Set this as a user target
        _isUserTarget = true;
        
        // If using A* movement, calculate path
        if (CurrentMovementType == MovementType.AStar)
        {
            CalculatePathToDestination(position);
        }
    }

    // This method was accidentally removed - restoring it
    private void SetTargetPosition(Vector2 target)
    {
        // Only update target if it's significantly different from current target
        if (_lastClickPos == null || Vector2.Distance(_lastClickPos.Value, target) > 0.5f)
        {
            _lastClickPos = target;
            _flagSr.enabled = true;
            _flag.position = target;
            
            // Update path if using A* movement
            if (CurrentMovementType == MovementType.AStar)
            {
                CalculatePathToDestination(target);
            }
            
            // Reset user target flag when AI sets a target
            _isUserTarget = false;
        }
    }

    // New method: Calculate weighted flee position away from all nearby snakes
    private Vector2 CalculateFleePosition()
    {
        if (nearbySnakes.Count == 0)
        {
            return Vector2.zero; // No snakes to flee from
        }
        
        // Calculate weighted flee direction away from all nearby snakes
        Vector2 fleeDirection = Vector2.zero;
        
        foreach (Snake snake in nearbySnakes)
        {
            float distToSnake = Vector2.Distance(transform.position, snake.transform.position);
            if (distToSnake < scaredRange * 1.2f) // Slightly increased range for better avoidance
            {
                // Calculate direction away from this snake
                Vector2 awayDir = ((Vector2)transform.position - (Vector2)snake.transform.position).normalized;
                
                // Weight by danger (closer snakes have more influence)
                float weight = 1.0f - (distToSnake / scaredRange);
                
                // Additional weighting based on snake state
                if (snake.State == Snake.SnakeState.Attack)
                {
                    weight *= 1.5f; // Attacking snakes are more threatening
                }
                
                // Consider snake velocity - avoid where they're heading
                Rigidbody2D snakeRb = snake.GetComponent<Rigidbody2D>();
                if (snakeRb != null && snakeRb.linearVelocity.magnitude > 0.5f)
                {
                    // Factor in the snake's movement direction
                    Vector2 snakeDirection = snakeRb.linearVelocity.normalized;
                    
                    // If snake is moving toward us, increase the weight
                    float dotProduct = Vector2.Dot(-awayDir, snakeDirection);
                    if (dotProduct > 0) // Snake moving toward frog
                    {
                        weight *= (1f + dotProduct); // Up to double weight if directly approaching
                    }
                }
                
                // Add to total flee direction
                fleeDirection += awayDir * weight;
            }
        }
        
        // Normalize and scale by scared range
        if (fleeDirection.magnitude > 0.01f)
        {
            fleeDirection.Normalize();
            Vector2 fleeTarget = (Vector2)transform.position + fleeDirection * scaredRange;
            
            // Ensure we stay in bounds
            fleeTarget.x = Mathf.Clamp(fleeTarget.x, -AnchorDims.x, AnchorDims.x);
            fleeTarget.y = Mathf.Clamp(fleeTarget.y, -AnchorDims.y, AnchorDims.y);
            
            // Try to avoid difficult terrain when fleeing
            if (_astarGrid != null && IsPointInGrid(fleeTarget))
            {
                Node targetNode = _astarGrid.NodeFromWorldPoint(fleeTarget);
                if (targetNode != null && targetNode.terrainType == TerrainType.Mud)
                {
                    // Find a safer direction by checking alternative angles
                    for (int i = 0; i < 8; i++)
                    {
                        float angle = 45f * i;
                        Vector2 testDir = SteeringCalcs.Steering.rotate(fleeDirection, angle);
                        Vector2 testTarget = (Vector2)transform.position + testDir * scaredRange;
                        
                        testTarget.x = Mathf.Clamp(testTarget.x, -AnchorDims.x, AnchorDims.x);
                        testTarget.y = Mathf.Clamp(testTarget.y, -AnchorDims.y, AnchorDims.y);
                        
                        if (IsPointInGrid(testTarget))
                        {
                            Node testNode = _astarGrid.NodeFromWorldPoint(testTarget);
                            if (testNode != null && testNode.terrainType != TerrainType.Mud)
                            {
                                return testTarget;
                            }
                        }
                    }
                }
            }
            
            return fleeTarget;
        }
        
        // Fallback to center if no clear direction
        return Vector2.zero;
    }
}


