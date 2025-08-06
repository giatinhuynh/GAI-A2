using UnityEngine;
using System.Collections.Generic;
using BehaviorTreeSystem;
using SteeringCalcs;

public class SnakeBehaviorTree : MonoBehaviour
{
    private BehaviorTree behaviorTree;
    private Snake snake;
    private Blackboard blackboard;
    private SpriteRenderer _spriteRenderer;
    private Rigidbody2D _rb;
    private Animator _animator;
    private AStarGrid _astarGrid;
    private TextMesh _terrainEffectText; // Reference to the terrain effect text

    // Reference to the snake's patrol point
    [Header("Behavior Tree Settings")]
    public bool showDebugInfo = true;
    [Tooltip("Whether to show terrain effect text")]
    public bool showTerrainEffects = true;
    
    [Header("Transition Settings")]
    [Tooltip("Time to blend between behaviors for smoother transitions")]
    public float behaviorBlendTime = 0.3f;
    
    [Header("Patrol Settings")]
    [Tooltip("Patrol point for the snake (will use Snake's patrol point by default)")]
    public Transform patrolPoint;
    
    [Tooltip("Random patrol radius around home point")]
    public float patrolRadius = 5f;
    
    [Tooltip("Distance to consider a point reached")]
    public float arriveRadius = 1.5f;
    
    [Header("Target Settings")]
    [Tooltip("Reference to the frog (the target for the Attack state)")]
    public GameObject Frog;
    
    [Header("Map Boundaries")]
    [Tooltip("Map width (X axis limit)")]
    public float mapWidth = 15f;
    
    [Tooltip("Map height (Y axis limit)")]
    public float mapHeight = 10f;
    
    // Current targets and distances
    private Vector2 currentMoveTarget;
    private Vector2 previousMoveTarget;
    private float transitionStartTime;
    private string currentBehaviorName = "None";
    
    // Timers for cooldowns
    private float lastBenignTime = 0f;
    private float minBenignDuration = 0f; // Changed from 2f to 0f to eliminate waiting period after bite
    private float collisionCooldown = 0f;
    private float minCollisionCooldown = 0f; // Changed from 0.5f to 0f to disable cooldown
    private float attackProximityTimer = 0f;
    private float maxAttackProximityTime = 0.01f; // Changed from 0.3f to 0.01f for near-instant bites
    
    // Stuck detection
    private float stuckTimer = 0f;
    private float stuckThreshold = 1.0f;
    private Vector2 lastPosition;
    private float stuckDistanceThreshold = 0.1f;
    
    // Flee state
    private bool _applyFleeExitImpulse = false;
    private Vector2 fleeTarget;
    private Vector2 home;

    // Timer for bite cooldown
    private float biteTimer = 0f;
    private float maxBiteTime = 0.01f; // Changed from 0.3f to 0.01f for faster bite action

    // Direction IDs used by the snake animator
    private enum Direction : int
    {
        Up = 0,
        Left = 1,
        Down = 2,
        Right = 3
    }

    // Animation constants
    private const float MIN_SPEED_TO_ANIMATE = 0.1f;

    // Color definitions for each state - match with FSM colors
    private readonly Color PATROL_AWAY_COLOR = new Color(0.3f, 0.3f, 0.3f);       // Gray
    private readonly Color PATROL_HOME_COLOR = new Color(1.0f, 1.0f, 1.0f);       // White
    private readonly Color ATTACK_COLOR = new Color(1.0f, 0.2f, 0.2f);            // Red
    private readonly Color BENIGN_COLOR = new Color(0.2f, 0.94f, 0.23f);          // Green
    private readonly Color FLEEING_COLOR = new Color(0.45f, 0.98f, 0.94f);        // Cyan

    // Terrain effects tracking
    private TerrainType _lastTerrainType = TerrainType.Normal;
    private float _lastTerrainCheckTime = 0f;
    private const float TERRAIN_CHECK_INTERVAL = 0.1f; // Check terrain every 0.1 seconds
    
    // Color definitions for terrain types
    private readonly Color TERRAIN_NORMAL_COLOR = Color.white;
    private readonly Color TERRAIN_WATER_COLOR = new Color(0.3f, 0.5f, 1.0f);
    private readonly Color TERRAIN_SAND_COLOR = new Color(0.9f, 0.8f, 0.5f);
    private readonly Color TERRAIN_MUD_COLOR = new Color(0.7f, 0.5f, 0.3f);

    // Public accessors for the GUI
    public string CurrentBehaviorName => currentBehaviorName;
    public Blackboard BehaviorBlackboard => blackboard;
    public Vector2 CurrentMoveTarget => currentMoveTarget;
    public Vector2 LastPosition => lastPosition;
    public Vector2 FleeTarget => fleeTarget;
    public Vector2 Home => home;
    public float BehaviorBlendTime => behaviorBlendTime;
    public float PatrolRadius => patrolRadius;
    public float ArriveRadius => arriveRadius;

