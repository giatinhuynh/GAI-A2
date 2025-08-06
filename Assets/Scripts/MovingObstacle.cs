using UnityEngine;

public class MovingObstacle : MonoBehaviour
{
    // Define movement pattern types
    public enum MovementPattern
    {
        UpDown,
        LeftRight,
        ForwardBackward,
        Circular,
        CustomDirection
    }

    // Inspector variables
    [Header("Movement Settings")]
    [Tooltip("The type of movement pattern")]
    public MovementPattern movementPattern = MovementPattern.UpDown;
    
    [Tooltip("How far the rock moves from its starting position")]
    public float moveDistance = 2.0f;
    
    [Tooltip("How fast the rock completes one full movement cycle")]
    public float moveSpeed = 1.0f;
    
    [Tooltip("Custom direction of movement (only used with CustomDirection pattern)")]
    public Vector2 customMoveDirection = Vector2.up;
    
    [Tooltip("Phase offset (0-1) to stagger movement of multiple objects")]
    [Range(0, 1)]
    public float phaseOffset = 0f;
    
    [Header("Circular Movement Settings")]
    [Tooltip("Radius of circular movement")]
    public float circleRadius = 2.0f;
    
    [Tooltip("Clockwise or counter-clockwise circular movement")]
    public bool clockwise = true;
    
    [Header("Physics Settings")]
    [Tooltip("Mass of the object (lower values push less)")]
    public float objectMass = 5f;
    
    [Tooltip("Linear drag to reduce pushing force")]
    public float objectDrag = 1f;
    
    [Tooltip("Whether to detect collisions with other moving obstacles")]
    public bool detectObstacleCollisions = true;
    
    [Header("Additional Effects")]
    [Tooltip("Whether the rock should rotate while moving")]
    public bool rotateWhileMoving = false;
    
    [Tooltip("Rotation speed in degrees per second")]
    public float rotationSpeed = 45.0f;
    
    [Header("Debug")]
    public bool showDebugGizmos = true;
    
    // Internal variables
    private Vector3 startPosition;
    private Rigidbody2D rb;
    private float timeOffset;
    private Vector2 moveDirection;
    private float pathProgress = 0f;
    
    // Layer mask for obstacle collision detection
    private LayerMask obstacleLayer;
    
    void Start()
    {
        // Store starting position
        startPosition = transform.position;
        
        // Setup movement direction based on pattern
        SetupMovementDirection();
        
        // Get or add rigidbody
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
        }
        
        // Configure rigidbody properties
        rb.gravityScale = 0f;
        rb.mass = objectMass;
        rb.linearDamping = objectDrag;
        rb.angularDamping = 0.5f;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        
        // Add random time offset for variety when multiple rocks are present
        timeOffset = phaseOffset * Mathf.PI * 2;
        
