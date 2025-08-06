using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SteeringCalcs;

// Controls the behavior of individual flies in the game
public class Fly : MonoBehaviour
{
    // Current FSM State, tracked publically so we can modify this (live) in Unity editor
    public FlyState State;

    // Parameters controlling transitions in/out of the Flee state based on proximity to frog/bubbles.
    public float StopFleeingRange;      // Distance beyond which the fly stops fleeing the frog.
    public float FrogStillFleeRange;    // Distance within which a still frog scares the fly.
    public float FrogMovingFleeRange;   // Distance within which a moving frog scares the fly.
    public float FrogAlertSpeed;        // Speed threshold for considering the frog as 'moving'.
    public float BubbleFleeRange;       // Distance within which a bubble scares the fly.

    // Time taken for the fly to respawn after being eaten by the frog.
    public float RespawnTime;

    // References to essential components and game objects in the scene.
    private Rigidbody2D _rb;            // Physics component for movement.
    private SpriteRenderer _sr;         // Component for rendering the fly sprite.
    private CircleCollider2D _collider; // Component for detecting collisions/triggers.
    private Transform _frog;            // Reference to the frog's transform (position, rotation, scale).
    private Rigidbody2D _frogRb;        // Reference to the frog's Rigidbody2D for velocity checks.

    // Reference to the flocking parameters (attached to the "Flock"
    // game object in the "FlockingTest" and "FullGame" scenes).
    private FlockSettings _settings;

    // FSM tracking properties
    // Time since eaten by the frog (0 when alive).
    private float _timeDead;

    // List tracking current neighbours (other flies) within flocking radius.
    List<Transform> _neighbours;
    
    // List of bubbles in the scene
    private List<GameObject> _bubblesInRange = new List<GameObject>();
    
    // Flag to prevent double counting when collecting the fly
    private bool _alreadyCollected = false;

    // Fly FSM states
    public enum FlyState : int
    {
        Flocking = 0,
        Alone = 1,
        Fleeing = 2,
        Dead = 3,
        Respawn = 4
    }

    // Fly FSM events
    public enum FlyEvent : int
    {
        JoinedFlock = 0,
        LostFlock = 1,
        ScaredByFrog = 2,
        EscapedFrog = 3,
        CaughtByFrog = 4,
        RespawnTimeElapsed = 5,
        NowAlive = 6,
        ScaredByBubble = 7
    }

    void Start()
    {
        _settings = transform.parent.GetComponent<FlockSettings>();
        _rb = GetComponent<Rigidbody2D>();
        _sr = GetComponent<SpriteRenderer>();
        
        // Get or add a collider and make sure it's a trigger
        _collider = GetComponent<CircleCollider2D>();
        if (_collider != null)
        {
            _collider.isTrigger = true; // Ensure it acts as a trigger for OnTriggerEnter events.
        }
        else
        {
            _collider = gameObject.AddComponent<CircleCollider2D>();
            _collider.isTrigger = true;
            _collider.radius = 0.3f;
        }

        // Have to be a bit careful setting _frog, since the frog doesn't exist in all scenes.
        GameObject frog = GameObject.Find("Frog");
        if (frog != null)
        {
            _frog = frog.transform;
            _frogRb = frog.GetComponent<Rigidbody2D>();
        }

        // Initial FSM variables
        _timeDead = 0.0f;
        _neighbours = new List<Transform>();
        _alreadyCollected = false;
    }

    void FixedUpdate()
    {
        // Update data needed in computing FSM transitions & states
        UpdateNeighbours();

        // Events triggered by each fixed update tick
        FixedUpdateEvents();

        // Update the Fly behaviour based on the current FSM state
        FSM_State();

        // Configure final appearance
        UpdateAppearance();
    }