    private void Start()
    {
        // Initialize components and references
        snake = GetComponent<Snake>();
        if (snake == null)
        {
            Debug.LogError("SnakeBehaviorTree requires a Snake component!");
            return;
        }

        _spriteRenderer = GetComponent<SpriteRenderer>();
        _rb = GetComponent<Rigidbody2D>();
        _animator = GetComponent<Animator>();
        _astarGrid = FindObjectOfType<AStarGrid>();
        
        // Create the terrain effect text - using the same approach as Snake.cs
        // First try to find existing text
        _terrainEffectText = GetComponentInChildren<TextMesh>();
        
        // If not found, check for a specific child object
        if (_terrainEffectText == null)
        {
            Transform textTransform = transform.Find("TerrainEffectText");
            if (textTransform != null)
            {
                _terrainEffectText = textTransform.GetComponent<TextMesh>();
            }
            else
            {
                // Create terrain effect text if it doesn't exist
                GameObject textObject = new GameObject("BT_TerrainEffectText");  // Changed name to distinguish from Snake.cs text
                textObject.transform.SetParent(transform);
                textObject.transform.localPosition = new Vector3(0, 0.8f, 0); // Position above the snake
                _terrainEffectText = textObject.AddComponent<TextMesh>();
                _terrainEffectText.fontSize = 36;
                _terrainEffectText.characterSize = 0.15f;
                _terrainEffectText.alignment = TextAlignment.Center;
                _terrainEffectText.anchor = TextAnchor.LowerCenter;
                _terrainEffectText.fontStyle = FontStyle.Bold;
                _terrainEffectText.color = Color.white;
            }
        }
        
        // Initialize terrain effect text visibility
        if (_terrainEffectText != null)
        {
            _terrainEffectText.text = "";
            _terrainEffectText.gameObject.SetActive(false);  // Start with our text hidden
            // Force text to face up immediately
            _terrainEffectText.transform.up = Vector3.up;
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
        
        blackboard = new Blackboard();
        
        // Create behavior tree
        CreateBehaviorTree();
        
        // Switch snake to BehaviorTree mode
        snake.controlMode = Snake.ControlMode.BehaviorTree;
        
        // Set initial position and target
        home = transform.position;
        currentMoveTarget = transform.position;
        previousMoveTarget = transform.position;
        
        // Get the patrol point from the Snake component - DO NOT CREATE NEW ONES
        if (snake.PatrolPoint != null)
        {
            patrolPoint = snake.PatrolPoint;
            Debug.Log($"Snake {name} using patrol point: {patrolPoint.name} at {patrolPoint.position}");
        }
        else
        {
            Debug.LogWarning($"Snake {name} has no patrol point assigned!");
        }
        
        // Initial state is PatrolAway (moving away from home to patrol point)
        blackboard.SetValue("wasPatrollingHome", false);
        blackboard.SetValue("wasPatrollingAway", true);
        
        // Report initial state
        LogDebugState();

        // Make sure our text shows correctly according to current mode
        UpdateLabelVisibility();
    }

    private void OnEnable()
    {
        // Initialize blackboard if null (in case OnEnable is called before Start)
        if (blackboard == null)
        {
            blackboard = new Blackboard();
        }

        // Get the patrol point from the Snake component
        if (patrolPoint == null && snake != null && snake.PatrolPoint != null)
        {
            patrolPoint = snake.PatrolPoint;
            Debug.Log($"Snake {name} synced patrol point from Snake component: {patrolPoint.name}");
        }
        
        // Reset state to start with patrolling away from home
        blackboard.SetValue("wasPatrollingHome", false);
        blackboard.SetValue("wasPatrollingAway", true);
        
        // Update our label visibility whenever this component is enabled
        UpdateLabelVisibility();
    }

    // Update is called once per frame
    private void Update()
    {
        // First update our label visibility based on current mode
        UpdateLabelVisibility();
        
        if (snake.controlMode == Snake.ControlMode.BehaviorTree)
        {
            // Try to maintain reference to the TextMesh if not set
            if (_terrainEffectText == null)
            {
                _terrainEffectText = GetComponentInChildren<TextMesh>();
                if (_terrainEffectText == null)
                {
                    Transform textTransform = transform.Find("BT_TerrainEffectText");
                    if (textTransform != null)
                    {
                        _terrainEffectText = textTransform.GetComponent<TextMesh>();
                    }
                }
            }
            
            // Always ensure text is updated and properly oriented if in BT mode
            if (_terrainEffectText != null && _terrainEffectText.gameObject.activeSelf)
            {
                UpdateTextIndicator();
                UpdateTextOrientation();
            }
            
            // Check for bitten state and force benign if needed - fail-safe approach
            bool hasAlreadyBitten = false;
            blackboard.TryGetValue<bool>("bitFrog", out hasAlreadyBitten);
            if (hasAlreadyBitten && snake.State != Snake.SnakeState.Benign)
            {
                Debug.LogWarning($"[BT UPDATE] Snake bit frog but is in {snake.State} state! Forcing to Benign.");
                SetState(Snake.SnakeState.Benign);
            }
            
            // If snake is in patrol state but still has bite flags set, reset them - fail-safe
            if ((snake.State == Snake.SnakeState.PatrolAway || snake.State == Snake.SnakeState.PatrolHome) && hasAlreadyBitten)
            {
                ResetBiteFlags();
                Debug.LogWarning("[BT UPDATE] Snake in patrol state still had bite flags set - reset them to allow biting again");
            }
            
            // Log frog distance every few frames
            if (showDebugInfo && Time.frameCount % 30 == 0 && Frog != null)
            {
                float frogDist = Vector2.Distance(transform.position, Frog.transform.position);
                Debug.Log($"[BT DEBUG] Frog distance: {frogDist:F2}, AggroRange: {snake.AggroRange:F2}, InRange: {frogDist <= snake.AggroRange}");
                
                // Check attack conditions
                bool hasBitten;
                blackboard.TryGetValue<bool>("bitFrog", out hasBitten);
                Debug.Log($"[BT DEBUG] Attack prerequisites: HasBitten={hasBitten}, InRange={frogDist <= snake.AggroRange}, ShouldAttack={!hasBitten && frogDist <= snake.AggroRange}");
            }
            
            // Update terrain effect visualization
            if (Time.time > _lastTerrainCheckTime + TERRAIN_CHECK_INTERVAL)
            {
                UpdateTerrainEffectVisual();
                _lastTerrainCheckTime = Time.time;
            }
            
            // Update blackboard values before tree evaluation
            UpdateBlackboard();
            
            // Update behavior tree
            NodeState result = behaviorTree.Update();
            
            // Always update appearance to ensure correct colors are applied
            UpdateAppearance();
            
            // Log evaluation result
            if (showDebugInfo && Time.frameCount % 30 == 0)
            {
                Debug.Log($"[BT DEBUG] Tree evaluation result: {result}, Current state: {snake.State}");
            }
            
            // Debug visualization
            if (showDebugInfo)
            {
                DrawDebugVisuals();
            }
        }
        
        // Always update animation and direction regardless of control mode
        // This ensures animation is always consistent like in Snake.cs
        UpdateAnimationAndDirection();
    }

    // Update the snake's appearance based on its current state
    private void UpdateBlackboard()
    {
        // Store pathfinding information in blackboard
        bool isUsingAStar = IsUseAStarPathfinding();
        blackboard.SetValue<bool>("isUsingAStar", isUsingAStar);
        blackboard.SetValue<string>("movementType", isUsingAStar ? "A*" : "Direct");
        
        // Update frog distance
        if (Frog != null)
        {
            float distToFrog = Vector2.Distance(transform.position, Frog.transform.position);
            blackboard.SetValue<float>("frogDist", distToFrog);
            
            // Log distance for debugging
            if (showDebugInfo && Time.frameCount % 120 == 0)
            {
                // Get important flags
                bool bittenFrog = false;
                blackboard.TryGetValue<bool>("bitFrog", out bittenFrog);
                bool usingAStar = false;
                blackboard.TryGetValue<bool>("isUsingAStar", out usingAStar);
                
                string movementTypeString = "Unknown";
                blackboard.TryGetValue<string>("movementType", out movementTypeString);
                
                Debug.Log($"[BT UPDATE] Snake state: {snake.State}, BitFrog={bittenFrog}, DistToFrog={distToFrog:F2}, AggroRange={snake.AggroRange:F2}, Movement={movementTypeString}");
            }
        }
        
        // Update whether snake is currently benign
        blackboard.SetValue<bool>("isBenign", snake.State == Snake.SnakeState.Benign);
        
        // Check if at home
        float distToHome = Vector2.Distance(transform.position, home);
        bool isAtHome = distToHome <= arriveRadius;
        blackboard.SetValue("atHome", isAtHome);
        
        // Update bite timer
        if (biteTimer > 0)
        {
            biteTimer -= Time.deltaTime;
        }
        
        // Get current state values for safety checks
        blackboard.TryGetValue<bool>("hasBitFrog", out bool hasBitten);
        blackboard.TryGetValue<bool>("isScared", out bool isScared);
        
        // Safety check: If snake bit frog, enforce benign state
        if (hasBitten && snake.State != Snake.SnakeState.Benign)
        {
            Debug.LogWarning($"State inconsistency detected: Snake bit frog but state is {snake.State}. Forcing to Benign.");
            SetState(Snake.SnakeState.Benign);
        }
        
        // Safety check: If snake is scared, enforce fleeing state
        if (isScared && snake.State != Snake.SnakeState.Fleeing)
        {
            Debug.LogWarning($"State inconsistency detected: Snake is scared but state is {snake.State}. Forcing to Fleeing.");
            SetState(Snake.SnakeState.Fleeing);
        }
        
        // Auto-clear hasBitFrog when at home
        if (hasBitten && isAtHome)
        {
            blackboard.SetValue("hasBitFrog", false);
            Debug.Log("Snake at home, cleared bite flag");
        }
        
        // If snake is at patrol point, update patrol state if needed
        if (snake.PatrolPoint != null)
        {
            float distToPatrol = Vector2.Distance(transform.position, snake.PatrolPoint.position);
            bool isAtPatrol = distToPatrol <= arriveRadius;
            
            if (isAtPatrol)
            {
                blackboard.SetValue("wasPatrollingAway", true);
                blackboard.SetValue("wasPatrollingHome", false);
            }
            else if (isAtHome)
            {
                blackboard.SetValue("wasPatrollingHome", true);
                blackboard.SetValue("wasPatrollingAway", false);
            }
        }
        
        // Update current target position for visual debugging
        if (snake.State == Snake.SnakeState.Attack && Frog != null)
        {
            currentMoveTarget = Frog.transform.position;
            blackboard.SetValue<Vector2>("currentMoveTarget", currentMoveTarget);
        }
        else if (snake.State == Snake.SnakeState.Benign || snake.State == Snake.SnakeState.PatrolHome)
        {
            currentMoveTarget = home;
            blackboard.SetValue<Vector2>("currentMoveTarget", currentMoveTarget);
        }
        else if (snake.State == Snake.SnakeState.PatrolAway && snake.PatrolPoint != null)
        {
            currentMoveTarget = snake.PatrolPoint.position;
            blackboard.SetValue<Vector2>("currentMoveTarget", currentMoveTarget);
        }
        else if (snake.State == Snake.SnakeState.Fleeing)
        {
            blackboard.TryGetValue<Vector2>("fleeTarget", out Vector2 fleeTarget);
            if (fleeTarget != Vector2.zero)
            {
                currentMoveTarget = fleeTarget;
                blackboard.SetValue<Vector2>("currentMoveTarget", currentMoveTarget);
            }
        }
    }

    // Set the snake's state and update the blackboard
    private void CreateBehaviorTree()
    {
        // Create a priority-based selector as root with debug mode enabled
        var rootSelector = new SelectorNode("SnakeRoot", true); // Prioritized mode
        
        // ----- 1. FLEE BEHAVIOR (highest priority) -----
        var fleeSequence = new SequenceNode("Flee");
        
        // Only execute if snake is scared
        fleeSequence.Attach(new ConditionalNode(() => blackboard.TryGetValue<bool>("isScared", out bool isScared) && isScared, "IsScared"));
        
        // Main flee behavior in one comprehensive node
        fleeSequence.Attach(new ActionNode(() => {
            // Phase 1: Calculate flee target if needed
            if (!blackboard.TryGetValue<Vector2>("fleeTarget", out Vector2 fleeTarget) || fleeTarget == Vector2.zero)
            {
                fleeTarget = CalculateFleeDirection();
                blackboard.SetValue<Vector2>("fleeTarget", fleeTarget);
                _applyFleeExitImpulse = true;
                
                if (showDebugInfo)
                {
                    Debug.Log($"FLEE: Calculated flee target: {fleeTarget}");
                }
            }
            
            // Phase 2: Move along flee path using A* when available
            MoveThroughPath(fleeTarget, 1.1f);
            SetState(Snake.SnakeState.Fleeing);
            
            // Phase 3: Check if done fleeing (reached target or stuck)
            float distToTarget = Vector2.Distance(transform.position, fleeTarget);
            if (distToTarget < 0.5f || stuckTimer > stuckThreshold)
            {
                // End flee behavior
                blackboard.SetValue<bool>("isScared", false);
                _rb.linearVelocity = Vector2.zero;
                stuckTimer = 0f;
                
                // Apply exit impulse if configured
                if (_applyFleeExitImpulse)
                {
                    _applyFleeExitImpulse = false;
                }
                
                // Transition to patrol state instead of benign
                // This matches more natural behavior - after fleeing, a snake would patrol
                if (Vector2.Distance(transform.position, home) < arriveRadius)
                {
                    // If we're close to home, patrol away from home
                    SetState(Snake.SnakeState.PatrolAway);
                    blackboard.SetValue<bool>("wasPatrollingHome", false);
                    blackboard.SetValue<bool>("wasPatrollingAway", true);
                }
                else
                {
                    // If we're away from home, patrol back home first
                    SetState(Snake.SnakeState.PatrolHome);
                    blackboard.SetValue<bool>("wasPatrollingHome", true);
                    blackboard.SetValue<bool>("wasPatrollingAway", false);
                }
                
                if (showDebugInfo)
                {
                    Debug.Log($"FLEE: Completed fleeing, transitioning to PATROL state ({snake.State})");
                }
                
                return NodeState.Success;
            }
            
            return NodeState.Running;
        }, "FleeAction"));
        
        rootSelector.Attach(fleeSequence);

        // ----- 2. BENIGN BEHAVIOR (2nd highest priority) -----
        var benignSequence = new SequenceNode("Benign");
        
        // Only execute benign if just bitten - check both flags for redundancy
        benignSequence.Attach(new ConditionalNode(() => {
            bool bitFrog = false;
            bool hasBitFrog = false;
            blackboard.TryGetValue<bool>("bitFrog", out bitFrog);
            blackboard.TryGetValue<bool>("hasBitFrog", out hasBitFrog);
            
            bool shouldBeBenign = bitFrog || hasBitFrog || snake.State == Snake.SnakeState.Benign;
            
            if (shouldBeBenign && showDebugInfo && Time.frameCount % 30 == 0)
            {
                Debug.Log($"[BT DEBUG] Should be in Benign state: bitFrog={bitFrog}, hasBitFrog={hasBitFrog}, current state={snake.State}");
            }
            
            return shouldBeBenign;
        }, "ShouldBeBenign"));
        
        // Main benign behavior in one comprehensive node
        benignSequence.Attach(new ActionNode(() => {
            // Get home position
            Vector2 homePos = blackboard.TryGetValue<Vector2>("homePos", out Vector2 home) ? home : transform.position;
            
            // Phase 1: Set state and return home
            SetState(Snake.SnakeState.Benign);
            float distToHome = Vector2.Distance(transform.position, homePos);
            
            if (showDebugInfo && Time.frameCount % 60 == 0)
            {
                Debug.Log($"[BT BENIGN] Active! Distance to home: {distToHome:F2}");
            }
            
            if (distToHome > 0.2f)
            {
                // Not home yet, keep moving - use A* when available
                MoveThroughPath(homePos, 0.7f);
                
                if (showDebugInfo && Time.frameCount % 60 == 0)
                {
                    Debug.Log($"[BT BENIGN] Moving home. Distance = {distToHome:F2}");
                }
                
                return NodeState.Running;
            }
            
            // Phase 2: At home, wait for benign duration
            _rb.linearVelocity = Vector2.zero; // Stop moving
            float timeInBenign = Time.time - lastBenignTime;
            
            if (timeInBenign < minBenignDuration)
            {
                // Still waiting
                if (showDebugInfo && Time.frameCount % 60 == 0)
                {
                    Debug.Log($"BENIGN: Waiting at home. Time left: {(minBenignDuration - timeInBenign):F1}s");
                }
            
            return NodeState.Running;
            }
            
            // Phase 3: Benign duration complete - CLEAR ALL BITE FLAGS to allow snake to bite again
            bool oldBitFrogValue, oldHasBitFrogValue;
            blackboard.TryGetValue<bool>("bitFrog", out oldBitFrogValue);
            blackboard.TryGetValue<bool>("hasBitFrog", out oldHasBitFrogValue);
            
            // Clear all bite-related flags
            blackboard.SetValue<bool>("bitFrog", false);
            blackboard.SetValue<bool>("hasBitFrog", false);
            blackboard.SetValue<bool>("isBenign", false);
            
            // Reset attack-related timers
            attackProximityTimer = 0f;
            
            Debug.LogWarning($"[BT BENIGN] Resetting bite flags: bitFrog {oldBitFrogValue}->false, hasBitFrog {oldHasBitFrogValue}->false");
            
            // Explicitly transition to patrol away state
            SetState(Snake.SnakeState.PatrolAway);
            blackboard.SetValue<bool>("wasPatrollingHome", false);
            blackboard.SetValue<bool>("wasPatrollingAway", true);
            
            if (showDebugInfo)
            {
                Debug.Log($"BENIGN: Rest complete! Transitioning to PatrolAway state");
            }
            
            return NodeState.Success;
        }, "BenignAction"));
        
        rootSelector.Attach(benignSequence);

        // ----- 3. ATTACK BEHAVIOR (3rd priority) -----
        var attackSequence = new SequenceNode("Attack");
        
        // First check, verify frog exists and is in range (combines previous conditions)
        attackSequence.Attach(new ConditionalNode(() => {
            // Check if we've already bitten - if so, skip attack
            bool hasBittenFrog = false;
            bool hasBittenFrogAlt = false;
            blackboard.TryGetValue<bool>("bitFrog", out hasBittenFrog);
            blackboard.TryGetValue<bool>("hasBitFrog", out hasBittenFrogAlt);
            
            if (hasBittenFrog || hasBittenFrogAlt)
            {
                if (showDebugInfo && Time.frameCount % 60 == 0)
                {
                    Debug.LogWarning($"[BT DEBUG] Attack: Blocked because snake has already bitten (bitFrog={hasBittenFrog}, hasBitFrog={hasBittenFrogAlt}, State={snake.State})");
                }
                return false;
            }
            else if (showDebugInfo && Time.frameCount % 60 == 0)
            {
                Debug.Log($"[BT DEBUG] Attack: Snake can bite (bitFrog={hasBittenFrog}, hasBitFrog={hasBittenFrogAlt}, State={snake.State})");
            }
            
            // Check if frog exists and get distance
            if (Frog == null)
            {
                if (showDebugInfo && Time.frameCount % 60 == 0)
                {
                    Debug.Log("[BT DEBUG] Attack: Failed - Frog is null");
                }
                
                // Try to find frog
                Frog = GameObject.FindWithTag("Player");
                if (Frog == null) return false;
            }
            
            float frogDist = Vector2.Distance(transform.position, Frog.transform.position);
            blackboard.SetValue<float>("frogDist", frogDist);
            
            // Check if within aggro range
            bool inAggroRange = snake.ChaseMode ? true : frogDist <= snake.AggroRange;
            
            if (showDebugInfo && Time.frameCount % 30 == 0)
            {
                Debug.Log($"[BT DEBUG] Attack check: Distance={frogDist:F2}, AggroRange={snake.AggroRange:F2}, Result={inAggroRange}");
            }
            
            return inAggroRange;
        }, "ShouldAttack"));
        
        // The attack action
        attackSequence.Attach(new ActionNode(() => {
            // Ensure frog still exists
            if (Frog == null)
            {
                if (showDebugInfo)
                {
                    Debug.Log("[BT DEBUG] Attack action: Failed - Lost frog reference");
                }
                return NodeState.Failure;
            }
            
            Vector2 frogPos = Frog.transform.position;
            float frogDist = Vector2.Distance(transform.position, frogPos);
            
            // Force update state
            SetState(Snake.SnakeState.Attack);
            
            // Phase 1: Chase the frog - use increased speed for more effective attacks
            if (_astarGrid != null && IsUseAStarPathfinding())
            {
                MoveThroughPath(frogPos, 1.2f);
            }
            else
            {
                // Use more aggressive movement toward frog
                Vector2 dirToFrog = (frogPos - (Vector2)transform.position).normalized;
                _rb.AddForce(dirToFrog * snake.MaxSpeed * 1.2f);
                
                // Ensure we don't exceed maximum speed
                if (_rb.linearVelocity.magnitude > snake.MaxSpeed * 1.2f)
                {
                    _rb.linearVelocity = _rb.linearVelocity.normalized * snake.MaxSpeed * 1.2f;
                }
            }
            
            if (showDebugInfo && Time.frameCount % 30 == 0)
            {
                Debug.Log($"[BT DEBUG] Chasing frog at distance: {frogDist:F2}");
            }
            
            // Phase 2: Check if should deaggro
            if (frogDist > snake.DeAggroRange && !snake.ChaseMode)
            {
                if (showDebugInfo)
                {
                    Debug.Log($"[BT DEBUG] Stopping attack - Frog too far: {frogDist:F2} > {snake.DeAggroRange:F2}");
                }
                return NodeState.Failure; // Will fall back to patrol
            }
            
            // Phase 3: Check for bite opportunity - LOWER bite threshold for easier bites
            if (frogDist < 0.7f) // Decreased from 1.0f to make bites easier
            {
                attackProximityTimer += Time.deltaTime * 2.0f; // Increased timer speed for faster bites
                
                if (showDebugInfo && Time.frameCount % 30 == 0)
                {
                    Debug.LogWarning($"[BT DEBUG] VERY Close to frog! Distance={frogDist:F2}, Timer={attackProximityTimer:F2}/{maxAttackProximityTime:F2}");
                }
                
                // Attempt forced collision
                Vector2 dirToFrog = (frogPos - (Vector2)transform.position).normalized;
                _rb.AddForce(dirToFrog * snake.MaxSpeed * 2.0f); // Strong impulse to make contact
                
                if (attackProximityTimer >= maxAttackProximityTime)
                {
                    // Bite the frog!
                    blackboard.SetValue<bool>("bitFrog", true);
                    blackboard.SetValue<bool>("hasBitFrog", true);
                    OnBiteFrog();
                    
                    // Force position to ensure collision
                    transform.position = Vector3.MoveTowards(transform.position, Frog.transform.position, 0.4f);
                    
                    if (showDebugInfo)
                    {
                        Debug.LogWarning("[BT DEBUG] Successfully bit the frog!");
                    }
                    
                    // Reset timer
                    attackProximityTimer = 0f;
                    
                    return NodeState.Success; // Bite happened, attack complete
                }
            }
            else
            {
                // Gradual decrease rather than immediate reset - helps with jittery frog movement
                attackProximityTimer = Mathf.Max(0, attackProximityTimer - Time.deltaTime);
            }
            
            return NodeState.Running; // Still chasing
        }, "AttackAction"));
        
        // Directly attach attack sequence to root
        rootSelector.Attach(attackSequence);

        // ----- 4. PATROL BEHAVIOR (lowest priority) -----
        var patrolSequence = new SequenceNode("Patrol");
        
        // Only patrol if not bitten
        patrolSequence.Attach(new ConditionalNode(() => 
            !blackboard.TryGetValue<bool>("bitFrog", out bool hasBitten) || !hasBitten, 
            "CanPatrol"));
        
        // Main patrol behavior in one comprehensive node    
        patrolSequence.Attach(new ActionNode(() => {
            // Get patrol status
            bool patrollingToHome = false;
            blackboard.TryGetValue<bool>("patrollingToHome", out patrollingToHome);
            
            // Get positions
            Vector2 homePos = blackboard.TryGetValue<Vector2>("homePos", out home) ? home : transform.position;
            Vector2 patrolPos;
            
            if (!blackboard.TryGetValue<Vector2>("patrolPoint", out patrolPos))
            {
                // Initialize patrol point if not set
                if (snake.PatrolPoint != null)
                {
                    patrolPos = snake.PatrolPoint.position;
                }
                else
                {
                    // Generate random patrol point
                    patrolPos = homePos + (Vector2)(Random.insideUnitCircle * patrolRadius);
                }
                blackboard.SetValue<Vector2>("patrolPoint", patrolPos);
            }
            
            // Determine target based on current patrol direction
            Vector2 currentTarget;
            float distanceToTarget;
            
            if (patrollingToHome)
            {
                // Patrolling to home
                currentTarget = homePos;
                distanceToTarget = Vector2.Distance(transform.position, homePos);
                SetState(Snake.SnakeState.PatrolHome);
                
                if (showDebugInfo && Time.frameCount % 60 == 0)
                {
                    Debug.Log($"PATROL: Moving HOME. Distance = {distanceToTarget:F2}");
                }
            }
            else
            {
                // Patrolling to patrol point
                currentTarget = patrolPos;
                distanceToTarget = Vector2.Distance(transform.position, patrolPos);
                SetState(Snake.SnakeState.PatrolAway);
                
                if (showDebugInfo && Time.frameCount % 60 == 0)
                {
                    Debug.Log($"PATROL: Moving AWAY. Distance = {distanceToTarget:F2}");
                }
            }
            
            // Move toward current target using A* when available
            MoveThroughPath(currentTarget, 1.0f);
            
            // Check if reached current target
            if (distanceToTarget <= arriveRadius)
            {
                // Flip patrol direction
                blackboard.SetValue<bool>("patrollingToHome", !patrollingToHome);
                
                if (showDebugInfo)
                {
                    Debug.Log($"PATROL: Reached target! Switching direction to {(!patrollingToHome ? "HOME" : "AWAY")}");
                }
            }
            
            return NodeState.Running; // Patrol is ongoing
        }, "PatrolAction"));
        
        rootSelector.Attach(patrolSequence);

        // Initialize the behavior tree with the root selector
        behaviorTree = new BehaviorTree(rootSelector, showDebugInfo);
        
        // Set up initial blackboard values
        SetupBlackboardValues();
    }

    private void SetupBlackboardValues()
    {
        // Initialize global blackboard keys
        blackboard.SetValue<GameObject>("frog", Frog);
        blackboard.SetValue<Vector2>("homePos", transform.position);
        blackboard.SetValue<bool>("isScared", false);
        blackboard.SetValue<bool>("bitFrog", false);
        blackboard.SetValue<bool>("isBenign", false);
        blackboard.SetValue<bool>("patrollingToHome", false);
        
        // Set patrol point if available
        if (snake.PatrolPoint != null)
        {
            blackboard.SetValue<Vector2>("patrolPoint", snake.PatrolPoint.position);
        }
        else
        {
            // Generate random patrol point
            Vector2 randomDirection = Random.insideUnitCircle.normalized;
            Vector2 patrolPoint = (Vector2)transform.position + randomDirection * patrolRadius;
            blackboard.SetValue<Vector2>("patrolPoint", patrolPoint);
        }
        
        // Initial distance to frog
        if (Frog != null)
        {
            float distToFrog = Vector2.Distance(transform.position, Frog.transform.position);
            blackboard.SetValue<float>("frogDist", distToFrog);
            Debug.Log($"[BT INIT] Initial frog distance: {distToFrog:F2}, AggroRange: {snake.AggroRange:F2}");
        }
        else
        {
            blackboard.SetValue<float>("frogDist", float.MaxValue);
            Debug.Log("[BT INIT] Warning: No frog found at initialization");
        }
        
        // Store the home position
        home = transform.position;
        
        if (showDebugInfo)
        {
            Debug.Log("[BT INIT] Blackboard initialized");
        }
    }
    
    private void UpdateAppearance()
    {
        if (_spriteRenderer == null) return;
        
        // Set color based on current state
        switch (snake.State)
        {
            case Snake.SnakeState.Fleeing:
                _spriteRenderer.color = FLEEING_COLOR;
                break;
            case Snake.SnakeState.Benign:
                _spriteRenderer.color = BENIGN_COLOR;
                break;
            case Snake.SnakeState.Attack:
                _spriteRenderer.color = ATTACK_COLOR;
                break;
            case Snake.SnakeState.PatrolAway:
                _spriteRenderer.color = PATROL_AWAY_COLOR;
                break;
            case Snake.SnakeState.PatrolHome:
                _spriteRenderer.color = PATROL_HOME_COLOR;
                break;
        }
    }
    
    private void UpdateTextIndicator()
    {
        if (_terrainEffectText == null || !showTerrainEffects) return;
        
        // First get terrain information
        TerrainType currentTerrain = GetCurrentTerrain();
        float speedModifier = GetTerrainSpeedModifier();
        string speedText = (speedModifier * 100).ToString("F0");
        string terrainInfo = $"{currentTerrain}\n{speedText}% Speed";
        
        // Build the status indicators
        List<string> indicators = new List<string>();
        
        // Add movement type indicator
        bool isUsingAStar = IsUseAStarPathfinding();
        indicators.Add(isUsingAStar ? "[A*]" : "[Direct]");
        
        // Add state indicator
        switch (snake.State)
        {
            case Snake.SnakeState.Attack:
                indicators.Add("[ATTACK]");
                break;
            case Snake.SnakeState.Fleeing:
                indicators.Add("[FLEE]");
                break;
            case Snake.SnakeState.Benign:
                indicators.Add("[BENIGN]");
                break;
        }
        
        // Combine all indicators
        string statusIndicators = string.Join(" ", indicators);
        
        // Combine indicators with terrain info
        _terrainEffectText.text = $"{statusIndicators}\n{terrainInfo}";
        
        // Set color based on terrain type
        switch (currentTerrain)
        {
            case TerrainType.Normal:
                _terrainEffectText.color = TERRAIN_NORMAL_COLOR;
                break;
            case TerrainType.Water:
                _terrainEffectText.color = TERRAIN_WATER_COLOR;
                break;
            case TerrainType.Sand:
                _terrainEffectText.color = TERRAIN_SAND_COLOR;
                break;
            case TerrainType.Mud:
                _terrainEffectText.color = TERRAIN_MUD_COLOR;
                break;
        }
    }
    
    private void UpdateTextOrientation()
    {
        if (_terrainEffectText == null) return;
        
        // Always ensure text faces up in world space, regardless of snake's rotation
        _terrainEffectText.transform.up = Vector3.up;
        
        // Ensure text stays at the correct height above the snake
        Vector3 localPos = _terrainEffectText.transform.localPosition;
        
        // Make sure we maintain the correct height
        if (Mathf.Abs(localPos.y - 0.8f) > 0.01f)
        {
            localPos.y = 0.8f;
            _terrainEffectText.transform.localPosition = localPos;
        }
        
        // Make sure text is visible based on showTerrainEffects setting
        if (_terrainEffectText.gameObject.activeSelf != showTerrainEffects)
        {
            _terrainEffectText.gameObject.SetActive(showTerrainEffects);
        }
    }
    
    private void DrawDebugVisuals()
    {
        if (!showDebugInfo) return;

        ClearDebugVisuals();

        // Get color based on current state
        Color stateColor = GetColorForState(snake.State);

        // Draw a circle around the snake to indicate its state
        DrawCircle(transform.position, 1.5f, stateColor);
        
        // Show movement type (A* or Direct)
        string movementType = "Direct";
        blackboard.TryGetValue<string>("movementType", out movementType);
        bool isUsingAStar = false;
        blackboard.TryGetValue<bool>("isUsingAStar", out isUsingAStar);
        
        // Draw a small indicator for A* status (yellow for A*, white for direct)
        Color mvtColor = isUsingAStar ? Color.yellow : Color.white;
        Vector3 mvtStart = transform.position + Vector3.down * 0.8f;
        Vector3 mvtEnd = mvtStart + Vector3.down * 0.4f;
        Debug.DrawLine(mvtStart, mvtEnd, mvtColor);
        
        // Add a horizontal line for A*
        if (isUsingAStar)
        {
            Vector3 crossStart = mvtEnd + Vector3.left * 0.2f;
            Vector3 crossEnd = mvtEnd + Vector3.right * 0.2f;
            Debug.DrawLine(crossStart, crossEnd, Color.yellow);
        }

        // Draw a line to the current target
        if (blackboard.TryGetValue<Vector2>("currentMoveTarget", out Vector2 targetPos))
        {
            Vector2 currentPos = transform.position;
            Debug.DrawLine(currentPos, targetPos, stateColor);
            
            // Draw a circle at the target
            DrawCircle(targetPos, 0.5f, stateColor);
        }

        // Draw a line to the frog if we have one
        if (Frog != null)
        {
            Vector2 frogPos = Frog.transform.position;
            
            // Change line style based on state
            if (snake.State == Snake.SnakeState.Attack)
            {
                // Red dashed line for attack
                DrawDashedLine(transform.position, frogPos, ATTACK_COLOR, 0.3f, 0.2f);
            }
            else if (snake.State == Snake.SnakeState.Fleeing)
            {
                // Gold dotted line for fleeing
                DrawDashedLine(transform.position, frogPos, FLEEING_COLOR, 0.15f, 0.3f);
            }
            else if (snake.State == Snake.SnakeState.Benign)
            {
                // Thin blue line for benign
                Debug.DrawLine(transform.position, frogPos, BENIGN_COLOR, 0f, false);
            }
            else
            {
                // Green dashed line for patrol
                DrawDashedLine(transform.position, frogPos, PATROL_AWAY_COLOR, 0.5f, 0.2f);
            }
            
            // Show distance to frog
            float distToFrog = Vector2.Distance(transform.position, frogPos);
            string distText = $"{distToFrog:F1}";
            Debug.DrawLine(transform.position + Vector3.up * 0.8f, transform.position + Vector3.up * 1.2f, 
                distToFrog < snake.AggroRange ? ATTACK_COLOR : Color.gray);
        }
    }
    
    // Helper method to get color for a specific state
    private Color GetColorForState(Snake.SnakeState state)
    {
        switch (state)
        {
            case Snake.SnakeState.PatrolAway:
                return PATROL_AWAY_COLOR;
            case Snake.SnakeState.PatrolHome:
                return PATROL_HOME_COLOR;
            case Snake.SnakeState.Attack:
                return ATTACK_COLOR;
            case Snake.SnakeState.Benign:
                return BENIGN_COLOR;
            case Snake.SnakeState.Fleeing:
                return FLEEING_COLOR;
            default:
                return Color.white;
        }
    }
    
    // Draw a dashed line for debugging
    private void DrawDashedLine(Vector3 start, Vector3 end, Color color, float dashSize = 0.4f, float gapSize = 0.2f)
    {
        Vector3 direction = (end - start).normalized;
        float distance = Vector3.Distance(start, end);
        
        int dashCount = Mathf.FloorToInt(distance / (dashSize + gapSize));
        
        for (int i = 0; i < dashCount; i++)
        {
            Vector3 dashStart = start + direction * i * (dashSize + gapSize);
            Vector3 dashEnd = dashStart + direction * dashSize;
            Debug.DrawLine(dashStart, dashEnd, color);
        }
    }
    
    // Draw a circle for debugging
    private void DrawCircle(Vector2 center, float radius, Color color, int segments = 20)
    {
        for (int i = 0; i < segments; i++)
        {
            float angle1 = 2 * Mathf.PI * i / segments;
            float angle2 = 2 * Mathf.PI * (i + 1) / segments;
            
            Vector2 point1 = center + new Vector2(Mathf.Cos(angle1), Mathf.Sin(angle1)) * radius;
            Vector2 point2 = center + new Vector2(Mathf.Cos(angle2), Mathf.Sin(angle2)) * radius;
            
            Debug.DrawLine(point1, point2, color);
        }
    }
    
    public void OnBubbleHit()
    {
        // Set scared state in blackboard
        blackboard.SetValue<bool>("isScared", true);
        
        // Additional effects
        if (_spriteRenderer != null)
        {
            _spriteRenderer.color = Color.cyan;
        }
        
        // Notify behavior tree
        SetState(Snake.SnakeState.Fleeing);
    }
    
    public void OnBiteFrog()
    {
        // Log start of bite event
        Debug.LogWarning($"[BT BITE EVENT] Starting. Current snake state: {snake.State}");
        
        // Verify the snake is in Attack state - otherwise don't bite
        if (snake.State != Snake.SnakeState.Attack)
        {
            Debug.LogWarning($"[BT BITE EVENT] Cancelled - snake not in Attack state: {snake.State}");
            return;
        }
        
        // Set bite animation trigger
        _animator.SetTrigger("Bite");
        
        // Update blackboard with bite status
        blackboard.SetValue<bool>("bitFrog", true);
        blackboard.SetValue<bool>("hasBitFrog", true);
        
        try
        {
            // Use reflection to call private method (for backwards compatibility)
            System.Reflection.MethodInfo handleEvent = snake.GetType().GetMethod("HandleEvent", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (handleEvent != null)
            {
                handleEvent.Invoke(snake, new object[] { Snake.SnakeEvent.BitFrog });
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[BT BITE EVENT] Failed to call HandleEvent: {e.Message}");
        }
        
        blackboard.SetValue<bool>("isBenign", true);
        lastBenignTime = Time.time;
        
        // If we have a reference to the frog, call its damage method
        if (Frog != null)
        {
            // Move the snake really close to the frog to ensure collision
            transform.position = Vector3.MoveTowards(transform.position, Frog.transform.position, 0.3f);
            
            // Get the Frog component
            Frog frogComponent = Frog.GetComponent<Frog>();
            if (frogComponent != null)
            {
                // Notify the frog it was bitten (for animation, etc.)
                frogComponent.TakeDamage();
                Debug.LogWarning($"Snake called TakeDamage on frog. Current health: {frogComponent.Health}");
                
                // Apply additional collision force to make the bite feel more impactful
                Rigidbody2D frogRb = Frog.GetComponent<Rigidbody2D>();
                if (frogRb != null)
                {
                    // Push the frog away from the snake
                    Vector2 pushDirection = ((Vector2)Frog.transform.position - (Vector2)transform.position).normalized;
                    frogRb.AddForce(pushDirection * 4.0f, ForceMode2D.Impulse);
                }
            }
        }
        
        // Transition to Benign state after biting
        SetState(Snake.SnakeState.Benign);
        
        // Force update appearance to use correct benign color
        if (_spriteRenderer != null)
        {
            _spriteRenderer.color = BENIGN_COLOR;
            Debug.LogWarning($"[BT BITE EVENT] Explicitly set color to BENIGN_COLOR: {BENIGN_COLOR}");
        }
        
        Debug.LogWarning($"[BT BITE EVENT] Snake state after bite: {snake.State}");
    }

    private void OnDrawGizmosSelected()
    {
        // Draw home position
        Gizmos.color = Color.cyan;
        if (Application.isPlaying)
        {
            Gizmos.DrawWireSphere(home, arriveRadius);
        }
        else
        {
            Gizmos.DrawWireSphere(transform.position, arriveRadius);
        }
        
        // Draw patrol point connection
        if (snake != null && snake.PatrolPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(snake.PatrolPoint.position, arriveRadius);
            Gizmos.DrawLine(Application.isPlaying ? home : transform.position, snake.PatrolPoint.position);
        }
        
        // Draw terrain check area for visualization
        if (Application.isPlaying && _astarGrid != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 0.5f);
        }
    }

    // Add a debug method to log the current state
    private void LogDebugState()
    {
        if (snake == null || !enabled) return;
        
        bool wasPatrollingAway = false;
        bool wasPatrollingHome = false;
        blackboard.TryGetValue<bool>("wasPatrollingAway", out wasPatrollingAway);
        blackboard.TryGetValue<bool>("wasPatrollingHome", out wasPatrollingHome);
        
        // Use currentBehaviorName rather than "None"
        string displayState = string.IsNullOrEmpty(currentBehaviorName) ? "None" : currentBehaviorName;
        
        // Get patrol point position with null check
        Vector2 patrolPointPos = patrolPoint != null ? patrolPoint.position : Vector2.zero;
        
        Debug.Log($"Snake {name} - State: {displayState} Position: {transform.position} " +
                  $"Target: {currentMoveTarget} Home: {home} PatrolPoint: {patrolPointPos} " +
                  $"PatrollingAway: {wasPatrollingAway} PatrollingHome: {wasPatrollingHome}");
    }

    // Helper method to clear any debug visuals from previous frames
    private void ClearDebugVisuals()
    {
        // Nothing to do here - Unity automatically clears Debug.DrawLine calls each frame
        // This is just a placeholder method for clarity in the code
    }

    // Add a method to the class to sync BT state with Snake FSM state
    private void SyncStateWithSnake(string behaviorName)
    {
        if (snake == null) return;
        
        // Convert behavior tree state to snake FSM state
        Snake.SnakeState newState = Snake.SnakeState.PatrolAway; // Default
        
        switch (behaviorName)
        {
            case "Patrol Away":
                newState = Snake.SnakeState.PatrolAway;
                break;
            case "Patrol Home":
                newState = Snake.SnakeState.PatrolHome;
                break;
            case "Attack":
                newState = Snake.SnakeState.Attack;
                break;
            case "Benign":
                newState = Snake.SnakeState.Benign;
                break;
            case "Fleeing":
                newState = Snake.SnakeState.Fleeing;
                break;
        }
        
        // Update snake's internal state field directly
        if (snake.State != newState)
        {
            snake.State = newState;
        }
    }

    // Improve corner detection to handle screen borders better
    private bool IsInCorner()
    {
        Vector2 position = transform.position;
        int obstacleCount = 0;
        int totalDirections = 12; // Increased from 8 for better detection
        
        // Check if we're near map boundaries
        bool nearBoundary = false;
        Vector2 distanceFromCenter = position - new Vector2(0, 0); // Assuming 0,0 is center
        if (Mathf.Abs(position.x) > mapWidth - 1.5f || Mathf.Abs(position.y) > mapHeight - 1.5f)
        {
            nearBoundary = true;
            if (showDebugInfo)
            {
                Debug.LogWarning($"[BT CORNER] Near map boundary: ({position.x}, {position.y})");
            }
        }
        
        // Cast rays in multiple directions to detect obstacles
        for (int i = 0; i < totalDirections; i++)
        {
            float angle = i * (360f / totalDirections) * Mathf.Deg2Rad;
            Vector2 rayDir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            
            // Use longer rays for better detection
            RaycastHit2D hit = Physics2D.Raycast(position, rayDir, 1.5f, LayerMask.GetMask("Obstacle"));
            if (hit.collider != null)
            {
                obstacleCount++;
                Debug.DrawRay(position, rayDir * hit.distance, Color.red, 0.1f);
            }
        }
        
        // If we're near a boundary or obstacles are detected in most directions, we're in a corner
        return nearBoundary || obstacleCount >= totalDirections - 4;
    }

    // Find a better direction to escape from corners, especially near borders
    private Vector2 FindCornerEscapeDirection()
    {
        Vector2 position = transform.position;
        float longestClearance = 0f;
        Vector2 bestDirection = Vector2.zero;
        
        // Prioritize moving toward the center of the map if near boundary
        bool nearBoundary = Mathf.Abs(position.x) > mapWidth - 2f || Mathf.Abs(position.y) > mapHeight - 2f;
        if (nearBoundary)
        {
            // Calculate direction toward center of map
            Vector2 towardCenter = -position.normalized;
            
            // Check if path toward center is clear
            RaycastHit2D hit = Physics2D.Raycast(position, towardCenter, 3f, LayerMask.GetMask("Obstacle"));
            if (hit.collider == null)
            {
                if (showDebugInfo)
                {
                    Debug.Log($"[BT ESCAPE] Using direct path to center from boundary");
                }
                return towardCenter;
            }
        }
        
        // Check 16 directions for the best escape route (more directions than before)
        for (int i = 0; i < 16; i++)
        {
            float angle = i * (360f / 16) * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            
            // Use longer rays (6f instead of 5f)
            RaycastHit2D hit = Physics2D.Raycast(position, direction, 6f, LayerMask.GetMask("Obstacle"));
            float clearance = hit.collider != null ? hit.distance : 6f;
            
            // Weight directions that lead away from boundaries higher
            if (nearBoundary)
            {
                // Check if this direction leads more toward center
                Vector2 centerDir = Vector2.zero - position;
                float dotToCenter = Vector2.Dot(direction.normalized, centerDir.normalized);
                
                // Bias toward directions that lead toward the center
                if (dotToCenter > 0.3f)
                {
                    clearance *= 1.5f; // Priority boost for directions toward center
                }
            }
            
            if (clearance > longestClearance)
            {
                longestClearance = clearance;
                bestDirection = direction;
            }
        }
        
        // If we found a good direction, return it with increased magnitude
        if (bestDirection != Vector2.zero)
        {
            return bestDirection.normalized * 5f; // Increased from default value
        }
        
        // Fallback: direction toward center of map
        return (Vector2.zero - position).normalized * 5f;
    }

    // Stronger unstick method for corners and boundaries
    private void UnstickFromObstacle()
    {
        // Instead of teleporting or applying strong impulses, we'll gently redirect the snake
        
        // First, reduce the current velocity to avoid compounding forces
        _rb.linearVelocity *= 0.5f;
        
        // Get current position
        Vector2 currentPosition = transform.position;
        
        // Find a safe direction to move - prioritize toward center of map
        Vector2 towardCenter = (Vector2.zero - currentPosition).normalized;
        
        // Apply a very mild force in the chosen direction
        // This is much gentler than before, avoiding big jumps
        _rb.AddForce(towardCenter * snake.MaxSpeed * 0.5f, ForceMode2D.Force);
        
        // Reset stuck timer to prevent continuous correction
        stuckTimer = 0f;
        
        if (showDebugInfo)
        {
            Debug.Log($"[BT STUCK] Applied gentle redirect toward center");
        }
        
        // We're not modifying the stuck threshold as before
    }

    // Add border awareness to GetSafePosition method
    private Vector2 GetSafePosition(Vector2 desiredPosition)
    {
        // Check if position is too close to map boundaries and adjust if needed
        float borderMargin = 1.5f; // Increased margin
        
        // Clamp to map boundaries with margin
        desiredPosition.x = Mathf.Clamp(desiredPosition.x, -mapWidth + borderMargin, mapWidth - borderMargin);
        desiredPosition.y = Mathf.Clamp(desiredPosition.y, -mapHeight + borderMargin, mapHeight - borderMargin);
        
        // Original code continues...
        // If A* isn't available, try to validate position through raycasting
        if (_astarGrid == null || !IsUseAStarPathfinding())
        {
            // Check if there's an obstacle at the target position
            RaycastHit2D hit = Physics2D.Linecast(transform.position, desiredPosition, LayerMask.GetMask("Obstacle"));
            if (hit.collider != null)
            {
                // If obstacle found, try alternate positions around the desired point
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * 45f * Mathf.Deg2Rad;
                    Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    Vector2 alternatePosition = (Vector2)transform.position + (direction * 4f);
                    
                    // Clamp the alternate position to map boundaries
                    alternatePosition.x = Mathf.Clamp(alternatePosition.x, -mapWidth + borderMargin, mapWidth - borderMargin);
                    alternatePosition.y = Mathf.Clamp(alternatePosition.y, -mapHeight + borderMargin, mapHeight - borderMargin);
                    
                    // Check if this alternate position is free of obstacles
                    hit = Physics2D.Linecast(transform.position, alternatePosition, LayerMask.GetMask("Obstacle"));
                    if (hit.collider == null)
                    {
                        if (showDebugInfo)
                        {
                            Debug.Log($"[BT FLEE] Found safe position at angle {i * 45f}°");
                        }
                        return alternatePosition;
                    }
                }
                
                // If all directions are blocked, try a shorter distance and bias toward center
                Vector2 towardCenter = Vector2.zero - (Vector2)transform.position;
                Vector2 escapeDir = ((desiredPosition - (Vector2)transform.position).normalized + towardCenter.normalized * 0.5f).normalized;
                return (Vector2)transform.position + escapeDir * 2f;
            }
            
            return desiredPosition;
        }
        
        // Rest of original function continues...
        // If A* is available, use it to validate the position
        // Ensure the position is within the grid bounds
        if (!IsPointInGrid(desiredPosition))
        {
            // Clamp to grid bounds
            Vector2 gridSize = _astarGrid.gridWorldSize;
            Vector2 gridCenter = _astarGrid.transform.position;
            Vector2 min = gridCenter - gridSize/2;
            Vector2 max = gridCenter + gridSize/2;
            
            desiredPosition.x = Mathf.Clamp(desiredPosition.x, min.x + 0.5f, max.x - 0.5f);
            desiredPosition.y = Mathf.Clamp(desiredPosition.y, min.y + 0.5f, max.y - 0.5f);
        }
        
        // Check if the target node is walkable
        Node targetNode = _astarGrid.NodeFromWorldPoint(desiredPosition);
        if (targetNode != null && !targetNode.walkable)
        {
            if (showDebugInfo)
            {
                Debug.LogWarning($"[BT FLEE] Target position is unwalkable: {desiredPosition}");
            }
            
            // Find nearest walkable node
            Node nearestWalkable = FindNearestWalkableNode(desiredPosition);
            if (nearestWalkable != null)
            {
                if (showDebugInfo)
                {
                    Debug.Log($"[BT FLEE] Found walkable alternative at: {nearestWalkable.worldPosition}");
                }
                return nearestWalkable.worldPosition;
            }
        }
        
        return desiredPosition;
    }

    private void MoveToTarget(Vector2 targetPosition, float speedModifier = 1f)
    {
        // Only proceed if target is valid
        if (targetPosition == Vector2.zero) return;
        
        // Get distance to target
        float distToTarget = Vector2.Distance(transform.position, targetPosition);
        
        // Calculate desired velocity
        Vector2 desiredVelocity = Vector2.zero;
        if (distToTarget > 0.01f)
        {
            Vector2 direction = (targetPosition - (Vector2)transform.position).normalized;
            
            // Apply speed modifiers (terrain, user-specified)
            float terrainModifier = GetTerrainSpeedModifier();
            float currentMaxSpeed = snake.MaxSpeed * speedModifier * terrainModifier;
            
            desiredVelocity = direction * currentMaxSpeed;
            
            // Apply steering behavior to move toward target
            Vector2 steering = (desiredVelocity - _rb.linearVelocity) / snake.AccelTime;
            steering = Vector2.ClampMagnitude(steering, snake.MaxAccel);
            _rb.AddForce(steering);
        }
        else
        {
            // Very close to target, slow down
            _rb.linearVelocity *= 0.9f;
        }
        
        // Enhanced stuck detection - Check if we're truly stuck
        Vector2 currentPos = transform.position;
        float distanceMoved = Vector2.Distance(currentPos, lastPosition);
        
        // Only consider stuck if we're trying to move but not actually moving much
        if (_rb.linearVelocity.magnitude > 0.1f && distanceMoved < stuckDistanceThreshold * 0.5f && 
            _rb.linearVelocity.magnitude < snake.MaxSpeed * 0.1f)
        {
            stuckTimer += Time.deltaTime * 0.5f; // Increment timer more slowly
            
            // Only attempt gentle redirection after a longer period of being stuck
            if (stuckTimer > stuckThreshold * 2f)
            {
                Debug.Log("Snake is stuck in MoveToTarget - applying gentle redirection");
                
                // Reduce current velocity to avoid compounding forces
                _rb.linearVelocity *= 0.5f;
                
                // Calculate a safe direction to move (prioritize center of map)
                Vector2 centerDirection = (Vector2.zero - (Vector2)transform.position).normalized;
                Vector2 targetDirection = (targetPosition - (Vector2)transform.position).normalized;
                
                // Blend between center and target direction, favoring center if very stuck
                Vector2 redirectDirection = Vector2.Lerp(targetDirection, centerDirection, 0.7f).normalized;
                
                // Apply mild force in chosen direction
                _rb.AddForce(redirectDirection * snake.MaxAccel * 0.8f);
                
                // Reset stuck timer partially to allow time to move
                stuckTimer = stuckThreshold * 0.5f;
            }
        }
        else
        {
            // Reset timer if moving normally
            stuckTimer = 0f;
        }
        
        // Store position for next frame's check
        lastPosition = currentPos;
        
        // Update animation and direction
        UpdateAnimationAndDirection();
    }

    private void MoveThroughPath(Vector2 target, float speedModifier)
    {
        // Check if A* pathfinding is enabled
        bool isUsingAStar = IsUseAStarPathfinding();
        
        // Apply terrain speed modifier
        float terrainModifier = GetTerrainSpeedModifier();
        float currentMaxSpeed = snake.MaxSpeed * speedModifier * terrainModifier;
        
        // Cache current position
        Vector2 currentPosition = transform.position;
        
        // Check if target is in grid (if using A*)
        if (isUsingAStar && _astarGrid != null)
        {
        if (!IsPointInGrid(target))
        {
                // Target is outside grid, clamp to grid bounds
            Vector2 gridSize = _astarGrid.gridWorldSize;
            Vector2 gridCenter = _astarGrid.transform.position;
            Vector2 min = gridCenter - gridSize/2;
            Vector2 max = gridCenter + gridSize/2;
            
                // Clamp to grid bounds with a margin
                target = new Vector2(
                Mathf.Clamp(target.x, min.x + 0.5f, max.x - 0.5f),
                Mathf.Clamp(target.y, min.y + 0.5f, max.y - 0.5f)
            );
            }
            
            // Request path using Pathfinding component
            Node[] path = Pathfinding.RequestPath(currentPosition, target);
            
            // If valid path exists
            if (path != null && path.Length > 0)
            {
                // Get first waypoint (skip our current position)
                int waypointIndex = 1;  // Start with second node (first is our position)
                
                // Safety check to ensure array bounds
                if (waypointIndex < path.Length)
                {
                    Vector2 waypoint = path[waypointIndex].worldPosition;
                    
                    // Check if in a corner for better navigation
                    bool isInCorner = _astarGrid.IsInCorner(transform.position);
                    Vector2 waypointDirection = (waypoint - currentPosition).normalized;
                    
                    // If in a corner, look further ahead in the path
                    if (isInCorner && waypointIndex + 2 < path.Length)
                    {
                        // Look two waypoints ahead to help with corner navigation
                        Vector2 futureWaypoint = path[waypointIndex + 2].worldPosition;
                        Vector2 futureDirection = (futureWaypoint - currentPosition).normalized;
                        
                        // Blend directions (70% current waypoint, 30% future waypoint)
                        waypointDirection = (waypointDirection * 0.7f + futureDirection * 0.3f).normalized;
                        
                        // Set modified target
                        target = currentPosition + waypointDirection * 2f;
                    }
                    else
                    {
                        // Use the next waypoint as target
                        target = waypoint;
                    }
                    
                    // Debug visualization
                    if (showDebugInfo)
                    {
                        // Draw path
                        for (int i = 0; i < path.Length - 1; i++)
                        {
                            Debug.DrawLine(path[i].worldPosition, path[i + 1].worldPosition, Color.yellow, 0.1f);
                        }
                    }
                }
            }
            else
            {
                // No valid path found, use direct movement with obstacle avoidance
                isUsingAStar = false;
            }
        }
        
        // Apply movement - either direct or to A* waypoint
        if (!isUsingAStar)
        {
            // Use movement with obstacle avoidance like in Snake.cs
            MoveWithObstacleAvoidance(target, speedModifier);
        }
        else
        {
            // Apply steering to the target/waypoint
            ApplySteeringBehavior(target, speedModifier);
        }
        
        // Update stuck detection - making this much more permissive
        float distanceMoved = Vector2.Distance(lastPosition, currentPosition);
        
        // Only consider stuck if almost completely motionless and velocity is very low
        if (distanceMoved < stuckDistanceThreshold * 0.5f && _rb.linearVelocity.magnitude < snake.MaxSpeed * 0.1f)
        {
            // Increment stuck timer but at a slower rate to be more patient
            stuckTimer += Time.deltaTime * 0.5f;
            
            // Only consider unsticking if stuck for a very long time
            if (stuckTimer > stuckThreshold * 2.0f)
            {
                // Just gently nudge the snake in the desired direction
                ApplySteeringBehavior(target, speedModifier * 1.2f);
                
                // Reset stuck timer partially
                stuckTimer = stuckThreshold * 0.5f;
            }
        }
        else
        {
            // Reset stuck timer when moving
            stuckTimer = 0f;
        }
        
        // Update last position for next frame's stuck detection
        lastPosition = currentPosition;
    }

    private Vector2 GetSmoothedTarget(Vector2 targetPosition)
    {
        // Store the new target
        currentMoveTarget = targetPosition;
        
        // If we just started a transition, blend from previous target
        float transitionProgress = (Time.time - transitionStartTime) / behaviorBlendTime;
        if (transitionProgress < 1.0f)
        {
            return Vector2.Lerp(previousMoveTarget, currentMoveTarget, transitionProgress);
        }
        
        return currentMoveTarget;
    }

    private float GetTerrainSpeedModifier()
    {
        TerrainType currentTerrain = GetCurrentTerrain();
        
        // Default modifiers
        Dictionary<TerrainType, float> defaultModifiers = new Dictionary<TerrainType, float>()
        {
            { TerrainType.Normal, 1.0f },
            { TerrainType.Water, 0.7f },
            { TerrainType.Sand, 0.8f },
            { TerrainType.Mud, 0.5f },
        };
        
        // Try to get snake's modifiers
        Dictionary<TerrainType, float> snakeModifiers = null;
        try {
            var field = typeof(Snake).GetField("terrainSpeedModifiers", 
                System.Reflection.BindingFlags.Instance | 
                System.Reflection.BindingFlags.NonPublic);
            if (field != null)
            {
                snakeModifiers = field.GetValue(snake) as Dictionary<TerrainType, float>;
            }
        }
        catch (System.Exception e) {
            if (showDebugInfo)
            {
                Debug.LogWarning($"[BT TERRAIN] Failed to get Snake's terrainSpeedModifiers: {e.Message}");
            }
        }
        
        // Use snake modifiers if available, otherwise defaults
        if (snakeModifiers != null && snakeModifiers.TryGetValue(currentTerrain, out float modifier))
        {
            return modifier;
        }
        
        // Fallback to defaults
        if (defaultModifiers.TryGetValue(currentTerrain, out float defaultMod))
        {
            return defaultMod;
        }
        
        return 1.0f;
    }
    
    private TerrainType GetCurrentTerrain()
    {
        if (_astarGrid != null && IsPointInGrid(transform.position))
        {
            Node currentNode = _astarGrid.NodeFromWorldPoint(transform.position);
            if (currentNode != null)
            {
                return currentNode.terrainType;
            }
        }
        
        return TerrainType.Normal;
    }
    
    private bool IsPointInGrid(Vector2 point)
    {
        if (_astarGrid == null) return true;
        
        Vector2 gridSize = _astarGrid.gridWorldSize;
        Vector2 gridCenter = _astarGrid.transform.position;
        Vector2 min = gridCenter - gridSize/2;
        Vector2 max = gridCenter + gridSize/2;
        
        return point.x >= min.x && point.x <= max.x && 
               point.y >= min.y && point.y <= max.y;
    }

    private void SetState(Snake.SnakeState state)
    {
        if (snake.State != state)
        {
            snake.State = state;
            
            // Update currentBehaviorName to match state
            switch (state)
            {
                case Snake.SnakeState.PatrolAway:
                    currentBehaviorName = "Patrol Away";
                    break;
                case Snake.SnakeState.PatrolHome:
                    currentBehaviorName = "Patrol Home";
                    break;
                case Snake.SnakeState.Attack:
                    currentBehaviorName = "Attack";
                    break;
                case Snake.SnakeState.Benign:
                    currentBehaviorName = "Benign";
                    break;
                case Snake.SnakeState.Fleeing:
                    currentBehaviorName = "Fleeing";
                    break;
            }
            
            // Update visualization by refreshing appearance
            UpdateAppearance();
        }
    }

    private Vector2 CalculateFleeDirection()
    {
        // Default random direction if no frog
        if (Frog == null) return GetSafePosition(Random.insideUnitCircle.normalized * 5f);
        
        Vector2 awayFromFrog = ((Vector2)transform.position - (Vector2)Frog.transform.position).normalized;
        Vector2 targetPosition = (Vector2)transform.position + awayFromFrog * 5f;
        
        // Ensure the position is valid and not inside obstacles
        return GetSafePosition(targetPosition);
    }

    // Find the nearest walkable node to a position
    private Node FindNearestWalkableNode(Vector2 position)
    {
        if (_astarGrid == null) return null;
        
        Node closestNode = null;
        float closestDist = float.MaxValue;
        
        // Start with the immediate node and spiral outward
        Node startNode = _astarGrid.NodeFromWorldPoint(position);
        if (startNode == null) return null;
        
        int gridX = startNode.gridX;
        int gridY = startNode.gridY;
        
        // Search in a spiral pattern around the position
        for (int radius = 1; radius <= 5; radius++)
        {
            // Check nodes in a square pattern around the starting point
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    // Skip nodes we've already checked (inner squares)
                    if (Mathf.Abs(dx) < radius && Mathf.Abs(dy) < radius)
                        continue;
                    
                    // Get the node at this position
                    Node node = _astarGrid.NodeFromWorldPoint(new Vector2(gridX + dx, gridY + dy));
                    if (node != null && node.walkable)
                    {
                        float dist = Vector2.Distance(position, node.worldPosition);
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            closestNode = node;
                        }
                    }
                }
            }
            
