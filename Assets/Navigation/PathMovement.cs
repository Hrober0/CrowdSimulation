using Unity.Collections;
using Unity.Mathematics;

namespace Navigation
{
    public static class PathMovement
    {
        public static float2 GetLookAheadPoint(
            float2 position,
            NativeArray<float2> path,
            int index,
            float lookAhead)
        {
            float2 current = position;
            float remaining = lookAhead;
        
            // Walk forward through path segments
            for (int i = index; i < path.Length; i++)
            {
                float2 next = path[i];
                float2 segment = next - current;
                float segLen = math.length(segment);
        
                if (segLen >= remaining)
                {
                    // We can place the lookahead point inside this segment
                    float2 direction = segment / math.max(segLen, math.EPSILON);
                    return current + direction * remaining;
                }
        
                remaining -= segLen;
                current = next;
            }
        
            // End of path
            return path[^1];
        }

        public static float2 ComputePreferredVelocity(
            float2 position,
            float2 velocity,
            float2 target,
            float maxSpeed,
            float maxAcceleration,
            float slowDownDistance,
            float deltaTime)
        {
            float2 toTarget = target - position;
            float toTargetLength = math.length(toTarget);

            if (toTargetLength < 0.001f)
            {
                return float2.zero;
            }

            float desiredSpeed =
                maxSpeed * math.saturate(toTargetLength / slowDownDistance);

            float2 desiredVel = toTarget / toTargetLength * desiredSpeed;

            float2 dv = desiredVel - velocity;
            float maxDelta = maxAcceleration * deltaTime;

            float dvLength = math.length(dv);
            if (dvLength > maxDelta)
            {
                dv = dv / dvLength * maxDelta;
            }

            return velocity + dv;
        }
    }
}