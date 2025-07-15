using Unity.Collections;
using Unity.Mathematics;

namespace Navigation
{
    public static class FunnelPath
    {
        public static void FromPortals(NativeArray<Portal> portals, NativeList<float2> resultPath)
        {
            float2 apex = portals[0].Left;
            float2 left = apex;
            float2 right = apex;
            int apexIndex = 0;
            int leftIndex = 0;
            int rightIndex = 0;
            
            resultPath.Add(apex);

            for (int i = 1; i < portals.Length; i++)
            {
                float2 pLeft = portals[i].Left;
                float2 pRight = portals[i].Right;

                // Left check
                if (Triangle.Area2(apex, right, pRight) >= 0f)
                {
                    if (apex.Equals(right) || Triangle.Area2(apex, left, pRight) < 0f)
                    {
                        right = pRight;
                        rightIndex = i;
                    }
                    else
                    {
                        // Tight turn on left
                        resultPath.Add(left);
                        apex = left;
                        apexIndex = leftIndex;
                        left = apex;
                        right = apex;
                        leftIndex = apexIndex;
                        rightIndex = apexIndex;
                        i = apexIndex;
                        continue;
                    }
                }

                // Right check
                if (Triangle.Area2(apex, left, pLeft) <= 0f)
                {
                    if (apex.Equals(left) || Triangle.Area2(apex, right, pLeft) > 0f)
                    {
                        left = pLeft;
                        leftIndex = i;
                    }
                    else
                    {
                        // Tight turn on right
                        resultPath.Add(right);
                        apex = right;
                        apexIndex = rightIndex;
                        left = apex;
                        right = apex;
                        leftIndex = apexIndex;
                        rightIndex = apexIndex;
                        i = apexIndex;
                        continue;
                    }
                }
            }

            // add target if not already added
            if (math.distancesq(resultPath[^1], portals[^1].Left) > float.Epsilon)
            {
                resultPath.Add(portals[^1].Left);
            }
        }
    }
}