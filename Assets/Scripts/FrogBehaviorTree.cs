using UnityEngine;
using System.Collections.Generic;
using BehaviorTreeSystem;
using SteeringCalcs;

public class FrogBehaviorTree : MonoBehaviour
{
    private BehaviorTree behaviorTree;
    private Frog frog;
    private Blackboard blackboard;
    private SpriteRenderer _spriteRenderer;
    private Rigidbody2D _rb;
    private AStarGrid _astarGrid;

    // Add a field to track user-initiated targets
    private bool _isFollowingUserTarget = false;
    private Vector2? _userTargetPosition = null;

    [Header("Behavior Tree Settings")]
    public bool showDebugInfo = true;
    
    [Header("Transition Settings")]
    [Tooltip("Time to blend between behaviors for smoother transitions")]
    public float behaviorBlendTime = 0.3f;
    
    [Header("Decision Making Settings")]
    [Tooltip("Minimum score needed to switch targets")]
    public float targetSwitchThreshold = 0.2f;
    [Tooltip("Weight for distance when evaluating flies")]
    public float distanceWeight = 0.3f;
    [Tooltip("Weight for safety when evaluating flies")]
    public float safetyWeight = 0.4f;
    [Tooltip("Weight for terrain factors when evaluating flies")]
    public float terrainWeight = 0.2f;
    [Tooltip("Weight for center proximity when evaluating flies")]
    public float centerWeight = 0.1f;

    // Match Frog.cs parameters
    private float scaredRange = 5f;
    private float huntRange = 7f;
    private float bubbleRange = 7f;
    private Vector2 anchorDims = new Vector2(15f, 10f);
    
    // Current targets and distances
    private Fly bestFly;
    private Snake closestSnake;
    private Vector2 currentMoveTarget;
    private Vector2 previousMoveTarget;
    private float transitionStartTime;
    private string currentBehaviorName = "None";
    
    // Track multiple threats
    private List<Snake> nearbySnakes = new List<Snake>();
    
    // Store candidate scoring
    private Dictionary<Fly, float> flyScores = new Dictionary<Fly, float>();
    
    // Timers for cooldowns
    private float lastFlyEvaluationTime = 0f;
    private float flyEvaluationInterval = 0.2f;
    private float lastBubbleTime = 0f;
    private float bubbleCooldown = 1.5f;

    [Header("Screen Boundary Settings")]
    [Tooltip("Allow the frog to go slightly outside screen boundaries")]
    public bool allowExploreOutsideScreen = true;
    
    [Tooltip("How far outside the screen the frog is allowed to go (0-1, where 1 is a full screen width/height)")]
    [Range(0.0f, 1.0f)]
    public float screenBoundaryTolerance = 0.2f;
    
    [Tooltip("Maximum time allowed outside screen boundaries before forced return")]
    public float maxTimeOutsideScreen = 5.0f;
    
    // Tracking time outside screen
    private float outsideScreenTimer = 0f;
    private bool wasOutsideScreen = false;

    // Public accessors
    public string CurrentBehaviorName => currentBehaviorName;
    public Fly BestFly => bestFly;
    public Dictionary<Fly, float> FlyScores => flyScores;
    public Blackboard BehaviorBlackboard => blackboard;
    public List<Snake> NearbySnakes => nearbySnakes;
    public Snake ClosestSnake => closestSnake;
    public float ScaredRange => scaredRange;
    public bool WasOutsideScreen => wasOutsideScreen;
    public float MaxTimeOutsideScreen => maxTimeOutsideScreen;
    public float OutsideScreenTimer => outsideScreenTimer;
    public float TargetSwitchThreshold => targetSwitchThreshold;
    public bool AllowExploreOutsideScreen => allowExploreOutsideScreen;
    public float ScreenBoundaryTolerance => screenBoundaryTolerance;

    private void Start()
    {
        frog = GetComponent<Frog>();
        if (frog == null)
        {
            Debug.LogError("FrogBehaviorTree requires a Frog component!");
            return;
        }

        _spriteRenderer = GetComponent<SpriteRenderer>();
        _rb = GetComponent<Rigidbody2D>();
        _astarGrid = FindAnyObjectByType<AStarGrid>();
        
        blackboard = new Blackboard();
        InitializeBehaviorTree();
        
        // Set initial position
        currentMoveTarget = transform.position;
        previousMoveTarget = transform.position;
    }

    // Update is called once per frame
    private void Update()
    {
        if (frog.controlMode == Frog.ControlMode.BehaviorTree)
        {
            behaviorTree.Update();
            UpdateDebugVisuals();
        }
    }