    // Trigger Events for each fixed update tick, using an event first FSM implementation
    void FixedUpdateEvents()
    {
        // If the fly's been dead long enough, trigger respawn event
        if (State == FlyState.Dead)
        {
            _timeDead += Time.fixedDeltaTime;
            if (_timeDead > RespawnTime)
            {
                HandleEvent(FlyEvent.RespawnTimeElapsed);
            }
        }

        // Check triggers from flocking events
        // Note: These can be true each update but may not trigger a state transition.
        if (State == FlyState.Flocking ||
            State == FlyState.Fleeing ||
            State == FlyState.Alone)
        {
            if (_neighbours.Count == 0)
            {
                HandleEvent(FlyEvent.LostFlock);
            }
            else
            {
                HandleEvent(FlyEvent.JoinedFlock);
            }
        }

        // Check triggers related to the frog and bubbles, if the frog exists in the scene.
        if (_frog != null)
        {
            // Calculate distance and check if the frog is moving quickly.
            float distanceToFrog = (transform.position - _frog.transform.position).magnitude;
            bool frogIsMovingFast = _frogRb.linearVelocity.magnitude >= FrogAlertSpeed;
            
            // Check if we've been scared by the frog.
            if ((frogIsMovingFast && distanceToFrog < FrogMovingFleeRange) || 
                (!frogIsMovingFast && distanceToFrog < FrogStillFleeRange))
            {
                HandleEvent(FlyEvent.ScaredByFrog);
            }

            // Check if we no longer need to be scared
            bool safeFromFrog = distanceToFrog > StopFleeingRange;
            
            // Determine if the fly is currently safe from all nearby bubbles.
            bool safeFromBubbles = true;
            if (_bubblesInRange.Count > 0) // Only check if there are bubbles nearby.
            {
                // Clean up any null references
                _bubblesInRange.RemoveAll(item => item == null);
                
                // Check remaining bubbles
                foreach (GameObject bubble in _bubblesInRange)
                {
                    if (bubble != null)
                    {
                        float distanceToBubble = Vector2.Distance(transform.position, bubble.transform.position);
                        if (distanceToBubble <= BubbleFleeRange)
                        {
                            safeFromBubbles = false;
                            break;
                        }
                    }
                }
            }
            
            // Trigger the escape event only if the fly is safe from both the frog and all bubbles.
            if (safeFromFrog && safeFromBubbles && State == FlyState.Fleeing)
            {
                HandleEvent(FlyEvent.EscapedFrog); // Note: Event name implies frog, but covers bubbles too.
            }
        }
    }
    
    // Called by the Bubble script when a bubble comes within range
    public void OnBubbleInRange(GameObject bubble)
    {
        if (bubble != null && !_bubblesInRange.Contains(bubble))
        {
            _bubblesInRange.Add(bubble);
            HandleEvent(FlyEvent.ScaredByBubble);
        }
    }
    
    // Called when a bubble is destroyed or goes out of range
    public void OnBubbleOutOfRange(GameObject bubble)
    {
        if (bubble != null && _bubblesInRange.Contains(bubble))
        {
            _bubblesInRange.Remove(bubble);
        }
    }

