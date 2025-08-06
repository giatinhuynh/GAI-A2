using UnityEngine;
using System.Collections.Generic;

public class Bubble : MonoBehaviour
{
    // Bubble properties
    public float Speed = 5f;
    public float LifeTime = 2f; // How long the bubble exists before disappearing

    // Reference to the frog for positioning
    private Transform _frog;

    // List of flies currently within the bubble's flee notification range
    private List<Fly> _fliesInRange = new List<Fly>();

    void Awake()
    {
        // Ensure we have non-zero values for Speed and LifeTime
        if (Speed <= 0)
            Speed = 5f;
        if (LifeTime <= 0)
            LifeTime = 2f;
    }

    void Start()
    {
        // Set the bubble to destroy itself after lifetime
        Destroy(gameObject, LifeTime);
        
        // Find the frog
        if (_frog == null)
        {
            GameObject frogObj = GameObject.Find("Frog");
            if (frogObj != null)
                _frog = frogObj.transform;
        }
    }

    void FixedUpdate()
    {
        // Move the bubble forward based on the object's rotation
        transform.Translate(Vector3.up * Speed * Time.fixedDeltaTime);
        
        // Check for flies in range
        CheckFliesInRange();
    }

    // Check for flies within their flee range and notify them
    void CheckFliesInRange()
    {
        // Find all flies in the scene
        Fly[] allFlies = FindObjectsByType<Fly>(FindObjectsSortMode.None);
        
        if (allFlies == null || allFlies.Length == 0)
            return;
        
        foreach (Fly fly in allFlies)
        {
            if (fly == null)
                continue;

            // Calculate distance between bubble and fly
            float distance = Vector2.Distance(transform.position, fly.transform.position);

            // If fly is within range, add it to the list and notify it
            if (distance <= fly.BubbleFleeRange && !_fliesInRange.Contains(fly))
            {
                _fliesInRange.Add(fly);
                fly.SendMessage("OnBubbleInRange", gameObject, SendMessageOptions.DontRequireReceiver);
            }
            // If fly is no longer in range, remove it from the list
            else if (distance > fly.BubbleFleeRange && _fliesInRange.Contains(fly))
            {
                _fliesInRange.Remove(fly);
                fly.SendMessage("OnBubbleOutOfRange", gameObject, SendMessageOptions.DontRequireReceiver);
            }
        }
    }

    // Handle collisions with trigger colliders (e.g., sensors)
    void OnTriggerEnter2D(Collider2D collision)
    {
        ProcessCollision(collision.gameObject);
    }
    
    // Handle collisions with non-trigger colliders
    void OnCollisionEnter2D(Collision2D collision)
    {
        ProcessCollision(collision.gameObject);
    }

    // Common logic to process collision with any game object
    void ProcessCollision(GameObject collisionObject)
    {
        // If collided with a snake
        if (collisionObject.CompareTag("Snake"))
        {
            // Notify the snake
            collisionObject.SendMessage("OnBubbleHit", SendMessageOptions.DontRequireReceiver);
            
            // Destroy the bubble
            Destroy(gameObject);
        }
        // If collided with an obstacle
        else if (collisionObject.layer == LayerMask.NameToLayer("Obstacle"))
        {
            // Destroy the bubble
            Destroy(gameObject);
        }
    }

    // Clean up when the bubble is destroyed
    void OnDestroy()
    {
        // Notify all flies in range that the bubble is gone
        foreach (Fly fly in _fliesInRange)
        {
            if (fly != null)
            {
                fly.SendMessage("OnBubbleOutOfRange", gameObject, SendMessageOptions.DontRequireReceiver);
            }
        }
    }
} 