    // Public method to set user target position
    private void InitializeBehaviorTree()
    {
        // Create the root selector node
        var rootSelector = new SelectorNode("Root");
        
        // 0. User Target Check - NEW HIGHEST PRIORITY
        var userTargetCheck = new SequenceNode("User Target Check");
        userTargetCheck.Attach(new ConditionalNode(() => CheckForUserTarget(), "Has User Target"));
        userTargetCheck.Attach(new ActionNode(() => {
            // Just continue following the user target, no need to set it again
            SetCurrentBehavior("Following User Target");
            return NodeState.Success;
        }, "Follow User Target"));
        rootSelector.Attach(userTargetCheck);

        // 1. Health Check (matches Frog.cs health <= 0 check)
        var healthCheck = new SequenceNode("Health Check");
        healthCheck.Attach(new ConditionalNode(() => frog.Health <= 0, "No Health"));
        healthCheck.Attach(new ActionNode(() => {
            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = new Color(1f, 0.2f, 0.2f);
            }
            SetCurrentBehavior("Dead");
            return NodeState.Success;
        }, "Stop and Turn Red"));
        rootSelector.Attach(healthCheck);

        // 2. Out of Screen Check - now with tolerance
        var outOfScreenCheck = new SequenceNode("Out of Screen Check");
        outOfScreenCheck.Attach(new ConditionalNode(() => {
            // Check if pursuing a fly outside the screen
            bool pursuingOutsideFly = bestFly != null && 
                IsOutOfScreen(bestFly.transform, true) && // Strict check for the fly
                !IsOutOfScreen(transform); // Tolerance check for frog
            
            // Only trigger screen boundary return if:
            // 1. The frog is beyond tolerance OR has been outside too long
            // 2. AND it's not actively pursuing a fly just outside the screen
            return IsOutOfScreen(transform) && !pursuingOutsideFly;
        }, "Beyond Screen Boundary"));
        outOfScreenCheck.Attach(new ActionNode(() => {
            Vector2 centerTarget = GetSmoothedTarget(Vector2.zero);
            frog.SetTargetPositionPublic(centerTarget);
            SetCurrentBehavior("Return to Screen");
            return NodeState.Success;
        }, "Return to Screen"));
        rootSelector.Attach(outOfScreenCheck);

        // 3. Multiple Snakes Check - freeze when multiple threats
        var multipleSnakesCheck = new SequenceNode("Multiple Snakes Check");
        multipleSnakesCheck.Attach(new ConditionalNode(() => {
            FindNearbySnakes();
            return nearbySnakes.Count >= 2;
        }, "Multiple Snakes"));
        multipleSnakesCheck.Attach(new ActionNode(() => {
            // Just freeze in place
            SetCurrentBehavior("Freeze (Multiple Snakes)");
            return NodeState.Success;
        }, "Freeze"));
        rootSelector.Attach(multipleSnakesCheck);

        // 4. Single Snake Avoidance - NOW AVOIDS ALL SNAKES REGARDLESS OF STATE
        var singleSnakeCheck = new SequenceNode("Single Snake Check");
        singleSnakeCheck.Attach(new ConditionalNode(() => {
            FindNearbySnakes();
            return nearbySnakes.Count == 1 && 
                   Vector2.Distance(transform.position, nearbySnakes[0].transform.position) < scaredRange;
        }, "Single Snake Nearby"));
        singleSnakeCheck.Attach(new ActionNode(() => {
            Vector2 fleeTarget = CalculateFleePosition();
            Vector2 smoothedTarget = GetSmoothedTarget(fleeTarget);
            frog.SetTargetPositionPublic(smoothedTarget);
            
            // Try to shoot bubble at the snake if possible - but only in aggressive states
            if (nearbySnakes[0].State == Snake.SnakeState.Attack || nearbySnakes[0].State == Snake.SnakeState.PatrolAway || nearbySnakes[0].State == Snake.SnakeState.PatrolHome)
            {
                ShootBubbleAtSnake(nearbySnakes[0]);
            }
            
            SetCurrentBehavior("Flee from Snake");
            return NodeState.Success;
        }, "Flee from Snake"));
        rootSelector.Attach(singleSnakeCheck);