            // If we found a node, return it
            if (closestNode != null)
                return closestNode;
        }
        
        // Fallback to the original node if nothing better was found
        return startNode;
    }

    private void ApplySteeringBehavior(Vector2 target, float speedModifier)
    {
        if (_rb == null) return;
        
        // Ensure speedModifier is within reasonable bounds
        speedModifier = Mathf.Clamp(speedModifier, 0.3f, 1.5f);
        
        // Calculate distance to target
        Vector2 currentPos = transform.position;
        float distToTarget = Vector2.Distance(currentPos, target);
        
        // If extremely close to target, just stop movement and return
        if (distToTarget < 0.1f)
        {
            _rb.linearVelocity = Vector2.zero;
            return;
        }
        
        // Get values from snake component
        float maxSpeed = snake.MaxSpeed;
        float maxAccel = snake.MaxAccel;
        float accelTime = snake.AccelTime;
        
        // Calculate desired velocity based on terrain
        float terrainModifier = GetTerrainSpeedModifier();
        float currentMaxSpeed = maxSpeed * speedModifier * terrainModifier;
        
        // Calculate desired velocity (direction * speed)
        Vector2 desiredVel = (target - currentPos).normalized * currentMaxSpeed;
        
        // Convert desired velocity to a force using Snake.cs's approach
        Vector2 steering = Steering.DesiredVelToForce(desiredVel, _rb, accelTime, maxAccel);
        _rb.AddForce(steering);
        
        // Store terrain type in blackboard for debugging
        TerrainType currentTerrain = GetCurrentTerrain();
        blackboard.SetValue<TerrainType>("currentTerrain", currentTerrain);
        blackboard.SetValue<float>("terrainSpeedModifier", terrainModifier);
        
        // If we're in a corner, reduce max velocity to help with navigation (same as Snake.cs)
        if (_astarGrid != null && _astarGrid.IsInCorner(transform.position) && _rb.linearVelocity.magnitude > currentMaxSpeed * 0.7f)
        {
            _rb.linearVelocity = _rb.linearVelocity.normalized * (currentMaxSpeed * 0.7f);
        }
        
        // Use our centralized animation update method
        UpdateAnimationAndDirection();
    }

    private bool IsUseAStarPathfinding()
    {
        // Try to access the field via reflection
        try
        {
            var field = typeof(Snake).GetField("_useAStarPathfinding", 
                System.Reflection.BindingFlags.Instance | 
                System.Reflection.BindingFlags.NonPublic);
            if (field != null)
                return (bool)field.GetValue(snake);
        }
        catch {}
        return false;
    }

    // Helper method to reset bite flags
    public void ResetBiteFlags()
    {
        // Reset all bite-related flags in the blackboard
        blackboard.SetValue<bool>("bitFrog", false);
        blackboard.SetValue<bool>("hasBitten", false);
        blackboard.SetValue<bool>("hasBitFrog", false);
        blackboard.SetValue<float>("attackProximityTimer", 0f);
        
        // Reset bite timer
        biteTimer = 0f;
        
        // Log the reset for debugging
        Debug.Log($"Reset bite flags for snake in state: {snake.State}");
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        HandlePossibleFrogCollision(collision.gameObject);
    }
    
    private void OnCollisionStay2D(Collision2D collision)
    {
        HandlePossibleFrogCollision(collision.gameObject);
    }
    
    private void OnTriggerEnter2D(Collider2D collision)
    {
        HandlePossibleFrogCollision(collision.gameObject);
    }
    
    private void OnTriggerStay2D(Collider2D collision)
    {
        HandlePossibleFrogCollision(collision.gameObject);
    }
    
    private void HandlePossibleFrogCollision(GameObject collisionObject)
    {
        // Only handle frog collisions if we have a valid Frog reference
        if (collisionObject.CompareTag("Frog"))
        {
            Frog = collisionObject;
            
            // Only process attack logic if the snake is in Attack state
            if (snake.State == Snake.SnakeState.Attack)
            {
                // If we're in attack state and not mid-bite, start tracking proximity
                if (!blackboard.TryGetValue<bool>("hasBitten", out bool hasBitten) || !hasBitten)
                {
                    // Start or continue tracking attack proximity time
                    if (blackboard.TryGetValue<float>("attackProximityTimer", out float proximityTimer))
                    {
                        proximityTimer += Time.deltaTime;
                        blackboard.SetValue("attackProximityTimer", proximityTimer);
                        
                        // After a certain duration of being close, trigger the bite
                        if (proximityTimer >= maxAttackProximityTime)
                        {
                            // Trigger bite action
                            OnBiteFrog();
                            
                            // Reset the timer
                            blackboard.SetValue("attackProximityTimer", 0f);
                            
                            // Mark that we've bitten to prevent multiple bites
                            blackboard.SetValue("hasBitten", true);
                        }
                    }
                    else
                    {
                        // Initialize the timer if it doesn't exist
                        blackboard.SetValue("attackProximityTimer", 0f);
                    }
                }
            }
            else if (snake.State == Snake.SnakeState.Benign)
            {
                // In Benign state, reset bite flags but don't attack
                ResetBiteFlags();
                Debug.Log("Snake is in Benign state - not attacking frog");
            }
            else if (snake.State == Snake.SnakeState.Fleeing)
            {
                // In Fleeing state, move away from frog
                ResetBiteFlags();
                Debug.Log("Snake is in Fleeing state - moving away from frog");
            }
            else 
            {
                // In Patrol states, just reset bite flags
                ResetBiteFlags();
                Debug.Log("Snake is in Patrol state - not attacking frog");
            }
        }
    }

    // Completely revised text indicator method following Snake.cs approach
    private void UpdateTerrainEffectVisual()
    {
        if (!showTerrainEffects || _terrainEffectText == null) return;
        
        // Update the terrain type tracking
        TerrainType currentTerrain = GetCurrentTerrain();
        if (currentTerrain != _lastTerrainType)
        {
            _lastTerrainType = currentTerrain;
            
            // Log terrain change for debugging
            if (showDebugInfo)
            {
                float speedModifier = GetTerrainSpeedModifier();
                Debug.Log($"[BT TERRAIN] Changed to {currentTerrain} (Speed mod: {speedModifier:F2})");
            }
        }
        
        // Update text content and ensure proper orientation
        UpdateTextIndicator();
        UpdateTextOrientation();
    }

    public void ToggleTerrainEffects()
    {
        showTerrainEffects = !showTerrainEffects;
        
        // Update text visibility
        if (_terrainEffectText != null)
        {
            _terrainEffectText.gameObject.SetActive(showTerrainEffects);
        }
        
        Debug.Log($"Snake {name} terrain effects: {(showTerrainEffects ? "ON" : "OFF")}");
    }

    // New method to properly handle label visibility based on control mode
    private void UpdateLabelVisibility()
    {
        // No work to do if we don't have our references
        if (snake == null || _terrainEffectText == null) return;
        
        // Get direct reference to Snake.cs text mesh using reflection
        TextMesh snakeTextMesh = GetSnakeTextMesh();
        
        // In BehaviorTree mode
        if (snake.controlMode == Snake.ControlMode.BehaviorTree)
        {
            // Hide Snake.cs text (if we found it)
            if (snakeTextMesh != null)
            {
                snakeTextMesh.gameObject.SetActive(false);
                if (showDebugInfo && Time.frameCount % 60 == 0)
                {
                    Debug.Log("[BT] Snake.cs text mesh hidden in BT mode");
                }
            }
            
            // Show our BT text
            _terrainEffectText.gameObject.SetActive(showTerrainEffects);
        }
        // In FSM mode
        else
        {
            // Hide our BT text
            _terrainEffectText.gameObject.SetActive(false);
            
            // Show Snake.cs text (if we found it)
            if (snakeTextMesh != null)
            {
                snakeTextMesh.gameObject.SetActive(snake.showTerrainEffects);
                if (showDebugInfo && Time.frameCount % 60 == 0)
                {
                    Debug.Log("[BT] Snake.cs text mesh restored in FSM mode");
                }
            }
        }
    }

    // Method to access Snake.cs's text mesh field using reflection
    private TextMesh GetSnakeTextMesh()
    {
        if (snake == null) return null;
        
        try
        {
            // Try to access the private field directly through reflection
            System.Reflection.FieldInfo fieldInfo = snake.GetType().GetField("_terrainEffectText", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (fieldInfo != null)
            {
                return fieldInfo.GetValue(snake) as TextMesh;
            }
        }
        catch (System.Exception e)
        {
            if (showDebugInfo)
            {
                Debug.LogWarning($"[BT] Error accessing Snake.cs _terrainEffectText: {e.Message}");
            }
        }
        
        return null;
    }

    // New method for movement with basic obstacle avoidance
    private void MoveWithObstacleAvoidance(Vector2 target, float speedModifier)
    {
        // Get current state to determine steering behavior
        bool isArriving = (snake.State == Snake.SnakeState.PatrolAway || 
                            snake.State == Snake.SnakeState.PatrolHome || 
                            snake.State == Snake.SnakeState.Benign || 
                            snake.State == Snake.SnakeState.Fleeing);
                        
        // Get terrain modified speed
        float terrainModifier = GetTerrainSpeedModifier();
        float currentMaxSpeed = snake.MaxSpeed * speedModifier * terrainModifier;
        
        // Get avoidance parameters from snake
        AvoidanceParams avoidParams = snake.AvoidParams;
        
        // Create enhanced avoidance params for fleeing (just like Snake.cs)
        if (snake.State == Snake.SnakeState.Fleeing)
        {
            avoidParams = new AvoidanceParams
            {
                Enable = avoidParams.Enable,
                ObstacleMask = avoidParams.ObstacleMask,
                MaxCastLength = avoidParams.MaxCastLength * 1.5f,
                CircleCastRadius = avoidParams.CircleCastRadius * 1.5f,
                AngleIncrement = avoidParams.AngleIncrement
            };
        }
        
        // Use appropriate steering behavior based on state
        Vector2 desiredVel;
        
        if (isArriving)
        {
            // Use arrive steering for PatrolAway, PatrolHome, Benign, and Fleeing
            desiredVel = Steering.Arrive(transform.position, target, snake.ArriveRadius, currentMaxSpeed, avoidParams);
        }
        else
        {
            // Use seek steering for Attack state
            desiredVel = Steering.Seek(transform.position, target, currentMaxSpeed, avoidParams);
        }
        
        // Convert desired velocity to force and apply it
        Vector2 steering = Steering.DesiredVelToForce(desiredVel, _rb, snake.AccelTime, snake.MaxAccel);
        _rb.AddForce(steering);
        
        // Apply additional obstacle avoidance for fleeing state
        if (snake.State == Snake.SnakeState.Fleeing)
        {
            Vector2 avoidance = Vector2.zero;
            
            // Cast rays in multiple directions to detect obstacles (same as Snake.cs)
            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f * Mathf.Deg2Rad;
                Vector2 rayDir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)).normalized;
                
                RaycastHit2D hit = Physics2D.Raycast(transform.position, rayDir, avoidParams.MaxCastLength * 1.5f);
                if (hit.collider != null && !hit.collider.CompareTag("Snake") && !hit.collider.CompareTag("Frog"))
                {
                    // Calculate avoidance force based on proximity (closer = stronger force)
                    float strength = 1.0f - (hit.distance / (avoidParams.MaxCastLength * 1.5f));
                    avoidance += -rayDir * strength * snake.MaxAccel * 2f;
                }
            }
            
            // Blend avoidance with desired velocity
            if (avoidance.magnitude > 0.1f)
            {
                _rb.AddForce(avoidance);
                // Maintain speed after adding avoidance
                if (_rb.linearVelocity.magnitude > currentMaxSpeed)
                {
                    _rb.linearVelocity = _rb.linearVelocity.normalized * currentMaxSpeed;
                }
            }
        }
        
        // Corner handling (same as in ApplySteeringBehavior)
        if (_astarGrid != null && _astarGrid.IsInCorner(transform.position) && _rb.linearVelocity.magnitude > currentMaxSpeed * 0.7f)
        {
            _rb.linearVelocity = _rb.linearVelocity.normalized * (currentMaxSpeed * 0.7f);
        }
        
        // Update animation and direction after applying movement forces
        UpdateAnimationAndDirection();
    }

    // Add a new dedicated method for handling animation and direction consistently
    private void UpdateAnimationAndDirection()
    {
        // Only update animation if we have required components
        if (_rb == null || _animator == null) return;
        
        // Match Snake.cs animation logic exactly
        if (_rb.linearVelocity.magnitude > MIN_SPEED_TO_ANIMATE)
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
    }
} 