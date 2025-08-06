using System.Collections.Generic;
using UnityEngine;

namespace SteeringCalcs
{ 
    [System.Serializable]
    public class AvoidanceParams
    {
        public bool Enable;
        public LayerMask ObstacleMask;

        public float MaxCastLength;
        public float CircleCastRadius;
        public float AngleIncrement;
    }

    public class Steering
    {
        // PLEASE NOTE:
        // You do not need to edit any of the methods in the HelperMethods region.
        // In Visual Studio, you can collapse the HelperMethods region by clicking the "-" to the left.
        #region HelperMethods

        // Helper method for rotating a vector by an angle (in degrees).
        public static Vector2 rotate(Vector2 v, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;

            return new Vector2(
                v.x * Mathf.Cos(radians) - v.y * Mathf.Sin(radians),
                v.x * Mathf.Sin(radians) + v.y * Mathf.Cos(radians)
            );
        }

        // Converts a desired velocity into a steering force,
        // as will be explained in class (Week 2).
        public static Vector2 DesiredVelToForce(Vector2 desiredVel, Rigidbody2D rb, float accelTime, float maxAccel)
        {
            Vector2 accel = (desiredVel - rb.linearVelocity) / accelTime;

            if (accel.magnitude > maxAccel)
            {
                accel = accel.normalized * maxAccel;
            }

            // F = ma
            return rb.mass * accel;
        }

        // In addition to separation, cohesion and alignment, the flies also have
        // an "anchor" force applied to them while flocking, to keep them within the game arena.
        // This is already implemented for you.
        public static Vector2 GetAnchor(Vector2 currentPos, Vector2 anchorDims)
        {
            Vector2 desiredVel = Vector2.zero;

            if (Mathf.Abs(currentPos.x) > anchorDims.x)
            {
                desiredVel -= new Vector2(currentPos.x, 0.0f);
            }

            if (Mathf.Abs(currentPos.y) > anchorDims.y)
            {
                desiredVel -= new Vector2(0.0f, currentPos.y);
            }

            return desiredVel;
        }

        #endregion

        // These are "parent" steering methods that toggle between obstacle avoidance (XAndAvoid)
        // and no avoidance "XDirect" of each steering behaviour.
        // The avoid methods use GetAvoidanceTarget, to find an target position.
        // You will need to implement GetAvoidanceTarget for the avoidance behaviours to work.
        // Do not need to edit these methods.
        #region ParentSteeringMethods

        // Seek returns a desired velocity to reach a target position, at a set speed.
        // This will cause an overshoot of the target position.
        // Do not edit this.
        public static Vector2 Seek(Vector2 currentPos, Vector2 targetPos, float maxSpeed, AvoidanceParams avoidParams)
        {
            if (avoidParams.Enable)
            {
                return SeekAndAvoid(currentPos, targetPos, maxSpeed, avoidParams);
            }
            else
            {
                return SeekDirect(currentPos, targetPos, maxSpeed);
            }
        }

        // Do not edit this method.
        // To implement obstacle avoidance, the only method you need to edit is GetAvoidanceTarget.
        public static Vector2 SeekAndAvoid(Vector2 currentPos, Vector2 targetPos, float maxSpeed, AvoidanceParams avoidParams)
        {
            targetPos = GetAvoidanceTarget(currentPos, targetPos, avoidParams);
            return SeekDirect(currentPos, targetPos, maxSpeed);
        }

        // Arrvie returns a desired velocity to reach a target position,
        // where the velocity is scaled by the distance to the target to avoid overshooting.
        // Do not edit this.
        public static Vector2 Arrive(Vector2 currentPos, Vector2 targetPos, float radius, float maxSpeed, AvoidanceParams avoidParams)
        {
            if (avoidParams.Enable)
            {
                return ArriveAndAvoid(currentPos, targetPos, radius, maxSpeed, avoidParams);
            }
            else
            {
                return ArriveDirect(currentPos, targetPos, radius, maxSpeed);
            }
        }

        // Do not edit this method.
        // To implement obstacle avoidance, the only method you need to edit is GetAvoidanceTarget.
        public static Vector2 ArriveAndAvoid(Vector2 currentPos, Vector2 targetPos, float radius, float maxSpeed, AvoidanceParams avoidParams)
        {
            targetPos = GetAvoidanceTarget(currentPos, targetPos, avoidParams);
            return ArriveDirect(currentPos, targetPos, radius, maxSpeed);
        }


        // Flee returns a desired velocity to move away from a target position, at a set speed.
        // where the velocity is scaled by the distance to the target to avoid overshooting.
        // Do not edit this.
        public static Vector2 Flee(Vector2 currentPos, Vector2 predatorPos, float maxSpeed, AvoidanceParams avoidParams)
        {
            if (avoidParams.Enable)
            {
                return FleeAndAvoid(currentPos, predatorPos, maxSpeed, avoidParams);
            }
            else
            {
                return FleeDirect(currentPos, predatorPos, maxSpeed);
            }
        }