        // 5. Fly Hunting - using utility-based scoring system
        var flyHuntingCheck = new SequenceNode("Fly Hunting Check");
        flyHuntingCheck.Attach(new ConditionalNode(() => {
            EvaluateAllFlies();
            return bestFly != null;
        }, "Found Valuable Fly"));
        flyHuntingCheck.Attach(new ActionNode(() => {
            // Calculate predicted position with velocity
            Vector2 predictedPosition = PredictFlyPosition(bestFly);
            Vector2 smoothedTarget = GetSmoothedTarget(predictedPosition);
            frog.SetTargetPositionPublic(smoothedTarget);
            
            // Check if any nearby attack snakes need to be shot at
            foreach (Snake snake in nearbySnakes)
            {
                // Only shoot at attacking snakes, not when they're in benign mode
                if (snake.State == Snake.SnakeState.Attack && 
                    Vector2.Distance(transform.position, snake.transform.position) < bubbleRange)
                {
                    ShootBubbleAtSnake(snake);
                    break; // Only shoot at one snake per update
                }
            }
            
            SetCurrentBehavior("Hunting Fly");
            return NodeState.Success;
        }, "Move to Best Fly"));
        rootSelector.Attach(flyHuntingCheck);

        // 6. Default: Move to Center with gentle wandering
        var defaultMove = new ActionNode(() => {
            // Add slight wandering when at center to make it more natural
            Vector2 targetPos;
            float distToCenter = Vector2.Distance(transform.position, Vector2.zero);
            
            if (distToCenter < 1.5f)
            {
                // If near center, add gentle wandering
                float wanderRadius = 3.0f;
                float wanderNoise = Mathf.PerlinNoise(Time.time * 0.1f, 0) * 2.0f - 1.0f;
                float wanderAngle = wanderNoise * 180f;
                Vector2 wanderOffset = SteeringCalcs.Steering.rotate(Vector2.right, wanderAngle) * wanderRadius;
                targetPos = wanderOffset;
            }
            else
            {
                // Move toward center
                targetPos = Vector2.zero;
            }
            
            Vector2 smoothedTarget = GetSmoothedTarget(targetPos);
            frog.SetTargetPositionPublic(smoothedTarget);
            SetCurrentBehavior("Center Wandering");
            return NodeState.Success;
        }, "Default Move to Center");
        rootSelector.Attach(defaultMove);