    // Process the current FSM state, using an event first FSM implementation
    private void FSM_State()
    {
        // Common variables between states
        Vector2 desiredVel = Vector2.zero;

        if (State == FlyState.Dead)
        {
            // UpdateAppearance ensures the sprite render is off
            // Ensure dead fly stops moving
            desiredVel = Vector2.zero;
        }
        else if (State == FlyState.Respawn)
        {
            // Note this causes an immediate FSM transition
            Respawn();

            // Ensure initial zero velocity
            desiredVel = Vector2.zero;
        }
        else if (State == FlyState.Flocking)
        {
            Vector2 desiredSep = _settings.SeparationWeight * Steering.GetSeparation(transform.position, _neighbours, _settings.MaxSpeed);
            Vector2 desiredCoh = _settings.CohesionWeight * Steering.GetCohesion(transform.position, _neighbours, _settings.MaxSpeed);
            Vector2 desiredAli = _settings.AlignmentWeight * Steering.GetAlignment(_neighbours, _settings.MaxSpeed);
            Vector2 desiredAnch = _settings.AnchorWeight * Steering.GetAnchor(transform.position, _settings.AnchorDims);

            // Draw the forces for debugging purposes.
            Debug.DrawLine(transform.position, (Vector2)transform.position + desiredSep, Color.red);
            Debug.DrawLine(transform.position, (Vector2)transform.position + desiredCoh, Color.green);
            Debug.DrawLine(transform.position, (Vector2)transform.position + desiredAli, Color.blue);
            Debug.DrawLine(transform.position, (Vector2)transform.position + desiredAnch, Color.yellow);

            desiredVel = (desiredSep + desiredCoh + desiredAli + desiredAnch).normalized * _settings.MaxSpeed;
        }
        else if (State == FlyState.Alone)
        {
            Transform nearestFly = null;

            foreach (Transform flockMember in transform.parent)
            {
                if (flockMember.GetComponent<Fly>().State != FlyState.Dead && flockMember != transform)
                {
                    if (nearestFly == null || (transform.position - flockMember.position).magnitude < (transform.position - nearestFly.position).magnitude)
                    {
                        nearestFly = flockMember;
                    }
                }
            }

            if (nearestFly != null)
            {
                desiredVel = Steering.SeekDirect(transform.position, nearestFly.position, _settings.MaxSpeed);
                Debug.DrawLine(transform.position, nearestFly.position, Color.yellow);
            }
        }
        else if (State == FlyState.Fleeing)
        {
            // Calculate the direction directly away from the frog.
            Vector2 fleeDirection = ((Vector2)transform.position - (Vector2)_frog.position).normalized;
            
            // Calculate a potential flee target point some distance away in the flee direction.
            float fleeDistance = 5f; // How far to initially project the flee target.
            Vector2 fleeTarget = (Vector2)transform.position + fleeDirection * fleeDistance;
            
            // Ensure the flee target stays within reasonable game boundaries.
            float boundaryLimit = 15f; // Define the limits of the playable area.
            fleeTarget.x = Mathf.Clamp(fleeTarget.x, -boundaryLimit, boundaryLimit); // Clamp X coordinate.
            fleeTarget.y = Mathf.Clamp(fleeTarget.y, -boundaryLimit, boundaryLimit); // Clamp Y coordinate.
            
            // Use arrive behavior to flee to the constrained target
            desiredVel = Steering.ArriveDirect(transform.position, fleeTarget, 1f, _settings.MaxSpeed);
            
            // Fallback to direct flee if _frog is null
            if (_frog == null)
            {
                // Just use a random direction if we can't flee from the frog
                desiredVel = Random.insideUnitCircle.normalized * _settings.MaxSpeed;
            }
        }

        // Convert the desired velocity to a force, then apply it.
        // All states use this
        Vector2 steering = Steering.DesiredVelToForce(desiredVel, _rb, _settings.AccelTime, _settings.MaxAccel);
        _rb.AddForce(steering);
    }

    // Respawns the fly at a random position away from the origin
    private void Respawn()
    {
        // Respawn 20 units away from the origin at a random angle.
        // The flocking forces should automatically bring the fly back into the main arena.
        float randomAngle = Random.Range(-Mathf.PI, Mathf.PI);
        Vector2 randomDirection = new Vector2(Mathf.Cos(randomAngle), Mathf.Sin(randomAngle));
        transform.position = 20.0f * randomDirection;

        _timeDead = 0.0f;
        _alreadyCollected = false; // Reset the collected flag when respawning

        // Immediately trigger Respawn trigger
        HandleEvent(FlyEvent.NowAlive);
    }

    // UpdateNeighbours() sets all flock members that:
    // - Are not dead.
    // - Are not equal to this flock member.
    // - Are within a distance of _settings.FlockRadius from this flock member.
    private void UpdateNeighbours()
    {
        _neighbours.Clear();

        foreach (Transform flockMember in transform.parent)
        {
            if (flockMember.GetComponent<Fly>().State != FlyState.Dead
                && flockMember != transform && (transform.position - flockMember.position).magnitude < _settings.FlockRadius)
            {
                _neighbours.Add(flockMember);
            }
        }
    }