        // Do not edit this method.
        // To implement obstacle avoidance, the only method you need to edit is GetAvoidanceTarget.
        public static Vector2 FleeAndAvoid(Vector2 currentPos, Vector2 predatorPos, float maxSpeed, AvoidanceParams avoidParams)
        {
            predatorPos = GetAvoidanceTarget(currentPos, predatorPos, avoidParams);
            return FleeDirect(currentPos, predatorPos, maxSpeed);
        }


        #endregion


        // Below are all the methods that you *do* need to edit.
        #region MethodsToImplement

        // Seek returns a desired velocity to reach a target position, at a set speed.
        // This will cause an overshoot of the target position.
        public static Vector2 SeekDirect(Vector2 currentPos, Vector2 targetPos, float maxSpeed)
        {
            Vector2 offset = targetPos - currentPos;
            Vector2 desiredVel = offset.normalized * maxSpeed;
            return desiredVel;
        }

        // Arrvie returns a desired velocity to reach a target position,
        // where the velocity is scaled by the distance to the target to avoid overshooting.
        public static Vector2 ArriveDirect(Vector2 currentPos, Vector2 targetPos, float radius, float maxSpeed)
        {
            Vector2 offset = targetPos - currentPos;
            float arriveRatio = Mathf.Min(1.0f, offset.magnitude / radius);
            return offset.normalized * maxSpeed * arriveRatio;
        }

        public static Vector2 FleeDirect(Vector2 currentPos, Vector2 predatorPos, float maxSpeed)
        {
            Vector2 offset = currentPos - predatorPos;
            return offset.normalized * maxSpeed;
        }

        // Find an avoidance target position for the given current and target positions.
        // See the spec for a detailed explanation of how GetAvoidanceTarget is expected to work.
        // You're expected to use Physics2D.CircleCast (https://docs.unity3d.com/ScriptReference/Physics2D.CircleCast.html)
        // You'll also probably want to use the rotate() method declared above.
        public static Vector2 GetAvoidanceTarget(Vector2 currentPos, Vector2 targetPos, AvoidanceParams avoidParams)
        {
            Vector2 avoidancePos = targetPos;

            // Try direct route first
            Vector2 offset = targetPos - currentPos;
            //Debug.DrawLine(currentPos, targetPos, Color.black);
            float castLength = Mathf.Min(avoidParams.MaxCastLength, Mathf.Max(0.0f, offset.magnitude - avoidParams.CircleCastRadius));
            RaycastHit2D hit = Physics2D.CircleCast(currentPos, avoidParams.CircleCastRadius, offset, castLength, avoidParams.ObstacleMask);

            // Return the original target if the direct route is free.
            if (!hit)
            {
                avoidancePos = targetPos;
            }
            else
            {
                bool foundPosition = false;
                for (float deviation = avoidParams.AngleIncrement; deviation <= 90.0f && !foundPosition; deviation += avoidParams.AngleIncrement)
                {
                    for (int sign = -1; sign <= 1 && !foundPosition; sign += 2)
                    {
                        Vector2 testDirection = rotate(offset, sign * deviation);
                        hit = Physics2D.CircleCast(currentPos, avoidParams.CircleCastRadius, testDirection, castLength, avoidParams.ObstacleMask);

                        if (!hit)
                        {
                            //Debug.DrawLine(currentPos, currentPos + testDirection, Color.green);
                            avoidancePos = currentPos + testDirection;
                            foundPosition = true;
                        }
                        else
                        {
                            //Debug.DrawLine(currentPos, currentPos + testDirection, Color.red);
                        }
                    }
                }
            }

            // Return the original target if we couldn't find a free path.
            return avoidancePos;
        }

        // See the assignment spec for an explanation of this method
        public static Vector2 GetSeparation(Vector2 currentPos, List<Transform> neighbours, float maxSpeed)
        {
            Vector2 desiredVel = Vector2.zero;

            foreach (Transform flockMember in neighbours)
            {
                Vector2 avoidanceVec = currentPos - (Vector2)flockMember.position;
                desiredVel += avoidanceVec.normalized / avoidanceVec.magnitude;
            }

            if (neighbours.Count > 0)
            {
                desiredVel = desiredVel.normalized * maxSpeed;
            }

            return desiredVel;
        }

        // See the assignment spec for an explanation of this method
        public static Vector2 GetCohesion(Vector2 currentPos, List<Transform> neighbours, float maxSpeed)
        {
            Vector2 desiredVel = Vector2.zero;

            foreach (Transform flockMember in neighbours)
            {
                desiredVel += (Vector2)flockMember.position - currentPos;
            }

            if (neighbours.Count > 0)
            {
                desiredVel = desiredVel.normalized * maxSpeed;
            }

            return desiredVel;
        }

        // See the assignment spec for an explanation of this method
        public static Vector2 GetAlignment(List<Transform> neighbours, float maxSpeed)
        {
            Vector2 desiredVel = Vector2.zero;

            foreach (Transform flockMember in neighbours)
            {
                desiredVel += flockMember.GetComponent<Rigidbody2D>().linearVelocity;
            }

            if (neighbours.Count > 0)
            {
                desiredVel = desiredVel.normalized * maxSpeed;
            }

            return desiredVel;
        }

        #endregion
    }
}