        behaviorTree = new BehaviorTree(rootSelector, showDebugInfo);
    }
    
    // Check if the user has set a target position
    private void SetCurrentBehavior(string behaviorName)
    {
        if (behaviorName != currentBehaviorName)
        {
            previousMoveTarget = currentMoveTarget;
            transitionStartTime = Time.time;
            currentBehaviorName = behaviorName;
        }
    }
    
    // Check if the user has set a target position
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

    //Find all nearby snakes and store them in a list
    private void FindNearbySnakes()
    {
        nearbySnakes.Clear();
        Snake[] allSnakes = FindObjectsByType<Snake>(FindObjectsSortMode.None);
        
        // Find all snakes within scared range
        foreach (Snake snake in allSnakes)
        {
            float distanceToSnake = Vector2.Distance(transform.position, snake.transform.position);
            if (distanceToSnake < scaredRange * 1.5f) // Extend range slightly for awareness
            {
                nearbySnakes.Add(snake);
            }
        }
        
        // Sort by distance
        nearbySnakes.Sort((a, b) => 
            Vector2.Distance(transform.position, a.transform.position)
            .CompareTo(Vector2.Distance(transform.position, b.transform.position)));
        
        // Set closest snake for compatibility
        closestSnake = nearbySnakes.Count > 0 ? nearbySnakes[0] : null;
    }
    
    //Evaluate all flies in the scene and determine the best target
    private void EvaluateAllFlies()
    {
        // Only recalculate periodically to save performance
        if (Time.time - lastFlyEvaluationTime < flyEvaluationInterval)
        {
            return;
        }
        
        lastFlyEvaluationTime = Time.time;
        bestFly = null;
        float bestScore = -1f;
        flyScores.Clear();

        Fly[] allFlies = FindObjectsByType<Fly>(FindObjectsSortMode.None);
        
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
        
        // If there are flies in the grid, prioritize them strongly
        bool preferInGridFlies = fliesInGrid > 0 && frog.CurrentMovementType == Frog.MovementType.AStar;
        
        // Calculate center of mass of all flies (for group behavior consideration)
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
        
        // Evaluate each fly
        foreach (Fly fly in allFlies)
        {
            if (fly.State != Fly.FlyState.Dead)
            {
                // Calculate predicted position based on velocity
                Vector2 predictedPosition = PredictFlyPosition(fly);
                float distanceToFly = Vector2.Distance(transform.position, predictedPosition);
                
                // Skip if too far
                if (distanceToFly > huntRange * 2f)
                {
                    continue;
                }
                
                // Skip flies near snakes to avoid accidental collisions
                bool nearSnake = false;
                foreach (Snake snake in nearbySnakes)
                {
                    float snakeToFlyDistance = Vector2.Distance(predictedPosition, snake.transform.position);
                    if (snakeToFlyDistance < 2.0f) // Buffer distance to prevent chasing flies too close to snakes
                    {
                        nearSnake = true;
                        break;
                    }
                }
                
                // Skip flies that are too close to any snake
                if (nearSnake)
                {
                    continue;
                }
                
                // Check if fly is in grid - apply major scoring adjustments based on this
                bool isInGrid = IsPointInGrid(predictedPosition);
                
                // Calculate safety score (distance to nearest snake)
                float safetyScore = CalculateSafetyScore(predictedPosition);
                
                // Calculate terrain score
                float terrainScore = CalculateTerrainScore(predictedPosition);
                
                // Calculate center influence (prefer flies near the center of mass)
                float centerScore = CalculateCenterScore(predictedPosition, centerOfMass);
                
                // Combine scores with weights
                float totalScore = 
                    (1f - Mathf.Clamp01(distanceToFly / huntRange)) * distanceWeight + 
                    safetyScore * safetyWeight + 
                    terrainScore * terrainWeight + 
                    centerScore * centerWeight;
                
                // Grid position factor - much stronger prioritization
                if (frog.CurrentMovementType == Frog.MovementType.AStar)
                {
                    if (!isInGrid)
                    {
                        // Apply stronger penalty when using A* pathfinding
                        if (preferInGridFlies)
                        {
                            // If there are flies inside the grid, heavily penalize outside flies
                            totalScore *= 0.2f; // Much stronger penalty (was 0.6f)
                        }
                        else
                        {
                            // If no flies in grid, still penalize but less severely
                            totalScore *= 0.7f;
                        }
                    }
                    else
                    {
                        // Bonus for being in grid when using A*
                        totalScore *= 1.25f;
                    }
                }
                
                // Store score
                flyScores[fly] = totalScore;
                
                // Store grid status in blackboard for debugging
                blackboard.SetValueForType<Fly, bool>(fly.GetInstanceID().ToString() + "_inGrid", isInGrid);
                
                // Check if this is the best fly
                if (totalScore > bestScore && totalScore > 0.3f) // Minimum threshold to be considered
                {
                    bestScore = totalScore;
                    bestFly = fly;
                }
            }
        }
        
        // If we have a current target, only switch if the new one is significantly better
        if (blackboard.TryGetValue<Fly>("currentTarget", out Fly currentTarget) && 
            currentTarget != null && 
            currentTarget.State != Fly.FlyState.Dead)
        {
            if (bestFly != currentTarget)
            {
                // Only switch if new target is significantly better
                float currentScore = flyScores.ContainsKey(currentTarget) ? flyScores[currentTarget] : 0f;
                if (bestScore - currentScore < targetSwitchThreshold)
                {
                    bestFly = currentTarget; // Stick with current target
                }
            }
        }
        
        // Store current target in blackboard
        blackboard.SetValue("currentTarget", bestFly);
    }
    
    private float CalculateSafetyScore(Vector2 position)
    {
        float safetyScore = 1.0f; // Default to perfectly safe
        
        foreach (Snake snake in nearbySnakes)
        {
            float distToSnake = Vector2.Distance(position, snake.transform.position);
            
            // Create a larger danger radius (3.0 was previous value)
            float dangerRadius = scaredRange * 1.8f; // Increased to be more cautious
            float snakeDanger = 1.0f - Mathf.Clamp01(distToSnake / dangerRadius);
            
            // Apply stronger penalty for ALL snakes, with extra for attack mode
            if (snake.State == Snake.SnakeState.Attack)
            {
                snakeDanger *= 1.8f; // Increased from 1.5f
            }
            else if (snake.State == Snake.SnakeState.Fleeing)
            {
                snakeDanger *= 1.2f; // Still dangerous but less than attack mode
            }
            else
            {
                snakeDanger *= 1.0f; // Normal danger for other states
            }
            
            // Increase penalty based on snake's speed
            Rigidbody2D snakeRb = snake.GetComponent<Rigidbody2D>();
            if (snakeRb != null)
            {
                float speedFactor = snakeRb.linearVelocity.magnitude / 5.0f; // Normalize to typical max speed
                snakeDanger *= (1.0f + speedFactor * 0.5f); // More speed = more danger
            }
            
            safetyScore = Mathf.Min(safetyScore, 1.0f - snakeDanger);
        }
        
        return safetyScore;
    }
    
    private float CalculateTerrainScore(Vector2 position)
    {
        float terrainScore = 1.0f; // Default for normal terrain
        
        if (_astarGrid != null && IsPointInGrid(position))
        {
            Node positionNode = _astarGrid.NodeFromWorldPoint(position);
            if (positionNode != null)
            {
                switch (positionNode.terrainType)
                {
                    case TerrainType.Water:
                        terrainScore = 0.7f; // Slower in water
                        break;
                    case TerrainType.Sand:
                        terrainScore = 0.8f; // Slightly slower in sand
                        break;
                    case TerrainType.Mud:
                        terrainScore = 0.0f; // Completely avoided - absolute top priority to avoid
                        break;
                }
            }
        }
        
        return terrainScore;
    }
    
    private float CalculateCenterScore(Vector2 position, Vector2 centerOfMass)
    {
        if (centerOfMass == Vector2.zero)
        {
            // Regular center score - proximity to game center
            float distanceFromCenter = position.magnitude;
            return 1.0f - Mathf.Clamp01(distanceFromCenter / 10f); // 10 is the assumed map radius
        }
        else
        {
            // Score based on proximity to the fly center of mass
            float distanceFromCenter = Vector2.Distance(position, centerOfMass);
            return 1.0f - Mathf.Clamp01(distanceFromCenter / huntRange);
        }
    }
    
    private Vector2 PredictFlyPosition(Fly fly)
    {
        Vector2 predictedPosition = (Vector2)fly.transform.position;
        Rigidbody2D flyRb = fly.GetComponent<Rigidbody2D>();
        
        if (flyRb != null)
        {
            // Calculate intercept time based on relative velocities
            Vector2 relativePosition = predictedPosition - (Vector2)transform.position;
            Vector2 relativeVelocity = flyRb.linearVelocity - (_rb != null ? _rb.linearVelocity : Vector2.zero);
            float flySpeed = flyRb.linearVelocity.magnitude;
            float mySpeed = _rb != null ? _rb.linearVelocity.magnitude : 0f;
            
            // Predict future position with more accuracy for faster flies
            float predictionTime;
            if (relativeVelocity.magnitude > 0.1f)
            {
                // Dynamic prediction time based on speeds and distance
                float distance = relativePosition.magnitude;
                predictionTime = Mathf.Clamp(distance / (mySpeed + 0.1f), 0.1f, 1.0f);
            }
            else
            {
                predictionTime = 0.5f; // Default prediction time
            }
            
            // Apply prediction
            predictedPosition += flyRb.linearVelocity * predictionTime;
        }
        
        return predictedPosition;
    }
    
    private Vector2 CalculateFleePosition()
    {
        if (nearbySnakes.Count == 0)
        {
            return Vector2.zero;
        }
        
        // Calculate weighted flee direction away from all nearby snakes
        Vector2 fleeDirection = Vector2.zero;
        
        foreach (Snake snake in nearbySnakes)
        {
            float distToSnake = Vector2.Distance(transform.position, snake.transform.position);
            if (distToSnake < scaredRange)
            {
                // Calculate direction away from this snake
                Vector2 awayDir = ((Vector2)transform.position - (Vector2)snake.transform.position).normalized;
                
                // Weight by danger (closer snakes have more influence)
                float weight = 1.0f - (distToSnake / scaredRange);
                
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
            fleeTarget.x = Mathf.Clamp(fleeTarget.x, -anchorDims.x, anchorDims.x);
            fleeTarget.y = Mathf.Clamp(fleeTarget.y, -anchorDims.y, anchorDims.y);
            
            // Try to avoid dangerous terrain when fleeing
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
                        
                        testTarget.x = Mathf.Clamp(testTarget.x, -anchorDims.x, anchorDims.x);
                        testTarget.y = Mathf.Clamp(testTarget.y, -anchorDims.y, anchorDims.y);
                        
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
    
    private void ShootBubbleAtSnake(Snake snake)
    {
        // Only shoot at attacking snakes, not benign or fleeing ones
        if (snake.State == Snake.SnakeState.Benign || snake.State == Snake.SnakeState.Fleeing)
        {
            return;
        }
        
        if (Time.time - lastBubbleTime < bubbleCooldown)
        {
            return;
        }
        
        float distanceToSnake = Vector2.Distance(transform.position, snake.transform.position);
        if (distanceToSnake <= bubbleRange)
        {
            // Predict snake movement
            Rigidbody2D snakeRb = snake.GetComponent<Rigidbody2D>();
            Vector2 snakePos = snake.transform.position;
            
            if (snakeRb != null && snakeRb.linearVelocity.magnitude > 0.5f)
            {
                // Lead the target based on distance
                float leadTime = distanceToSnake / 10f; // Adjust the divisor based on bubble speed
                snakePos += snakeRb.linearVelocity * leadTime;
            }
            
            // Directly shoot bubble instead of using SendMessage
            GameObject bubblePrefab = frog.BubblePrefab;
            if (bubblePrefab != null)
            {
                // Calculate direction to target
                Vector2 direction = (snakePos - (Vector2)transform.position).normalized;
                
                // Calculate spawn position (in front of the frog's mouth)
                Vector2 spawnPosition = (Vector2)transform.position + direction * frog.BubbleOffset;
                
                // Calculate rotation towards target
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
                Quaternion rotation = Quaternion.Euler(0, 0, angle);
                
                // Instantiate the bubble
                GameObject bubble = Instantiate(bubblePrefab, spawnPosition, rotation);
                
                // Try to get and configure the Bubble component
                Bubble bubbleScript = bubble.GetComponent<Bubble>();
                if (bubbleScript != null)
                {
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
                
                lastBubbleTime = Time.time;
                Debug.Log("Shot bubble at attacking snake at distance: " + distanceToSnake);
            }
        }
    }

    private bool IsOutOfScreen(Transform transform, bool useStrictBoundary = false)
    {
        Vector2 screenPos = Camera.main.WorldToViewportPoint(transform.position);
        
        // Update tracking of time spent outside screen
        bool isOutside = screenPos.x < 0 || screenPos.x > 1 || screenPos.y < 0 || screenPos.y > 1;
        
        if (isOutside)
        {
            if (!wasOutsideScreen)
            {
                // Just went outside
                wasOutsideScreen = true;
                outsideScreenTimer = 0f;
            }
            else
            {
                // Continue being outside
                outsideScreenTimer += Time.deltaTime;
            }
        }
        else
        {
            // Back inside screen
            wasOutsideScreen = false;
            outsideScreenTimer = 0f;
        }
        
        // If strict boundary check is requested, use the original boundary check
        if (useStrictBoundary || !allowExploreOutsideScreen)
        {
            return isOutside;
        }
        
        // With tolerance enabled, we only consider it "out of screen" if:
        // 1. It's beyond the tolerance boundary, OR
        // 2. It's been outside too long
        float minX = -screenBoundaryTolerance;
        float maxX = 1 + screenBoundaryTolerance;
        float minY = -screenBoundaryTolerance;
        float maxY = 1 + screenBoundaryTolerance;
        
        bool beyondTolerance = screenPos.x < minX || screenPos.x > maxX || 
                              screenPos.y < minY || screenPos.y > maxY;
        
        bool outsideTooLong = outsideScreenTimer > maxTimeOutsideScreen;
        
        return beyondTolerance || outsideTooLong;
    }
    
    private bool IsPointInGrid(Vector2 point)
    {
        if (_astarGrid == null)
        {
            return true;
        }
        
        // Check if the point is within the grid bounds
        Vector2 gridWorldSize = _astarGrid.gridWorldSize;
        Vector2 gridCenter = (Vector2)_astarGrid.transform.position;
        Vector2 gridMin = gridCenter - gridWorldSize / 2;
        Vector2 gridMax = gridCenter + gridWorldSize / 2;
        
        return point.x >= gridMin.x && point.x <= gridMax.x && 
               point.y >= gridMin.y && point.y <= gridMax.y;
    }

    private void UpdateDebugVisuals()
    {
        if (!showDebugInfo)
        {
            return;
        }
        
        // Draw lines to all flies with different colors based on grid status
        Fly[] allFlies = FindObjectsByType<Fly>(FindObjectsSortMode.None);
        foreach (Fly fly in allFlies)
        {
            if (fly.State != Fly.FlyState.Dead)
            {
                bool isInGrid = false;
                blackboard.TryGetValueForType<Fly, bool>(fly.GetInstanceID().ToString() + "_inGrid", out isInGrid);
                
                // Draw different line colors based on whether flies are in grid
                Color lineColor = isInGrid ? new Color(0, 0.8f, 0, 0.3f) : new Color(0.8f, 0, 0, 0.3f);
                Debug.DrawLine(transform.position, fly.transform.position, lineColor);
                
                // Draw a circle around in-grid flies to make them more visible
                if (isInGrid && frog.CurrentMovementType == Frog.MovementType.AStar)
                {
                    Vector3 flyPos = fly.transform.position;
                    float radius = 0.5f;
                    int segments = 8;
                    Vector3 prevPoint = flyPos + new Vector3(radius, 0, 0);
                    
                    for (int i = 1; i <= segments; i++)
                    {
                        float angle = i * 2 * Mathf.PI / segments;
                        Vector3 newPoint = flyPos + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0);
                        Debug.DrawLine(prevPoint, newPoint, Color.cyan);
                        prevPoint = newPoint;
                    }
                }
            }
        }
        
        // Draw line to best fly
        if (bestFly != null)
        {
            Debug.DrawLine(transform.position, bestFly.transform.position, Color.green);
            
            // Show score if available - using Debug.DrawLine instead of GUI text
            if (flyScores.ContainsKey(bestFly))
            {
                // Draw a small marker instead of text
                Vector3 flyPos = bestFly.transform.position;
                float score = flyScores[bestFly];
                Color scoreColor = GetColorForScore(score);
                Debug.DrawLine(flyPos, flyPos + Vector3.up * 0.5f, scoreColor);
                Debug.DrawLine(flyPos + Vector3.up * 0.5f, flyPos + Vector3.up * 0.5f + Vector3.right * 0.3f, scoreColor);
            }
            
            // Show if best fly is in grid or out of grid
            bool bestFlyInGrid = false;
            blackboard.TryGetValueForType<Fly, bool>(bestFly.GetInstanceID().ToString() + "_inGrid", out bestFlyInGrid);
            string statusText = bestFlyInGrid ? "IN GRID" : "OUT OF GRID";
            
            // If best fly is out of grid but we're using A*, draw a warning indicator
            if (!bestFlyInGrid && frog.CurrentMovementType == Frog.MovementType.AStar)
            {
                // Draw an X shape at the target to indicate potential navigation issues
                Vector3 flyPos = bestFly.transform.position;
                float size = 0.7f;
                Debug.DrawLine(flyPos + new Vector3(-size, -size, 0), flyPos + new Vector3(size, size, 0), Color.red);
                Debug.DrawLine(flyPos + new Vector3(-size, size, 0), flyPos + new Vector3(size, -size, 0), Color.red);
            }
        }
        
        // Draw lines to nearby snakes
        foreach (Snake snake in nearbySnakes)
        {
            Color lineColor = snake.State == Snake.SnakeState.Attack ? Color.red : Color.yellow;
            Debug.DrawLine(transform.position, snake.transform.position, lineColor);
        }
        
        // Draw current behavior indicator
        // Instead of text, use color-coded rays to show current behavior
        switch (currentBehaviorName)
        {
            case "Dead":
                Debug.DrawRay(transform.position, Vector3.up * 0.7f, Color.red);
                break;
            case "Return to Screen":
                Debug.DrawRay(transform.position, Vector3.up * 0.7f, Color.cyan);
                break;
            case "Freeze (Multiple Snakes)":
                Debug.DrawRay(transform.position, Vector3.up * 0.7f, Color.yellow);
                break;
            case "Flee from Snake":
                Debug.DrawRay(transform.position, Vector3.up * 0.7f, Color.magenta);
                break;
            case "Hunting Fly":
                Debug.DrawRay(transform.position, Vector3.up * 0.7f, Color.green);
                break;
            case "Center Wandering":
                Debug.DrawRay(transform.position, Vector3.up * 0.7f, Color.white);
                break;
            default:
                Debug.DrawRay(transform.position, Vector3.up * 0.7f, Color.grey);
                break;
        }
        
        // Visualize the grid boundary
        if (_astarGrid != null && frog.CurrentMovementType == Frog.MovementType.AStar)
        {
            Vector2 gridWorldSize = _astarGrid.gridWorldSize;
            Vector2 gridCenter = (Vector2)_astarGrid.transform.position;
            Vector2 gridMin = gridCenter - gridWorldSize / 2;
            Vector2 gridMax = gridCenter + gridWorldSize / 2;
            
            Debug.DrawLine(new Vector3(gridMin.x, gridMin.y, 0), new Vector3(gridMax.x, gridMin.y, 0), Color.blue);
            Debug.DrawLine(new Vector3(gridMax.x, gridMin.y, 0), new Vector3(gridMax.x, gridMax.y, 0), Color.blue);
            Debug.DrawLine(new Vector3(gridMax.x, gridMax.y, 0), new Vector3(gridMin.x, gridMax.y, 0), Color.blue);
            Debug.DrawLine(new Vector3(gridMin.x, gridMax.y, 0), new Vector3(gridMin.x, gridMin.y, 0), Color.blue);
        }
        
        // Visualize the screen boundaries with tolerance
        if (allowExploreOutsideScreen && showDebugInfo)
        {
            // Get camera boundaries in world space
            Camera cam = Camera.main;
            if (cam != null)
            {
                // Calculate standard screen edges
                Vector3 bottomLeft = cam.ViewportToWorldPoint(new Vector3(0, 0, cam.nearClipPlane));
                Vector3 topLeft = cam.ViewportToWorldPoint(new Vector3(0, 1, cam.nearClipPlane));
                Vector3 topRight = cam.ViewportToWorldPoint(new Vector3(1, 1, cam.nearClipPlane));
                Vector3 bottomRight = cam.ViewportToWorldPoint(new Vector3(1, 0, cam.nearClipPlane));
                
                // Draw standard screen boundary in white
                Debug.DrawLine(bottomLeft, topLeft, Color.white);
                Debug.DrawLine(topLeft, topRight, Color.white);
                Debug.DrawLine(topRight, bottomRight, Color.white);
                Debug.DrawLine(bottomRight, bottomLeft, Color.white);
                
                // Calculate extended boundaries with tolerance
                Vector3 extBottomLeft = cam.ViewportToWorldPoint(new Vector3(-screenBoundaryTolerance, -screenBoundaryTolerance, cam.nearClipPlane));
                Vector3 extTopLeft = cam.ViewportToWorldPoint(new Vector3(-screenBoundaryTolerance, 1+screenBoundaryTolerance, cam.nearClipPlane));
                Vector3 extTopRight = cam.ViewportToWorldPoint(new Vector3(1+screenBoundaryTolerance, 1+screenBoundaryTolerance, cam.nearClipPlane));
                Vector3 extBottomRight = cam.ViewportToWorldPoint(new Vector3(1+screenBoundaryTolerance, -screenBoundaryTolerance, cam.nearClipPlane));
                
                // Draw extended boundary in cyan (dashed effect by using short lines)
                Color extendedBoundaryColor = new Color(0, 1, 1, 0.5f);
                DrawDashedLine(extBottomLeft, extTopLeft, extendedBoundaryColor);
                DrawDashedLine(extTopLeft, extTopRight, extendedBoundaryColor);
                DrawDashedLine(extTopRight, extBottomRight, extendedBoundaryColor);
                DrawDashedLine(extBottomRight, extBottomLeft, extendedBoundaryColor);
                
                // Show time outside indication if outside
                if (wasOutsideScreen)
                {
                    float remainingTime = maxTimeOutsideScreen - outsideScreenTimer;
                    float timeRatio = remainingTime / maxTimeOutsideScreen;
                    Color timerColor = Color.Lerp(Color.red, Color.green, timeRatio);
                    
                    // Draw timer bar above frog when outside screen
                    Vector3 timerStart = transform.position + Vector3.up * 1.0f;
                    Vector3 timerEnd = timerStart + Vector3.right * timeRatio * 1.0f;
                    Debug.DrawLine(timerStart, timerEnd, timerColor, 0, false);
                }
            }
        }
    }
    
    private Color GetColorForScore(float score)
    {
        // Convert score (usually 0-1) to a color: red (low) to green (high)
        return Color.Lerp(Color.red, Color.green, Mathf.Clamp01(score));
    }
    
    // Helper method to draw dashed lines for visualization
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

    // Add this method to check if the frog has a user target
    private bool CheckForUserTarget()
    {
        // We need to access the frog's _lastClickPos but it's private
        // Instead, we can use a small hack with Transform positions
        
        // First, check if the flag is visible (Frog sets it visible on user clicks)
        Transform flag = GameObject.Find("Flag").transform;
        if (flag != null && flag.GetComponent<SpriteRenderer>().enabled)
        {
            // Get the flag position (which represents the user's target)
            Vector2 flagPosition = flag.position - new Vector3(0.55f, 0.55f, 0); // Adjust to match Frog.cs offset
            
            // If we have a new target position or we haven't saved it yet
            if (!_userTargetPosition.HasValue || Vector2.Distance(flagPosition, _userTargetPosition.Value) > 0.1f)
            {
                _userTargetPosition = flagPosition;
                _isFollowingUserTarget = true;
                return true;
            }
            
            // If we're already following a user target
            if (_isFollowingUserTarget)
            {
                // Check if we've reached the target
                float distanceToTarget = Vector2.Distance(transform.position, _userTargetPosition.Value);
                if (distanceToTarget <= 0.5f) // Similar to Constants.TARGET_REACHED_TOLERANCE
                {
                    _isFollowingUserTarget = false;
                    _userTargetPosition = null;
                    return false;
                }
                return true;
            }
        }
        else
        {
            // Flag is hidden, so we're not following a user target
            _isFollowingUserTarget = false;
            _userTargetPosition = null;
        }
        
        return false;
    }
} 