    // Set the Fly appearance based on it's movement and FSM state
    private void UpdateAppearance()
    {
        _sr.flipX = _rb.linearVelocity.x > 0;

        // Update color to provide a visual indication of the current state
        if (State == FlyState.Flocking)
        {
            _sr.enabled = true;
            _sr.color = new Color(1.0f, 1.0f, 1.0f);
        }
        else if (State == FlyState.Alone)
        {
            _sr.enabled = true;
            _sr.color = new Color(1.0f, 0.52f, 0.01f);
        }
        else if (State == FlyState.Fleeing)
        {
            _sr.enabled = true;
            _sr.color = new Color(0.45f, 0.98f, 0.94f);
        }
        else if (State == FlyState.Dead)
        {
            _sr.enabled = false;
        }
        else if (State == FlyState.Respawn)
        {
            _sr.enabled = false;
        }
    }

    // Handle collisions with the frog using either OnTriggerEnter2D or OnCollisionEnter2D
    private void OnTriggerEnter2D(Collider2D collider)
    {
        ProcessCollision(collider.gameObject);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        ProcessCollision(collision.gameObject);
    }

    // Handles collision processing with other game objects
    private void ProcessCollision(GameObject collisionObject)
    {
        // Process collision only if the fly is currently alive and hasn't already been collected in this 'life'.
        if (State != FlyState.Dead && !_alreadyCollected && collisionObject.CompareTag("Frog"))
        {
            Debug.Log(name + " caught by frog!");
            
            // Set flag to prevent double counting
            _alreadyCollected = true;
            
            // Notify the frog that it caught a fly
            Frog frog = collisionObject.GetComponent<Frog>();
            if (frog != null)
            {
                // Debug - print the MaxFlies value to verify it's 10
                Debug.Log("DEBUG: Frog.MaxFlies = " + Frog.MaxFlies);
                
                // Only increment the fly count if less than 10 flies
                // Using explicit '10' here, matching the win condition check below.
                if (frog.FliesCollected < 10) // Check if the frog can collect more flies.
                {
                    // Increment the frog's fly count.
                    frog.FliesCollected++;
                    Debug.Log("Frog's fly count increased to: " + frog.FliesCollected);
                    
                    // Check if the frog has won - EXACTLY at 10 flies 
                    if (frog.FliesCollected == 10)
                    {
                        Debug.Log("Frog has collected exactly 10 flies! WIN CONDITION!");
                        frog.ShowGameOver(true);
                    }
                    
                    // Transition FSM to dead state
                    HandleEvent(FlyEvent.CaughtByFrog);
                }
                else
                {
                    Debug.Log("Max flies already collected. Cannot collect more.");
                }
            }
        }
    }

    // Updates the current FSM state
    private void SetState(FlyState newState)
    {
        if (newState != State)
        {
            // Can uncomment this for debugging purposes.
            // Debug.Log(name + " switching state to " + newState.ToString());

            State = newState;
        }
    }

    // HandleEvent implements the transition logic of the FSM.
    //      This can be called with invalid transitions - so check first!
    private void HandleEvent(FlyEvent e)
    {
        //Debug.Log(name + " handling event " + e.ToString());

        // FSM Hierarchy
        if (State == FlyState.Dead)
        {
            if (e == FlyEvent.RespawnTimeElapsed)
            {
                SetState(FlyState.Respawn);
            }
        }
        else if (State == FlyState.Respawn)
        {
            if (e == FlyEvent.NowAlive)
            {
                SetState(FlyState.Flocking);
            }
        }
        // Second Hierarchy Layer
        else
        {
            // All can transition to Dead
            if (e == FlyEvent.CaughtByFrog)
            {
                SetState(FlyState.Dead);
            }

            // Otherwise cheack each state transition
            else if (State == FlyState.Flocking)
            {
                if (e == FlyEvent.LostFlock)
                {
                    SetState(FlyState.Alone);
                }
                else if (e == FlyEvent.ScaredByFrog || e == FlyEvent.ScaredByBubble)
                {
                    SetState(FlyState.Fleeing);
                }
            }
            else if (State == FlyState.Alone)
            {
                if (e == FlyEvent.JoinedFlock)
                {
                    SetState(FlyState.Flocking);
                }
                else if (e == FlyEvent.ScaredByFrog || e == FlyEvent.ScaredByBubble)
                {
                    SetState(FlyState.Fleeing);
                }
            }
            else if (State == FlyState.Fleeing)
            {
                if (e == FlyEvent.EscapedFrog)
                {
                    SetState(FlyState.Flocking);
                }
            }
        }
    }
}
