using Globals;
using SteeringCalcs;
using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class Snake : MonoBehaviour
{
    // Obstacle avoidance parameters (see the assignment spec for an explanation).
    public AvoidanceParams AvoidParams;

    // Steering parameters.
    public float MaxSpeed;
    public float MaxAccel;
    public float AccelTime;

    // Use this as the arrival radius for all states where the steering behaviour == arrive.
    public float ArriveRadius;

    // Parameters controlling transitions in/out of the Aggro state.
    public float AggroRange;
    public float DeAggroRange;
    
    // Chase mode controls whether the snake always chases the frog regardless of range
    public bool ChaseMode = false;
    
    // Parameters for fleeing behavior
    public float FleeDistance = 5f;

    // Reference to the frog (the target for the Aggro state).
    public GameObject Frog;

    // The patrol point (the target for the PatrolAway state).
    public Transform PatrolPoint;

    // The snake's initial position (the target for the PatrolHome and Harmless states).
    private Vector2 _home;
    
    // Target position for fleeing (set when entering Fleeing state)
    private Vector2 _fleeTarget;

    // Timer for benign state to enforce minimum duration
    private float _benignTimer = 0f;
    private float _minBenignDuration = 2f;

    // Collision cooldown to prevent rapid multiple collisions
    private float _collisionCooldown = 0f;
    private float _minCollisionCooldown = 0.5f;

    // Attack proximity timer to guarantee a bite after being close enough for a certain time
    private float _attackProximityTimer = 0f;
    private float _maxAttackProximityTime = 0.3f; // Force a bite after 0.3 seconds of being close

    // Stuck detection for fleeing state
    private float _stuckTimer = 0f;
    private float _stuckThreshold = 1.0f; // Time to consider the snake stuck
    private Vector2 _lastPosition;
    private float _stuckDistanceThreshold = 0.1f; // Distance to consider not moving

    // Flag to disable fleeing impulse completely if it causes issues
    private bool _applyFleeExitImpulse = false;

    // Debug rendering config
    private float _debugHomeOffset = 0.3f;

    // References for gameobject controls
    private Rigidbody2D _rb;
    private SpriteRenderer _sr;
    private Animator _animator;
    private CircleCollider2D _collider;

    // Current Snake FSM State
    public SnakeState State;

    // Snake FSM states (to be completed by you)
    public enum SnakeState : int
    {
        PatrolAway = 0,
        PatrolHome = 1,
        Attack = 2,
        Benign = 3,
        Fleeing = 4
    }

    // Snake FSM events (to be completed by you)
    public enum SnakeEvent : int
    {
        FrogInRange = 0,
        FrogOutOfRange = 1,
        BitFrog = 2,
        ReachedTarget = 3,
        HitByBubble = 4,
        NotScared = 5
    }

    // Direction IDs used by the snake animator (please don't edit these).
    private enum Direction : int
    {
        Up = 0,
        Left = 1,
        Down = 2,
        Right = 3
    }

    // Movement speed modifiers for different terrains
    private readonly Dictionary<TerrainType, float> terrainSpeedModifiers = new Dictionary<TerrainType, float>()
    {
        { TerrainType.Normal, 1.0f },
        { TerrainType.Water, 0.8f },    // Snakes are better in water than frogs
        { TerrainType.Sand, 0.9f },     // Snakes are better in sand than frogs
        { TerrainType.Mud, 0.6f }       // Snakes are better in mud than frogs
    };

    // Visual feedback for terrain effects
    public bool showTerrainEffects = true;
    private TextMesh _terrainEffectText;
    private Color _normalColor;
    private SpriteRenderer _spriteRenderer;

    private TerrainType _lastTerrainType = TerrainType.Normal;
    private float _lastTerrainCheckTime = 0f;
    private const float TERRAIN_CHECK_INTERVAL = 0.1f; // Check terrain every 0.1 seconds

    // A* Pathfinding variables
    private Pathfinding _pathfinding;
    private Node[] _currentPath;
    private int _currentPathIndex = 0;
    private float _waypointReachedDistance = 0.5f;
    private float _pathUpdateInterval = 0.5f;
    private float _pathUpdateTimer = 0f;
    private bool _useAStarPathfinding = true; // Controls whether to use A* or direct movement
    private AStarGrid _astarGrid;
    
    // Stuck detection for A* navigation
    private float _astarStuckTimer = 0f;
    private float _astarStuckThreshold = 1.5f;
    private int _pathRecalculationAttempts = 0;
    private int _maxPathRecalculationAttempts = 3;
    // Add corner handling variables
    private bool _isInCorner = false;
    private float _cornerDetectionRadius = 1.0f;
    private float _cornerEscapeTimer = 0f;
    private float _cornerEscapeThreshold = 0.8f;
    private Vector2 _cornerEscapeDirection = Vector2.zero;

    // Add enum for control mode
    public enum ControlMode : int
    {
        FSM = 0,
        BehaviorTree = 1
    }

    // Add control mode property
    public ControlMode controlMode = ControlMode.FSM;
    
    // Reference to behavior tree component
    private MonoBehaviour _behaviorTree;

    void Start()
    {
        _rb = GetComponent<Rigidbody2D>();
        _sr = GetComponent<SpriteRenderer>();
        _animator = GetComponent<Animator>();
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _normalColor = _spriteRenderer.color;

        _home = transform.position;

        // Set initial state to PatrolAway
        State = SnakeState.PatrolAway;
        
        // Initialize last position for stuck detection
        _lastPosition = transform.position;
        
        // Get or add a circle collider that is NOT a trigger
        _collider = GetComponent<CircleCollider2D>();
        if (_collider != null)
        {
            _collider.isTrigger = false;
        }
        else
        {
            _collider = gameObject.AddComponent<CircleCollider2D>();
            _collider.isTrigger = false;
            _collider.radius = 0.4f;
        }

        // If Frog reference is not set, find it by tag
        if (Frog == null)
        {
            Frog = GameObject.FindWithTag("Player");
            if (Frog != null)
            {
                Debug.Log($"Snake {name} found Frog reference automatically");
            }
            else
            {
                Debug.LogWarning($"Snake {name} could not find the Frog! The 'Player' tag might be missing on the Frog GameObject.");
            }
        }

        // Set up terrain effect visualization
        if (showTerrainEffects)
        {
            GameObject textObj = new GameObject("TerrainEffectText");
            textObj.transform.parent = transform;
            textObj.transform.localPosition = new Vector3(0, 0.8f, 0); // Lowered from 1.5f to 0.8f
            _terrainEffectText = textObj.AddComponent<TextMesh>();
            _terrainEffectText.alignment = TextAlignment.Center;
            _terrainEffectText.anchor = TextAnchor.LowerCenter;
            _terrainEffectText.characterSize = 0.15f;
            _terrainEffectText.fontSize = 36;
            _terrainEffectText.fontStyle = FontStyle.Bold;
            
            // Initialize the text indicator
            UpdateTextIndicator();
        }
        
        // Configure physics interaction
        SetupSnakeCollisionIgnore();
        
        // Initialize A* pathfinding components
        _pathfinding = FindFirstObjectByType<Pathfinding>();
        if (_pathfinding == null)
        {
            Debug.LogError("Pathfinding component not found in scene!");
            _useAStarPathfinding = false;
        }
        
        _astarGrid = FindFirstObjectByType<AStarGrid>();
        if (_astarGrid == null)
        {
            Debug.LogError("AStarGrid component not found in scene!");
            _useAStarPathfinding = false;
        }
        
        // If using A* pathfinding, calculate initial path based on state
        if (_useAStarPathfinding)
        {
            CalculatePathToDestination(GetTargetPosition());
        }

        // Get or add behavior tree component
        _behaviorTree = GetComponent("SnakeBehaviorTree") as MonoBehaviour;
        if (_behaviorTree == null)
        {
            // Add the component by type name
            _behaviorTree = gameObject.AddComponent(System.Type.GetType("SnakeBehaviorTree")) as MonoBehaviour;
            if (_behaviorTree != null)
            {
                _behaviorTree.enabled = false;
            }
        }
        
        // Set initial behavior tree state
        UpdateControlMode();
    }

    void Update()
    {
        // Only run Update logic when using FSM mode
        if (controlMode == ControlMode.FSM)
        {
            // Update terrain effect visualization
            if (showTerrainEffects)
            {
                UpdateTerrainEffectVisual();
                
                // Also ensure the BT text is hidden when in FSM mode
                TryHideBehaviorTreeText();
            }
            else if (_terrainEffectText != null)
            {
                // Make sure our text is hidden if terrain effects are disabled
                _terrainEffectText.gameObject.SetActive(false);
            }

            // Note: M key handling is now managed by DrawGUI.cs to toggle all snakes at once
        }
    }

    private void UpdateTerrainEffectVisual()
    {
        TerrainType currentTerrain = GetCurrentTerrain();
        
        // Update terrain text using our common method
        UpdateTextIndicator();
        
        // Set text color based on terrain type for better visibility
        if (_terrainEffectText != null)
        {
            // Set text color based on terrain with higher contrast
            switch (currentTerrain)
            {
                case TerrainType.Normal:
                    _terrainEffectText.color = new Color(1f, 1f, 1f, 1f); // Bright white
                    break;
                case TerrainType.Water:
                    _terrainEffectText.color = new Color(0f, 1f, 1f, 1f); // Bright cyan
                    break;
                case TerrainType.Sand:
                    _terrainEffectText.color = new Color(1f, 0.9f, 0f, 1f); // Bright yellow
                    break;
                case TerrainType.Mud:
                    _terrainEffectText.color = new Color(0.8f, 0.4f, 0f, 1f); // Bright brown
                    break;
            }

            // Make text always face up regardless of snake rotation
            _terrainEffectText.transform.up = Vector3.up;
        }
    }

    private TerrainType GetCurrentTerrain()
    {
        // Check terrain more frequently
        if (Time.time >= _lastTerrainCheckTime + TERRAIN_CHECK_INTERVAL)
        {
            _lastTerrainCheckTime = Time.time;
            if (Pathfinding.grid != null)
            {
                TerrainType newTerrain = Pathfinding.grid.GetTerrainTypeAt(transform.position);
                if (newTerrain != _lastTerrainType)
                {
                    Debug.Log($"Snake terrain changed from {_lastTerrainType} to {newTerrain} at position {transform.position}");
                    _lastTerrainType = newTerrain;
                }
                return newTerrain;
            }
        }
        return _lastTerrainType; // Return last known terrain if not time to check again
    }

    // Get the modified speed based on current terrain
    private float GetTerrainModifiedSpeed()
    {
        TerrainType currentTerrain = GetCurrentTerrain();
        float speedModifier = terrainSpeedModifiers[currentTerrain];
        return MaxSpeed * speedModifier;
    }

    // Set up snake collision ignore without using custom layers
    private void SetupSnakeCollisionIgnore()
    {
        // Find all snake objects in the scene using the newer method
        Snake[] allSnakes = FindObjectsByType<Snake>(FindObjectsSortMode.None);
        
        // Make each snake ignore collisions with all other snakes
        foreach (Snake otherSnake in allSnakes)
        {
            if (otherSnake != this && otherSnake.gameObject != gameObject)
            {
                // Ignore collision between this snake and the other snake
                if (otherSnake.GetComponent<Collider2D>() != null && _collider != null)
                {
                    Physics2D.IgnoreCollision(_collider, otherSnake.GetComponent<Collider2D>(), true);
                }
            }
        }
    }

    // Our common FSM approach has been setup for you.
    // This is an event-first FSM, where events can be triggered by FixedUpdateEvents().
    // Then FSM_State() processes the current FSM state.
    // UpdateAppearance() is called at the end to update the snake's appearance.
    void FixedUpdate()
    {
        // Only run FixedUpdate logic when using FSM mode
        if (controlMode == ControlMode.FSM)
        {
            // Update cooldown timer for collision detection
            if (_collisionCooldown > 0)
            {
                _collisionCooldown -= Time.fixedDeltaTime;
            }
            
            // Check for environmental obstacles that might cause sticking
            CheckForEnvironmentalObstacles();
            
            // Check if snake is stuck when fleeing
            if (State == SnakeState.Fleeing)
            {
                // Calculate distance moved since last frame
                float distanceMoved = Vector2.Distance(_lastPosition, transform.position);
                
                // If barely moving, increment stuck timer
                if (distanceMoved < _stuckDistanceThreshold && _rb.linearVelocity.magnitude < MaxSpeed * 0.2f)
                {
                    _stuckTimer += Time.fixedDeltaTime;
                    
                    // If stuck for too long, recalculate flee target
                    if (_stuckTimer > _stuckThreshold)
                    {
                        // Recalculate flee target to unstick
                        _fleeTarget = CalculateFleeTarget();
                        
                        // If using A* pathfinding, calculate a new path
                        if (_useAStarPathfinding)
                        {
                            CalculatePathToDestination(_fleeTarget);
                        }
                        
                        // Apply a random force to help unstick
                        _rb.AddForce(Random.insideUnitCircle.normalized * MaxAccel * 2f, ForceMode2D.Impulse);
                        
                        // Reset stuck timer
                        _stuckTimer = 0f;
                    }
                }
                else
                {
                    // Reset stuck timer if moving normally
                    _stuckTimer = 0f;
                }
            }
            else
            {
                // Reset stuck timer for other states
                _stuckTimer = 0f;
            }
            
            // Update A* path stuck detection
            if (_useAStarPathfinding && _currentPath != null)
            {
                CheckIfStuckOnPath();
            }
            
            // Update path periodically if using A* pathfinding
            if (_useAStarPathfinding)
            {
                _pathUpdateTimer -= Time.fixedDeltaTime;
                if (_pathUpdateTimer <= 0)
                {
                    // Adjust path update frequency based on state and chase mode
                    if (State == SnakeState.Attack)
                    {
                        // Use more frequent updates when in attack state, especially in chase mode
                        _pathUpdateInterval = ChaseMode ? 0.2f : 0.4f;
                    }
                    else
                    {
                        // Standard update interval for other states
                        _pathUpdateInterval = 0.5f;
                    }
                    
                    _pathUpdateTimer = _pathUpdateInterval;
                    
                    // Recalculate path to current target
                    Vector2 targetPos = GetTargetPosition();
                    
                    // When in attack state, always use the current frog position rather than cached position
                    if (State == SnakeState.Attack && Frog != null)
                    {
                        targetPos = Frog.transform.position;
                    }
                    
                    CalculatePathToDestination(targetPos);
                }
            }
            
            // Update last position for next frame's stuck detection
            _lastPosition = transform.position;

            // Events triggered by each fixed update tick
            FixedUpdateEvents();

            // Update the Snake behaviour based on the current FSM state
            FSM_State();

            // Configure final appearance of the snake
            UpdateAppearance();
        }
    }

    // Trigger Events for each fixed update tick, using a trigger first FSM implementation
    void FixedUpdateEvents()
    {
        // Update benign timer if in Benign state
        if (State == SnakeState.Benign)
        {
            _benignTimer += Time.fixedDeltaTime;
        }
        
        // Check if the frog is in range for attack
        if (Frog != null)
        {
            float distanceToFrog = Vector2.Distance(transform.position, Frog.transform.position);
            
            // Check if frog is in attack range - only if not in Benign state or Fleeing state
            // In chase mode, the snake will always see the frog as in range unless fleeing
            if ((ChaseMode || distanceToFrog <= AggroRange) && State != SnakeState.Benign && State != SnakeState.Fleeing)
            {
                HandleEvent(SnakeEvent.FrogInRange);
            }
            
            // Check if frog is out of attack range - in chase mode, the snake never loses track of the frog
            if (!ChaseMode && distanceToFrog > DeAggroRange && State == SnakeState.Attack)
            {
                HandleEvent(SnakeEvent.FrogOutOfRange);
            }
        }
        
        // Check if the snake has reached its target
        Vector2 targetPosition = GetTargetPosition();
        float distanceToTarget = Vector2.Distance(transform.position, targetPosition);
        
        if (distanceToTarget <= Constants.TARGET_REACHED_TOLERANCE)
        {
            // If in Fleeing state and reached flee target, trigger NotScared event
            if (State == SnakeState.Fleeing)
            {
                HandleEvent(SnakeEvent.NotScared);
            }
            else if (State == SnakeState.Benign)
            {
                // Only transition from Benign state if minimum time has passed
                if (_benignTimer >= _minBenignDuration)
                {
                    HandleEvent(SnakeEvent.ReachedTarget);
                }
            }
            else
            {
                HandleEvent(SnakeEvent.ReachedTarget);
            }
        }
    }
    
    // Called when a bubble collides with the snake
    public void OnBubbleHit()
    {
        if (controlMode == ControlMode.FSM)
        {
            // Existing FSM implementation
            HandleEvent(SnakeEvent.HitByBubble);
        }
        else if (controlMode == ControlMode.BehaviorTree && _behaviorTree != null)
        {
            // Direct call to the behavior tree component's OnBubbleHit method
            // Use GetComponent directly to avoid recursive SendMessage
            SnakeBehaviorTree behaviorTreeComponent = _behaviorTree as SnakeBehaviorTree;
            if (behaviorTreeComponent != null)
            {
                // Call the method directly instead of using SendMessage
                behaviorTreeComponent.OnBubbleHit();
            }
            else
            {
                Debug.LogError($"Snake {name}: Failed to cast _behaviorTree to SnakeBehaviorTree component");
            }
        }
    }

    // Helper method to get the current target position based on state
    private Vector2 GetTargetPosition()
    {
        switch (State)
        {
            case SnakeState.PatrolAway:
                return PatrolPoint.position;
            case SnakeState.PatrolHome:
            case SnakeState.Benign:
                return _home;
            case SnakeState.Attack:
                return Frog ? (Vector2)Frog.transform.position : _home;
            case SnakeState.Fleeing:
                return _fleeTarget;
            default:
                return _home;
        }
    }
    
    // Calculate a flee target position away from the frog
    private Vector2 CalculateFleeTarget()
    {
        // Define the boundary limit once
        float boundaryLimit = 15f; // Adjust this value based on your game size
        
        if (Frog != null)
        {
            // Get direction away from frog
            Vector2 fleeDirection = ((Vector2)transform.position - (Vector2)Frog.transform.position).normalized;
            
            // Cast several rays to check for obstacles
            float rayDistance = FleeDistance * 1.5f;
            bool obstacleFound = false;
            Vector2 clearDirection = fleeDirection;
            float bestDistance = 0f;
            
            // First check directly away from frog
            RaycastHit2D hit = Physics2D.Raycast(transform.position, fleeDirection, rayDistance, AvoidParams.ObstacleMask);
            
            // Debug visualization
            Debug.DrawRay(transform.position, fleeDirection * rayDistance, hit ? Color.red : Color.green, 0.5f);
            
            if (!hit)
            {
                // Direct path is clear
                clearDirection = fleeDirection;
                obstacleFound = false;
            }
            else 
            {
                // Direct path has obstacle, record best distance
                bestDistance = hit.distance;
                obstacleFound = true;
                
                // Check multiple directions to find a clear path
                for (int i = 0; i < 16; i++)
                {
                    // Try different angles in all directions
                    float angle = i * 22.5f; // 16 directions = 22.5 degrees each
                    Vector2 testDirection = Steering.rotate(fleeDirection, angle).normalized;
                    
                    hit = Physics2D.Raycast(transform.position, testDirection, rayDistance, AvoidParams.ObstacleMask);
                    
                    // Debug visualization
                    Debug.DrawRay(transform.position, testDirection * rayDistance, hit ? Color.red : Color.green, 0.5f);
                    
                    // If no obstacle in this direction
                    if (!hit)
                    {
                        clearDirection = testDirection;
                        obstacleFound = false;
                        break;
                    }
                    
                    // If this direction has more clearance than previous best
                    if (hit.distance > bestDistance)
                    {
                        bestDistance = hit.distance;
                        clearDirection = testDirection;
                    }
                    
                    // Try opposite direction
                    testDirection = Steering.rotate(fleeDirection, -angle).normalized;
                    hit = Physics2D.Raycast(transform.position, testDirection, rayDistance, AvoidParams.ObstacleMask);
                    
                    // Debug visualization
                    Debug.DrawRay(transform.position, testDirection * rayDistance, hit ? Color.red : Color.yellow, 0.5f);
                    
                    // If no obstacle in this direction
                    if (!hit)
                    {
                        clearDirection = testDirection;
                        obstacleFound = false;
                        break;
                    }
                    
                    // If this direction has more clearance than previous best
                    if (hit.distance > bestDistance)
                    {
                        bestDistance = hit.distance;
                        clearDirection = testDirection;
                    }
                }
                
                // If all directions have obstacles but we found best one with some distance
                if (obstacleFound && bestDistance > FleeDistance * 0.5f)
                {
                    // Use the direction with the most clearance, but at a shorter distance
                    float safeDistance = bestDistance * 0.7f; // Give some margin to avoid getting too close to obstacle
                    Vector2 fleeTarget = (Vector2)transform.position + clearDirection * safeDistance;
                    
                    // Debug visualization
                    Debug.DrawLine(transform.position, fleeTarget, Color.blue, 0.5f);
                    
                    // Constrain the flee target to be within a reasonable boundary
                    fleeTarget.x = Mathf.Clamp(fleeTarget.x, -boundaryLimit, boundaryLimit);
                    fleeTarget.y = Mathf.Clamp(fleeTarget.y, -boundaryLimit, boundaryLimit);
                    
                    return fleeTarget;
                }
            }
            
            // If we can't find a good direction or have a clear path, use the best direction we found
            // Set flee target at a good distance away
            float targetDistance = obstacleFound ? Mathf.Min(FleeDistance, bestDistance * 0.7f) : FleeDistance;
            Vector2 fleeTargetPosition = (Vector2)transform.position + clearDirection * targetDistance;
            
            // Debug visualization for final target
            Debug.DrawLine(transform.position, fleeTargetPosition, Color.magenta, 0.5f);
            
            // Constrain the flee target to be within a reasonable boundary
            fleeTargetPosition.x = Mathf.Clamp(fleeTargetPosition.x, -boundaryLimit, boundaryLimit);
            fleeTargetPosition.y = Mathf.Clamp(fleeTargetPosition.y, -boundaryLimit, boundaryLimit);
            
            return fleeTargetPosition;
        }
        
        // If no frog, just flee in a random direction (within bounds) that has no obstacles
        Vector2 randomDirection = Random.insideUnitCircle.normalized;
        RaycastHit2D randomHit = Physics2D.Raycast(transform.position, randomDirection, FleeDistance, AvoidParams.ObstacleMask);
        
        // Try to find a direction without obstacles
        for (int i = 0; i < 8; i++)
        {
            if (!randomHit)
            {
                // Found a clear direction
                break;
            }
            // Try another random direction
            randomDirection = Random.insideUnitCircle.normalized;
            randomHit = Physics2D.Raycast(transform.position, randomDirection, FleeDistance, AvoidParams.ObstacleMask);
        }
        
        Vector2 randomTarget = (Vector2)transform.position + randomDirection * (randomHit ? randomHit.distance * 0.7f : FleeDistance);
        
        // Debug visualization
        Debug.DrawLine(transform.position, randomTarget, Color.cyan, 0.5f);
        
        // Constrain random target
        randomTarget.x = Mathf.Clamp(randomTarget.x, -boundaryLimit, boundaryLimit);
        randomTarget.y = Mathf.Clamp(randomTarget.y, -boundaryLimit, boundaryLimit);
        
        return randomTarget;
    }

    // Process the current FSM state, using an event first FSM implementation
    void FSM_State()
    {
        Vector2 desiredVel = Vector2.zero;
        float currentMaxSpeed = GetTerrainModifiedSpeed(); // Use terrain-modified speed
        
        // Apply speed boost when in chase mode and attacking
        if (ChaseMode && State == SnakeState.Attack)
        {
            // Increase speed by 20% in chase mode for more aggressive pursuit
            currentMaxSpeed *= 1.2f;
            
            // Also reduce the waypoint reached distance when in chase mode for more precise following
            _waypointReachedDistance = 0.3f;
        }
        else
        {
            // Use standard waypoint reached distance otherwise
            _waypointReachedDistance = 0.5f;
        }
        
        Vector2 targetPosition = GetTargetPosition();

        // If using A* pathfinding and we have a valid path
        if (_useAStarPathfinding && _currentPath != null && _currentPath.Length > 0 && _currentPathIndex < _currentPath.Length)
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
                    _pathRecalculationAttempts = 0;
                    
                    // If we're in chase mode and attack state, don't clear the path completely
                    // Instead, force an immediate recalculation of the path to the frog
                    if (ChaseMode && State == SnakeState.Attack && Frog != null)
                    {
                        _pathUpdateTimer = 0.01f; // Force almost immediate update
                    }
                    
                    // If we've reached the target, trigger the appropriate event
                    float distanceToTarget = Vector2.Distance(transform.position, targetPosition);
                    if (distanceToTarget <= Constants.TARGET_REACHED_TOLERANCE)
                    {
                        // Trigger reached target event directly
                        if (State == SnakeState.Fleeing)
                        {
                            HandleEvent(SnakeEvent.NotScared);
                        }
                        else if (State == SnakeState.Benign)
                        {
                            if (_benignTimer >= _minBenignDuration)
                            {
                                HandleEvent(SnakeEvent.ReachedTarget);
                            }
                        }
                        else
                        {
                            HandleEvent(SnakeEvent.ReachedTarget);
                        }
                    }
                    
                    return;
                }
                
                // Update current waypoint
                currentWaypoint = _currentPath[_currentPathIndex].worldPosition;
            }
            
            // In corners, adjust the path following behavior
            bool isInCorner = _astarGrid != null && _astarGrid.IsInCorner(transform.position);
            
            // Calculate desired velocity towards waypoint
            Vector2 toWaypoint = (currentWaypoint - (Vector2)transform.position).normalized;
            
            // If in a corner, look further ahead in the path if possible
            if (isInCorner && _currentPathIndex < _currentPath.Length - 2)
            {
                // Look two waypoints ahead to help navigate through the corner
                Vector2 futureWaypoint = _currentPath[_currentPathIndex + 2].worldPosition;
                Vector2 toFutureWaypoint = (futureWaypoint - (Vector2)transform.position).normalized;
                
                // Blend the directions (70% current waypoint, 30% future waypoint)
                toWaypoint = (toWaypoint * 0.7f + toFutureWaypoint * 0.3f).normalized;
            }
            
            desiredVel = toWaypoint * currentMaxSpeed;
            
            // In chase mode, look ahead more waypoints to smooth movement
            if (ChaseMode && State == SnakeState.Attack && _currentPathIndex < _currentPath.Length - 1)
            {
                // Blend in some influence from the next waypoint to smooth movement
                Vector2 nextWaypoint = _currentPath[_currentPathIndex + 1].worldPosition;
                Vector2 toNextWaypoint = (nextWaypoint - (Vector2)transform.position).normalized;
                desiredVel = Vector2.Lerp(desiredVel, toNextWaypoint * currentMaxSpeed, 0.3f);
            }
        }
        else
        {
            // If no valid A* path or not using A*, fall back to direct movement
            // Implement steering behavior based on current state
            switch (State)
            {
                case SnakeState.PatrolAway:
                    // Move towards patrol point using arrive steering
                    desiredVel = Steering.Arrive(transform.position, PatrolPoint.position, ArriveRadius, currentMaxSpeed, AvoidParams);
                    break;
                    
                case SnakeState.PatrolHome:
                    // Move towards home position using arrive steering
                    desiredVel = Steering.Arrive(transform.position, _home, ArriveRadius, currentMaxSpeed, AvoidParams);
                    break;
                    
                case SnakeState.Attack:
                    // Chase the frog using seek steering with higher speed for more decisive attacks
                    desiredVel = Steering.Seek(transform.position, Frog.transform.position, currentMaxSpeed, AvoidParams);
                    break;
                    
                case SnakeState.Benign:
                    // Move towards home position using arrive steering
                    desiredVel = Steering.Arrive(transform.position, _home, ArriveRadius, currentMaxSpeed, AvoidParams);
                    break;
                    
                case SnakeState.Fleeing:
                    // Enhanced arrive steering for fleeing with improved obstacle avoidance
                    AvoidanceParams enhancedAvoidParams = new AvoidanceParams
                    {
                        Enable = AvoidParams.Enable,
                        ObstacleMask = AvoidParams.ObstacleMask,
                        MaxCastLength = AvoidParams.MaxCastLength * 1.5f,
                        CircleCastRadius = AvoidParams.CircleCastRadius * 1.5f,
                        AngleIncrement = AvoidParams.AngleIncrement
                    };
                    
                    desiredVel = Steering.Arrive(transform.position, _fleeTarget, ArriveRadius, currentMaxSpeed, enhancedAvoidParams);
                    
                    // Apply additional obstacle avoidance directly
                    Vector2 avoidance = Vector2.zero;
                    
                    // Cast rays in multiple directions to detect obstacles
                    for (int i = 0; i < 8; i++)
                    {
                        float angle = i * 45f * Mathf.Deg2Rad;
                        Vector2 rayDir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)).normalized;
                        
                        RaycastHit2D hit = Physics2D.Raycast(transform.position, rayDir, AvoidParams.MaxCastLength * 1.5f);
                        if (hit.collider != null && !hit.collider.CompareTag("Snake") && !hit.collider.CompareTag("Frog"))
                        {
                            // Calculate avoidance force based on proximity (closer = stronger force)
                            float strength = 1.0f - (hit.distance / (AvoidParams.MaxCastLength * 1.5f));
                            avoidance += -rayDir * strength * MaxAccel * 2f;
                        }
                    }
                    
                    // Blend avoidance with desired velocity
                    if (avoidance.magnitude > 0.1f)
                    {
                        desiredVel += avoidance;
                        // Maintain speed after adding avoidance
                        if (desiredVel.magnitude > currentMaxSpeed)
                        {
                            desiredVel = desiredVel.normalized * currentMaxSpeed;
                        }
                    }
                    break;
            }
        }
        
        // Special case for Attack state - check for frog proximity
        if (State == SnakeState.Attack && _collisionCooldown <= 0 && Frog != null)
        {
            float distanceToFrog = Vector2.Distance(transform.position, Frog.transform.position);
            
            // Increase bite detection radius to handle more edge cases
            float biteRadius = 0.7f;  // Larger radius to ensure bite happens
            
            // If very close to frog, increment the proximity timer
            if (distanceToFrog < biteRadius)
            {
                _attackProximityTimer += Time.fixedDeltaTime;
                
                // Force a bite after being close for a certain time period
                if (_attackProximityTimer >= _maxAttackProximityTime)
                {
                    // Reset timer and force bite
                    _attackProximityTimer = 0f;
                    ProcessCollision(Frog);
                }
            }
            else
            {
                // Reset timer if not close enough
                _attackProximityTimer = 0f;
            }
            
            // Try to bite immediately if very close (traditional method)
            if (distanceToFrog < biteRadius * 0.7f)
            {
                // This ensures bite can happen even when both objects are nearly stationary
                ProcessCollision(Frog);
            }
        }

        // Convert the desired velocity to a force, then apply it.
        Vector2 steering = Steering.DesiredVelToForce(desiredVel, _rb, AccelTime, MaxAccel);
        _rb.AddForce(steering);
        
        // If we're in a corner, reduce max velocity to help with navigation
        if (_astarGrid != null && _astarGrid.IsInCorner(transform.position) && _rb.linearVelocity.magnitude > currentMaxSpeed * 0.7f)
        {
            _rb.linearVelocity = _rb.linearVelocity.normalized * (currentMaxSpeed * 0.7f);
        }
    }

    private void SetState(SnakeState newState)
    {
        if (newState != State)
        {
            // Can uncomment this for debugging purposes.
            Debug.Log(name + " switching state to " + newState.ToString());
            
            // Special handling when entering Fleeing state
            if (newState == SnakeState.Fleeing)
            {
                // Calculate and set the flee target
                _fleeTarget = CalculateFleeTarget();
                
                // Calculate A* path to flee target if using A* pathfinding
                if (_useAStarPathfinding)
                {
                    CalculatePathToDestination(_fleeTarget);
                }
                
                // Reset stuck detection timers
                _stuckTimer = 0f;
                _lastPosition = transform.position;
            }
            else if (_useAStarPathfinding)
            {
                // For other states, calculate A* path to the appropriate target
                switch (newState)
                {
                    case SnakeState.PatrolAway:
                        CalculatePathToDestination(PatrolPoint.position);
                        break;
                    case SnakeState.PatrolHome:
                    case SnakeState.Benign:
                        CalculatePathToDestination(_home);
                        break;
                    case SnakeState.Attack:
                        if (Frog != null)
                        {
                            CalculatePathToDestination(Frog.transform.position);
                        }
                        break;
                }
            }
            
            // Reset benign timer when entering Benign state
            if (newState == SnakeState.Benign)
            {
                _benignTimer = 0f;
            }
            
            // Reset attack proximity timer when entering Attack state
            if (newState == SnakeState.Attack)
            {
                _attackProximityTimer = 0f;
            }

            State = newState;
        }
    }

    private void HandleEvent(SnakeEvent e)
    {
        // Implement FSM transitions based on events
        switch (State)
        {
            case SnakeState.PatrolAway:
                if (e == SnakeEvent.ReachedTarget)
                {
                    SetState(SnakeState.PatrolHome);
                }
                else if (e == SnakeEvent.FrogInRange)
                {
                    SetState(SnakeState.Attack);
                }
                else if (e == SnakeEvent.HitByBubble)
                {
                    SetState(SnakeState.Fleeing);
                }
                break;
                
            case SnakeState.PatrolHome:
                if (e == SnakeEvent.ReachedTarget)
                {
                    SetState(SnakeState.PatrolAway);
                }
                else if (e == SnakeEvent.FrogInRange)
            {
                SetState(SnakeState.Attack);
            }
                else if (e == SnakeEvent.HitByBubble)
                {
                    SetState(SnakeState.Fleeing);
                }
                break;
                
            case SnakeState.Attack:
                if (e == SnakeEvent.FrogOutOfRange)
            {
                SetState(SnakeState.PatrolHome);
            }
                else if (e == SnakeEvent.BitFrog)
                {
                    SetState(SnakeState.Benign);
                }
                else if (e == SnakeEvent.HitByBubble)
                {
                    SetState(SnakeState.Fleeing);
                }
                break;
                
            case SnakeState.Benign:
                // Only process ReachedTarget event if minimum time has passed
                if (e == SnakeEvent.ReachedTarget && _benignTimer >= _minBenignDuration)
            {
                SetState(SnakeState.PatrolAway);
            }
                else if (e == SnakeEvent.HitByBubble)
                {
                    SetState(SnakeState.Fleeing);
                }
                break;
                
            case SnakeState.Fleeing:
                if (e == SnakeEvent.NotScared)
                {
                    // When coming out of fleeing state, reset physics properties
                    // and ensure we have a clear path back
                    ResetForFleeConcluded();
                    SetState(SnakeState.PatrolHome);
                }
                break;
        }
    }
    
    // New helper method to reset physics state and ensure we don't get stuck when fleeing is done
    private void ResetForFleeConcluded()
    {
        // Stop the snake's current motion more gradually
        _rb.linearVelocity = _rb.linearVelocity * 0.5f;  // Reduce velocity rather than zeroing it
        
        // Reset any accumulated angular velocity
        _rb.angularVelocity = 0f;
        
        // Only apply an impulse if the flag is enabled
        if (_applyFleeExitImpulse)
        {
            // Apply a gentler impulse toward home to prevent the "dash" effect
            Vector2 directionToHome = (_home - (Vector2)transform.position).normalized;
            
            // Use a much smaller impulse force - just enough to adjust direction
            _rb.AddForce(directionToHome * MaxAccel * 0.1f, ForceMode2D.Impulse);
        }
        
        // Debug visual - draw path home
        Debug.DrawLine(transform.position, _home, Color.white, 1.0f);
    }
    
    // Handle collision with the frog using both methods
    private void OnTriggerEnter2D(Collider2D collision)
    {
        ProcessCollision(collision.gameObject);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        ProcessCollision(collision.gameObject);
    }

    // For the case when colliders overlap but no collision is detected
    private void OnCollisionStay2D(Collision2D collision)
    {
        // This helps with cases where the frog and snake are touching but no collision event fired
        if (State == SnakeState.Attack && _collisionCooldown <= 0)
        {
            ProcessCollision(collision.gameObject);
        }
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        // This helps with cases where the frog and snake are touching but no collision event fired
        if (State == SnakeState.Attack && _collisionCooldown <= 0)
        {
            ProcessCollision(collision.gameObject);
        }
    }

    private void ProcessCollision(GameObject collisionObject)
    {
        // Check if the collided object is the frog by using the reference and/or component check
        bool isFrog = (Frog != null && collisionObject == Frog) || collisionObject.GetComponent<Frog>() != null;
        
        // Only process if cooldown timer is finished
        if (isFrog && State == SnakeState.Attack && _collisionCooldown <= 0)
        {
            // Set collision cooldown
            _collisionCooldown = _minCollisionCooldown;
            
            // Debug collision detection
            Debug.Log("Snake bit the frog! Transitioning to Benign state.");
            
            // Trigger the BitFrog event to change the snake's state
            HandleEvent(SnakeEvent.BitFrog);
            
            // Notify the frog that it was bitten - ONLY the snake reduces health
            Frog frog = collisionObject.GetComponent<Frog>();
            if (frog != null)
            {
                // Damage the frog
                frog.Health--;
                
                Debug.Log("Snake reduced frog's health to: " + frog.Health);
                
                // Check if game over
                if (frog.Health <= 0)
                {
                    Debug.Log("Frog has no health left - showing game over!");
                    frog.ShowGameOver(false);
                }
            }
        }
    }
    
    // Helper method to update the snake's appearance based on state and terrain
    private void UpdateAppearance()
    {
        // First set the base color based on state
        Color stateColor = Color.white;
        switch (State)
        {
            case SnakeState.PatrolAway:
                stateColor = new Color(0.3f, 0.3f, 0.3f);
                break;
            case SnakeState.PatrolHome:
                stateColor = new Color(1.0f, 1.0f, 1.0f);
                break;
            case SnakeState.Attack:
                stateColor = new Color(1.0f, 0.2f, 0.2f);
                break;
            case SnakeState.Benign:
                stateColor = new Color(0.2f, 0.94f, 0.23f);
                break;
            case SnakeState.Fleeing:
                stateColor = new Color(0.45f, 0.98f, 0.94f);
                break;
        }

        // Apply chase mode tint if enabled (make the snake more intense/vibrant)
        if (ChaseMode && State != SnakeState.Fleeing)
        {
            // Make the colors more saturated and add a purple tint for chase mode
            stateColor = new Color(
                Mathf.Clamp01(stateColor.r * 1.2f),
                Mathf.Clamp01(stateColor.g * 0.8f),
                Mathf.Clamp01(stateColor.b * 1.5f),
                1.0f
            );
            
            // Update the text indicator for chase mode
            UpdateTextIndicator();
        }
        else if (_terrainEffectText != null && _terrainEffectText.text.Contains("[CHASE]"))
        {
            // Remove chase mode indicator if disabled
            UpdateTextIndicator();
        }

        // Then apply terrain tint as a secondary effect
        if (showTerrainEffects && _spriteRenderer != null)
        {
            TerrainType currentTerrain = GetCurrentTerrain();
            Color terrainTint = stateColor;
            switch (currentTerrain)
            {
                case TerrainType.Water:
                    terrainTint *= new Color(0.9f, 0.9f, 1f); // Subtle blue tint
                    break;
                case TerrainType.Sand:
                    terrainTint *= new Color(1f, 1f, 0.9f); // Subtle yellow tint
                    break;
                case TerrainType.Mud:
                    terrainTint *= new Color(0.9f, 0.8f, 0.7f); // Subtle brown tint
                    break;
            }
            _spriteRenderer.color = Color.Lerp(_spriteRenderer.color, terrainTint, Time.deltaTime * 10f);
        }
        else if (_spriteRenderer != null)
        {
            _spriteRenderer.color = Color.Lerp(_spriteRenderer.color, stateColor, Time.deltaTime * 10f);
        }

        // Update the Snake visual based on the direction it's moving
        // (please don't modify this block)
        if (_rb.linearVelocity.magnitude > Constants.MIN_SPEED_TO_ANIMATE)
        {
            // Determine the bearing of the snake in degrees (between -180 and 180)
            float angle = Mathf.Atan2(_rb.linearVelocity.y, _rb.linearVelocity.x) * Mathf.Rad2Deg;

            if (angle > -135.0f && angle <= -45.0f) // Down
            {
                transform.up = new Vector2(0.0f, -1.0f);
                _animator.SetInteger("Direction", (int)Direction.Down);
            }
            else if (angle > -45.0f && angle <= 45.0f) // Right
            {
                transform.up = new Vector2(1.0f, 0.0f);
                _animator.SetInteger("Direction", (int)Direction.Right);
            }
            else if (angle > 45.0f && angle <= 135.0f) // Up
            {
                transform.up = new Vector2(0.0f, 1.0f);
                _animator.SetInteger("Direction", (int)Direction.Up);
            }
            else // Left
            {
                transform.up = new Vector2(-1.0f, 0.0f);
                _animator.SetInteger("Direction", (int)Direction.Left);
            }
        }

        // Display the Snake home position as a cross
        Debug.DrawLine(_home + new Vector2(-_debugHomeOffset, -_debugHomeOffset), _home + new Vector2(_debugHomeOffset, _debugHomeOffset), Color.magenta);
        Debug.DrawLine(_home + new Vector2(-_debugHomeOffset, _debugHomeOffset), _home + new Vector2(_debugHomeOffset, -_debugHomeOffset), Color.magenta);
    }

    // Helper method to update the text indicator showing chase mode and movement type
    private void UpdateTextIndicator()
    {
        if (_terrainEffectText == null) return;
        
        // First get terrain information
        TerrainType currentTerrain = GetCurrentTerrain();
        float speedModifier = terrainSpeedModifiers[currentTerrain];
        string speedText = (speedModifier * 100).ToString("F0");
        string terrainInfo = $"{currentTerrain}\n{speedText}% Speed";
        
        // Build the status indicators
        List<string> indicators = new List<string>();
        
        // Add movement type indicator
        indicators.Add(_useAStarPathfinding ? "[A*]" : "[Direct]");
        
        // Add chase mode indicator
        if (ChaseMode && State != SnakeState.Fleeing)
        {
            indicators.Add("[CHASE]");
        }
        
        // Combine all indicators
        string statusIndicators = string.Join(" ", indicators);
        
        // Combine indicators with terrain info
        _terrainEffectText.text = $"{statusIndicators}\n{terrainInfo}";
    }

    // Check if the snake is stuck on its A* path and try to unstick it
    private void CheckIfStuckOnPath()
    {
        // Calculate distance moved since last frame
        float distanceMoved = Vector2.Distance(_lastPosition, transform.position);
        
        // Check if we're stuck
        if (distanceMoved < _stuckDistanceThreshold && _rb.linearVelocity.magnitude < MaxSpeed * 0.2f)
        {
            _astarStuckTimer += Time.fixedDeltaTime;
            
            // If stuck for too long, try to unstick
            if (_astarStuckTimer > _astarStuckThreshold)
            {
                // First, check if we're stuck in a corner
                if (DetectCorner())
                {
                    HandleCornerEscape();
                }
                else if (_pathRecalculationAttempts < _maxPathRecalculationAttempts)
                {
                    // Increment attempt counter
                    _pathRecalculationAttempts++;
                    
                    // Try to find an intermediate point
                    Vector2 intermediateTarget = FindIntermediateTargetAwayFromObstacles();
                    
                    if (intermediateTarget != Vector2.zero)
                    {
                        // First path to intermediate point
                        CalculatePathToDestination(intermediateTarget);
                        Debug.Log($"{name}: Using intermediate target to unstick: {intermediateTarget}");
                    }
                    else
                    {
                        // Recalculate the path to original target
                        CalculatePathToDestination(GetTargetPosition());
                        Debug.Log($"{name}: Recalculating path to original target");
                    }
                    
                    // Apply a stronger random force to help unstick
                    ApplyUnstickingForce(1.5f);
                }
                else
                {
                    // If too many attempts failed, temporarily switch to direct movement
                    Debug.Log($"{name}: Failed to unstick using A*. Temporarily using direct movement.");
                    _currentPath = null;
                    _pathRecalculationAttempts = 0;
                    
                    // Apply a more substantial unsticking force as a last resort
                    ApplyUnstickingForce(2.5f);
                }
                
                // Reset the stuck timer
                _astarStuckTimer = 0f;
            }
        }
        else
        {
            // Reset the timer if moving normally
            _astarStuckTimer = 0f;
            
            // Reset corner escape state if we're moving normally
            if (_isInCorner)
            {
                _isInCorner = false;
                _cornerEscapeTimer = 0f;
            }
        }
    }
    
    // Detect if the snake is stuck in a corner
    private bool DetectCorner()
    {
        Vector2 position = transform.position;
        int obstacleCount = 0;
        int totalDirections = 8;
        
        // Cast rays in multiple directions to detect obstacles
        for (int i = 0; i < totalDirections; i++)
        {
            float angle = i * (360f / totalDirections) * Mathf.Deg2Rad;
            Vector2 rayDir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            
            RaycastHit2D hit = Physics2D.Raycast(position, rayDir, _cornerDetectionRadius, AvoidParams.ObstacleMask);
            if (hit.collider != null)
            {
                obstacleCount++;
                Debug.DrawRay(position, rayDir * hit.distance, Color.red, 0.2f);
            }
            else
            {
                Debug.DrawRay(position, rayDir * _cornerDetectionRadius, Color.green, 0.2f);
            }
        }
        
        // If obstacles are detected in most directions, we're probably in a corner
        return obstacleCount >= totalDirections - 3;
    }

    // Handle escaping from a corner
    private void HandleCornerEscape()
    {
        // Set corner escape state
        _isInCorner = true;
        _cornerEscapeTimer += Time.fixedDeltaTime;
        
        if (_cornerEscapeDirection == Vector2.zero || _cornerEscapeTimer >= _cornerEscapeThreshold)
        {
            // Find the best direction to escape
            _cornerEscapeDirection = FindBestEscapeDirection();
            _cornerEscapeTimer = 0f;
        }
        
        // Apply a strong force in the escape direction
        _rb.AddForce(_cornerEscapeDirection * MaxAccel * 3f, ForceMode2D.Impulse);
        
        // Disable path following temporarily
        _currentPath = null;
        
        // Debug visualization
        Debug.DrawRay(transform.position, _cornerEscapeDirection * 2f, Color.yellow, 0.5f);
        Debug.Log($"{name}: Attempting to escape corner in direction: {_cornerEscapeDirection}");
        
        // After a few frames, recalculate the path
        if (_cornerEscapeTimer >= _cornerEscapeThreshold)
        {
            // Calculate a path to an intermediate point in the escape direction
            Vector2 escapePoint = (Vector2)transform.position + _cornerEscapeDirection * 3f;
            CalculatePathToDestination(escapePoint);
        }
    }

    // Find the best direction to escape from a corner
    private Vector2 FindBestEscapeDirection()
    {
        Vector2 position = transform.position;
        float longestClearance = 0f;
        Vector2 bestDirection = Vector2.zero;
        
        // Check 16 directions for the best escape route
        for (int i = 0; i < 16; i++)
        {
            float angle = i * (360f / 16) * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            
            RaycastHit2D hit = Physics2D.Raycast(position, direction, 5f, AvoidParams.ObstacleMask);
            float clearance = hit.collider != null ? hit.distance : 5f;
            
            // Debug visualization
            Debug.DrawRay(position, direction * clearance, hit.collider != null ? Color.red : Color.green, 0.5f);
            
            if (clearance > longestClearance)
            {
                longestClearance = clearance;
                bestDirection = direction;
            }
        }
        
        // If we found a good direction, return it
        if (bestDirection != Vector2.zero)
        {
            return bestDirection;
        }
        
        // Fallback: just move away from the nearest obstacle
        Collider2D[] nearbyObstacles = Physics2D.OverlapCircleAll(position, 1.5f, AvoidParams.ObstacleMask);
        if (nearbyObstacles.Length > 0)
        {
            Vector2 awayFromObstacle = Vector2.zero;
            foreach (Collider2D obstacle in nearbyObstacles)
            {
                Vector2 awayDirection = (Vector2)transform.position - (Vector2)obstacle.transform.position;
                awayFromObstacle += awayDirection.normalized;
            }
            return awayFromObstacle.normalized;
        }
        
        // Ultimate fallback: random direction
        return UnityEngine.Random.insideUnitCircle.normalized;
    }

    // Apply a force to help unstick the snake (with strength multiplier)
    private void ApplyUnstickingForce(float strengthMultiplier = 1f)
    {
        Vector2 unstickDirection = UnityEngine.Random.insideUnitCircle.normalized;
        
        // Bias towards the current path direction if available
        if (_currentPath != null && _currentPathIndex < _currentPath.Length)
        {
            Vector2 pathDirection = (_currentPath[_currentPathIndex].worldPosition - (Vector2)transform.position).normalized;
            unstickDirection = (unstickDirection + pathDirection * 2).normalized;
        }
        
        // Apply a stronger impulse force
        float unstickForce = _rb.mass * 3f * strengthMultiplier;
        _rb.AddForce(unstickDirection * unstickForce, ForceMode2D.Impulse);
        
        Debug.DrawRay(transform.position, unstickDirection * 2, Color.magenta, 1.0f);
        Debug.Log($"{name}: Applied unsticking force in direction: {unstickDirection}");
    }

    // Find an intermediate target point away from obstacles
    private Vector2 FindIntermediateTargetAwayFromObstacles()
    {
        Vector2 position = transform.position;
        float sampleRadius = 3.0f; // Increased from 2.0f to 3.0f for better clearance
        int numSamples = 16; // Increased from 8 to 16 for more precise direction finding
        
        // First try: find the direction with the longest clear path
        float longestClearance = 0f;
        Vector2 bestDirection = Vector2.zero;
        
        for (int i = 0; i < numSamples; i++)
        {
            float angle = (360f / numSamples) * i;
            Vector2 direction = Steering.rotate(Vector2.right, angle);
            
            RaycastHit2D hit = Physics2D.Raycast(position, direction, sampleRadius * 1.5f, AvoidParams.ObstacleMask);
            float clearance = hit.collider != null ? hit.distance : sampleRadius * 1.5f;
            
            // Debug visualization
            Debug.DrawRay(position, direction * clearance, hit.collider != null ? Color.red : Color.green, 0.5f);
            
            if (clearance > longestClearance)
            {
                longestClearance = clearance;
                bestDirection = direction;
            }
        }
        
        // If we found a good direction with sufficient clearance
        if (longestClearance > 1.5f)
        {
            Vector2 samplePoint = position + bestDirection * Mathf.Min(longestClearance * 0.7f, sampleRadius);
            
            // Check if this point is walkable in the A* grid
            Node node = _astarGrid.NodeFromWorldPoint(samplePoint);
            if (node != null && node.walkable)
            {
                Debug.DrawLine(position, samplePoint, Color.yellow, 0.5f);
                return samplePoint;
            }
        }
        
        // Second try: Sample in rings with increasing radius
        for (float radius = 1.0f; radius <= sampleRadius; radius += 0.5f)
        {
            for (int i = 0; i < numSamples; i++)
            {
                float angle = (360f / numSamples) * i;
                Vector2 direction = Steering.rotate(Vector2.right, angle);
                Vector2 samplePoint = position + direction * radius;
                
                // Check if this point is walkable
                Node node = _astarGrid.NodeFromWorldPoint(samplePoint);
                if (node != null && node.walkable)
                {
                    // Check if there's no obstacle between us and this point
                    RaycastHit2D hit = Physics2D.CircleCast(
                        position,
                        _collider.radius * 0.8f,
                        direction,
                        radius,
                        AvoidParams.ObstacleMask
                    );
                    
                    if (!hit)
                    {
                        Debug.DrawLine(position, samplePoint, Color.cyan, 0.5f);
                        return samplePoint;
                    }
                }
            }
        }
        
        return Vector2.zero; // No valid point found
    }

    // Method to check if a point is within the A* grid
    private bool IsPointInGrid(Vector2 point)
    {
        if (_astarGrid == null) return false;
        
        Vector2 gridWorldSize = _astarGrid.gridWorldSize;
        Vector2 gridCenter = (Vector2)_astarGrid.transform.position;
        Vector2 gridMin = gridCenter - gridWorldSize / 2;
        Vector2 gridMax = gridCenter + gridWorldSize / 2;
        
        return (point.x >= gridMin.x && point.x <= gridMax.x && 
                point.y >= gridMin.y && point.y <= gridMax.y);
    }
    
    // Calculate the A* path to the destination with improved obstacle handling
    private void CalculatePathToDestination(Vector2 destination)
    {
        if (_pathfinding == null || !_useAStarPathfinding) return;
        
        // Check if the destination is within the grid bounds
        bool isDestinationInGrid = IsPointInGrid(destination);
        
        if (isDestinationInGrid)
        {
            // Get the A* path from the current position to the destination
            _currentPath = Pathfinding.RequestPath(transform.position, destination);
            
            // Check if path is valid
            if (_currentPath == null || _currentPath.Length == 0)
            {
                Debug.Log($"{name}: Failed to find path to destination. Trying with enlarged radius.");
                
                // Try increasing obstacle overlap radius temporarily
                if (_astarGrid != null)
                {
                    float originalOverlapRadius = _astarGrid.overlapCircleRadius;
                    _astarGrid.overlapCircleRadius *= 0.8f; // Reduce overlap radius to make more nodes walkable
                    _astarGrid.CreateGrid(); // Recreate grid with smaller overlap radius
                    
                    // Try path again
                    _currentPath = Pathfinding.RequestPath(transform.position, destination);
                    
                    // If still no path, try a more significant reduction
                    if (_currentPath == null || _currentPath.Length == 0)
                    {
                        _astarGrid.overlapCircleRadius *= 0.7f; // Further reduce radius
                        _astarGrid.CreateGrid();
                        _currentPath = Pathfinding.RequestPath(transform.position, destination);
                    }
                    
                    // Restore original settings
                    _astarGrid.overlapCircleRadius = originalOverlapRadius;
                    _astarGrid.CreateGrid();
                    
                    // If still no path found, fall back to direct movement
                    if (_currentPath == null || _currentPath.Length == 0)
                    {
                        Debug.Log($"{name}: Unable to find path even with reduced radius. Using direct movement.");
                        _useAStarPathfinding = false; // Temporarily disable A* pathfinding
                        
                        // Schedule re-enabling A* after a short delay
                        StartCoroutine(ReenableAStarAfterDelay(2.0f, true));
                    }
                }
            }
            
            // Reset the path index
            _currentPathIndex = 0;
            
            // Reset stuck timer when calculating a new path
            _astarStuckTimer = 0f;
            
            // Set the path update timer
            _pathUpdateTimer = _pathUpdateInterval;
            
            // Debug - Draw the path for visualization
            DebugDrawPath();
        }
        else
        {
            // If the target is outside the grid bounds, use direct movement
            _currentPath = null;
            Debug.Log($"{name}: Target is outside A* grid bounds. Using direct movement.");
        }
    }
    
    // Draw the path for debugging
    private void DebugDrawPath()
    {
        if (_currentPath == null || _currentPath.Length == 0) return;
        
        // Draw a line from the snake to the first waypoint
        Debug.DrawLine(transform.position, _currentPath[0].worldPosition, Color.green, _pathUpdateInterval);
        
        // Draw lines between waypoints
        for (int i = 0; i < _currentPath.Length - 1; i++)
        {
            Debug.DrawLine(
                _currentPath[i].worldPosition,
                _currentPath[i + 1].worldPosition,
                Color.green, 
                _pathUpdateInterval
            );
        }
    }

    // Get movement type as a string for display in GUI
    public string GetMovementTypeString()
    {
        return _useAStarPathfinding ? "A*" : "Direct";
    }

    // Toggle between A* pathfinding and direct movement
    public void ToggleMovementType()
    {
        _useAStarPathfinding = !_useAStarPathfinding;
        
        // Update movement based on the new type
        if (_useAStarPathfinding)
        {
            Debug.Log($"{name}: Switched to A* pathfinding");
            // Recalculate path based on current state
            CalculatePathToDestination(GetTargetPosition());
        }
        else
        {
            Debug.Log($"{name}: Switched to direct movement");
            // Clear current path when switching to direct movement
            _currentPath = null;
        }
    }

    // Toggle chase mode on/off
    public void ToggleChaseMode()
    {
        ChaseMode = !ChaseMode;
        Debug.Log($"{name}: Chase mode {(ChaseMode ? "enabled" : "disabled")}");
        
        // If in Attack state and using A* pathfinding, recalculate path to ensure it reflects the new chase behavior
        if (State == SnakeState.Attack && _useAStarPathfinding && Frog != null)
        {
            CalculatePathToDestination(Frog.transform.position);
        }
    }
    
    // Get chase mode as a string for display in GUI
    public string GetChaseModeString()
    {
        return ChaseMode ? "On" : "Off";
    }

    // New method to check for environmental obstacles
    private void CheckForEnvironmentalObstacles()
    {
        // Only check if we have access to the A* grid
        if (_astarGrid != null && _useAStarPathfinding)
        {
            // Check if we're in a corner according to the grid
            if (_astarGrid.IsInCorner(transform.position))
            {
                // Apply a gentle force away from the corner
                Vector2 escapeDirection = FindBestEscapeDirection();
                _rb.AddForce(escapeDirection * MaxAccel * 0.5f, ForceMode2D.Impulse);
                
                // Debug visualization
                Debug.DrawRay(transform.position, escapeDirection * 2f, Color.yellow, 0.2f);
                
                // If we're stuck in a corner with a valid path, temporarily disable A* pathfinding
                if (_isInCorner && _currentPath != null)
                {
                    _cornerEscapeTimer += Time.fixedDeltaTime;
                    
                    // After being stuck in a corner for a while, try direct movement temporarily
                    if (_cornerEscapeTimer > _cornerEscapeThreshold * 2f)
                    {
                        Debug.Log($"{name}: Stuck in corner too long, switching to direct movement temporarily");
                        
                        // Store the current A* state
                        bool wasUsingAStar = _useAStarPathfinding;
                        
                        // Disable A* temporarily
                        _useAStarPathfinding = false;
                        
                        // Apply a strong escape force
                        _rb.AddForce(escapeDirection * MaxAccel * 2f, ForceMode2D.Impulse);
                        
                        // Schedule re-enabling A* after a short delay
                        StartCoroutine(ReenableAStarAfterDelay(1.0f, wasUsingAStar));
                        
                        // Reset corner timer
                        _cornerEscapeTimer = 0f;
                    }
                }
            }
            else
            {
                _cornerEscapeTimer = 0f;
            }
        }
    }

    // Coroutine to re-enable A* pathfinding after a delay
    private System.Collections.IEnumerator ReenableAStarAfterDelay(float delay, bool originalSetting)
    {
        yield return new WaitForSeconds(delay);
        
        // Only re-enable if it was originally enabled
        if (originalSetting)
        {
            _useAStarPathfinding = true;
            
            // Recalculate the path
            CalculatePathToDestination(GetTargetPosition());
            
            Debug.Log($"{name}: Re-enabled A* pathfinding after corner escape");
        }
    }

    // Add method to toggle control mode
    public void ToggleControlMode()
    {
        // Cycle through control modes
        controlMode = (ControlMode)(((int)controlMode + 1) % System.Enum.GetValues(typeof(ControlMode)).Length);
        
        // Update behavior tree state
        UpdateControlMode();
        
        Debug.Log($"Snake control mode changed to: {controlMode}");
    }
    
    // Update behavior tree enabled state based on control mode
    private void UpdateControlMode()
    {
        // Sync patrol point with behavior tree when in BT mode
        if (controlMode == ControlMode.BehaviorTree && _behaviorTree != null && PatrolPoint != null)
        {
            // Use direct call instead of SendMessage
            SnakeBehaviorTree behaviorTreeComponent = _behaviorTree as SnakeBehaviorTree;
            if (behaviorTreeComponent != null)
            {
                // Set patrol point through a public property or method
                behaviorTreeComponent.patrolPoint = PatrolPoint;
            }
            else
            {
                Debug.LogWarning($"Snake {name}: Failed to cast _behaviorTree to SnakeBehaviorTree component");
            }
        }
        
        // Enable/disable behavior tree component based on control mode
        if (_behaviorTree != null)
        {
            _behaviorTree.enabled = (controlMode == ControlMode.BehaviorTree);
            
            // When switching to FSM mode, ensure our text is visible and BT text is hidden
            if (controlMode == ControlMode.FSM)
            {
                // Show our text if terrain effects are enabled
                if (_terrainEffectText != null)
                {
                    _terrainEffectText.gameObject.SetActive(showTerrainEffects);
                }
                
                // Try to hide the BT text (if it exists)
                TryHideBehaviorTreeText();
            }
        }
    }
    
    // Helper method to hide the BehaviorTree text when in FSM mode
    private void TryHideBehaviorTreeText()
    {
        if (_behaviorTree != null)
        {
            // Look for the BT text object directly
            Transform btTextTransform = transform.Find("BT_TerrainEffectText");
            
            if (btTextTransform != null && btTextTransform.GetComponent<TextMesh>() != null)
            {
                btTextTransform.gameObject.SetActive(false);
                return;
            }
            
            // If not found directly, try to access it via the behavior tree component
            try
            {
                SnakeBehaviorTree btComponent = _behaviorTree as SnakeBehaviorTree;
                if (btComponent != null)
                {
                    // Use reflection to access the private _terrainEffectText field
                    System.Reflection.FieldInfo fieldInfo = btComponent.GetType().GetField(
                        "_terrainEffectText", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    if (fieldInfo != null)
                    {
                        TextMesh btText = fieldInfo.GetValue(btComponent) as TextMesh;
                        if (btText != null)
                        {
                            btText.gameObject.SetActive(false);
                        }
                    }
                }
            }
            catch (System.Exception)
            {
                // Silently fail - not critical if we can't access the field
            }
        }
    }
    
    // Get string representation of current control mode
    public string GetControlModeString()
    {
        return controlMode.ToString();
    }
}
