namespace Globals
{
    public class Constants
    {
        // Velocity threshold for the frog and snakes' animations to update.
        public const float MIN_SPEED_TO_ANIMATE = 1.0f;

        // The distance at which a target is considered to be reached.
        // For the frog, target = flag position after right-clicking.
        // For the snakes, the target depends on the current FSM state.
        public const float TARGET_REACHED_TOLERANCE = 1.0f;
    }
}
