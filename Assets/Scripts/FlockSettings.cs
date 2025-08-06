using UnityEngine;

// Class to hold the parameters of the flocking characters (flies).
// This is attached to the "Flock" game object in the "FlockingTest"
// and "FullGame" scenes.
public class FlockSettings : MonoBehaviour
{
    public float MaxSpeed;
    public float MaxAccel;
    public float AccelTime;
    public float InitSpeedMin;
    public float InitSpeedMax;

    public float FlockRadius;
    public Vector2 AnchorDims;

    public float SeparationWeight;
    public float CohesionWeight;
    public float AlignmentWeight;

    public float AnchorWeight;
}