        // Setup obstacle layer for collision detection
        obstacleLayer = LayerMask.GetMask("Obstacles");
        if (obstacleLayer.value == 0)
        {
            // Fallback to default
            obstacleLayer = Physics2D.DefaultRaycastLayers;
        }
    }
    
    // Setup movement direction based on selected pattern
    void SetupMovementDirection()
    {
        switch (movementPattern)
        {
            case MovementPattern.UpDown:
                moveDirection = Vector2.up;
                break;
                
            case MovementPattern.LeftRight:
                moveDirection = Vector2.right;
                break;
                
            case MovementPattern.ForwardBackward:
                moveDirection = transform.up;
                break;
                
            case MovementPattern.CustomDirection:
                moveDirection = customMoveDirection.normalized;
                break;
                
            case MovementPattern.Circular:
                // No direction needed for circular pattern
                moveDirection = Vector2.right; // Initial direction
                break;
        }
    }

    // FixedUpdate for physics-based movement
    void FixedUpdate()
    {
        // Update path progress
        pathProgress = ((Time.time + timeOffset) * moveSpeed) % (2 * Mathf.PI);
        
        // Calculate new position based on movement pattern
        Vector3 targetPosition = CalculateTargetPosition();
        
        // Check for obstacle collisions if enabled
        if (detectObstacleCollisions)
        {
            Vector2 nextDirection = (targetPosition - transform.position).normalized;
            float moveDist = Vector2.Distance(transform.position, targetPosition);
            
            RaycastHit2D hit = Physics2D.CircleCast(
                transform.position, 
                0.5f, // Use half the typical size of the obstacle
                nextDirection, 
                moveDist,
                obstacleLayer
            );
            
            if (hit.collider != null && hit.collider.GetComponent<MovingObstacle>() != null)
            {
                // Found another moving obstacle in our path
                // Adjust target position to avoid collision
                targetPosition = Vector3.Lerp(transform.position, targetPosition, 0.5f);
            }
        }
        
        // Move using rigidbody for proper physics interactions
        rb.MovePosition(targetPosition);
        
        // Handle rotation if enabled
        if (rotateWhileMoving)
        {
            // Calculate rotation based on movement and pattern
            UpdateRotation();
        }
    }
    
    Vector3 CalculateTargetPosition()
    {
        switch (movementPattern)
        {
            case MovementPattern.Circular:
                return CalculateCircularPosition();
                
            case MovementPattern.UpDown:
            case MovementPattern.LeftRight:
            case MovementPattern.ForwardBackward:
            case MovementPattern.CustomDirection:
            default:
                // Linear oscillation using sine wave
                float offsetFactor = Mathf.Sin(pathProgress);
                return startPosition + (Vector3)(moveDirection * offsetFactor * moveDistance);
        }
    }
    
    Vector3 CalculateCircularPosition()
    {
        // Calculate position on a circle
        float angle = clockwise ? -pathProgress : pathProgress;
        float x = Mathf.Cos(angle) * circleRadius;
        float y = Mathf.Sin(angle) * circleRadius;
        
        return startPosition + new Vector3(x, y, 0);
    }
    
    void UpdateRotation()
    {
        float currentRotation = transform.rotation.eulerAngles.z;
        float rotationAmount = 0f;
        
        switch (movementPattern)
        {
            case MovementPattern.Circular:
                // Make rotation follow the circular path
                float targetAngle = clockwise ? 
                    (Mathf.Atan2(-Mathf.Cos(pathProgress), -Mathf.Sin(pathProgress)) * Mathf.Rad2Deg) : 
                    (Mathf.Atan2(Mathf.Cos(pathProgress), Mathf.Sin(pathProgress)) * Mathf.Rad2Deg);
                
                // Smooth rotation to target angle
                float angleDifference = Mathf.DeltaAngle(currentRotation, targetAngle);
                rotationAmount = Mathf.Sign(angleDifference) * Mathf.Min(Mathf.Abs(angleDifference), rotationSpeed * Time.deltaTime);
                break;
                
            default:
                // Simple oscillating rotation based on movement
                rotationAmount = rotationSpeed * Time.deltaTime * Mathf.Sign(Mathf.Cos(pathProgress));
                break;
        }
        
        transform.rotation = Quaternion.Euler(0, 0, currentRotation + rotationAmount);
    }
    
    // Draw movement path in editor
    private void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;
        
        if (Application.isPlaying)
        {
            // Show actual path in play mode
            DrawRuntimePath();
        }
        else
        {
            // Show predicted path in edit mode
            DrawEditModePath();
        }
    }
    
    private void DrawRuntimePath()
    {
        if (movementPattern == MovementPattern.Circular)
        {
            DrawCirclePath(startPosition, circleRadius);
        }
        else
        {
            Vector3 endPos = startPosition + (Vector3)(moveDirection * moveDistance);
            Vector3 startPos = startPosition - (Vector3)(moveDirection * moveDistance);
            
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(startPos, endPos);
            
            // Draw markers at endpoints
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(startPos, 0.1f);
            Gizmos.DrawSphere(endPos, 0.1f);
        }
        
        // Draw current position marker
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(transform.position, 0.15f);
    }

    // Draw path in edit mode
    private void DrawEditModePath()
    {
        // Setup direction based on selected pattern
        Vector2 direction;
        switch (movementPattern)
        {
            case MovementPattern.UpDown:
                direction = Vector2.up;
                break;
            case MovementPattern.LeftRight:
                direction = Vector2.right;
                break;
            case MovementPattern.ForwardBackward:
                direction = transform.up;
                break;
            case MovementPattern.CustomDirection:
                direction = customMoveDirection.normalized;
                break;
            case MovementPattern.Circular:
                DrawCirclePath(transform.position, circleRadius);
                return;
            default:
                direction = Vector2.up;
                break;
        }
        
        Vector3 endPos = transform.position + (Vector3)(direction * moveDistance);
        Vector3 startPos = transform.position - (Vector3)(direction * moveDistance);
        
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(startPos, endPos);
        
        // Draw markers at endpoints
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(startPos, 0.1f);
        Gizmos.DrawSphere(endPos, 0.1f);
    }
    
    // Draw circular path in editor
    private void DrawCirclePath(Vector3 center, float radius)
    {
        Gizmos.color = Color.yellow;
        
        // Draw circle using line segments
        int segments = 32;
        Vector3 prevPoint = center + new Vector3(radius, 0, 0);
        
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 newPoint = center + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0);
            Gizmos.DrawLine(prevPoint, newPoint);
            prevPoint = newPoint;
        }
        
        // Draw radius line as movement indicator
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(center, center + new Vector3(radius, 0, 0));
    }
    
    // Reset to original position when disabled
    private void OnDisable()
    {
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
        }
        transform.position = startPosition;
    }
    
    // Update movement pattern at runtime
    public void SetMovementPattern(MovementPattern newPattern)
    {
        movementPattern = newPattern;
        SetupMovementDirection();
    }
    
    // Get the current actual velocity for other scripts to use
    public Vector2 GetCurrentVelocity()
    {
        if (rb != null)
        {
            return rb.linearVelocity;
        }
        return Vector2.zero;
    }
} 