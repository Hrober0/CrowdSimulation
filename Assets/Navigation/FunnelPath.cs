using Unity.Collections;
using Unity.Mathematics;

namespace Navigation
{
    public static class FunnelPath
    {
        public static void FromPortals(float2 start, float2 end, NativeArray<Portal> portals, NativeList<float2> resultPath)
        {
            float2 apex = start;
            float2 left = apex;
            float2 right = apex;
            int leftIndex = 0;
            int rightIndex = 0;

            resultPath.Add(apex);

            for (int i = 0; i < portals.Length; i++)
            {
                float2 pLeft = portals[i].Left;
                float2 pRight = portals[i].Right;

                // Left check
                if (Triangle.SignedArea(apex, right, pRight) >= 0f)
                {
                    if (GeometryUtils.NearlyEqual(apex, right) || Triangle.SignedArea(apex, left, pRight) < 0f)
                    {
                        right = pRight;
                        rightIndex = i;
                    }
                    else
                    {
                        // Tight turn on left
                        resultPath.Add(left);
                        apex = left;
                        left = apex;
                        right = apex;
                        var apexIndex = leftIndex;
                        leftIndex = apexIndex;
                        rightIndex = apexIndex;
                        i = apexIndex;
                        continue;
                    }
                }

                // Right check
                if (Triangle.SignedArea(apex, left, pLeft) <= 0f)
                {
                    if (GeometryUtils.NearlyEqual(apex, left) || Triangle.SignedArea(apex, right, pLeft) > 0f)
                    {
                        left = pLeft;
                        leftIndex = i;
                    }
                    else
                    {
                        // Tight turn on right
                        resultPath.Add(right);
                        apex = right;
                        left = apex;
                        right = apex;
                        var apexIndex = rightIndex;
                        leftIndex = apexIndex;
                        rightIndex = apexIndex;
                        i = apexIndex;
                        continue;
                    }
                }
            }

            // Add end
            if (Triangle.SignedArea(apex, right, end) >= 0f && !GeometryUtils.NearlyEqual(apex, right) && !(Triangle.SignedArea(apex, left, end) < 0f))
            {
                resultPath.Add(left);
            }
            else if (Triangle.SignedArea(apex, left, end) <= 0f && !GeometryUtils.NearlyEqual(apex, left) && !(Triangle.SignedArea(apex, right, end) > 0f))
            {
                resultPath.Add(right);
            }
            if (resultPath.Length == 0 || !GeometryUtils.NearlyEqual(resultPath[^1], end))
            {
                resultPath.Add(end);
            }
        }
    }
}