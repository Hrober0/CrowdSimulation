using Unity.Collections;
using Unity.Mathematics;

namespace Avoidance
{
    public static class Linear
    {
        public static bool LinearProgram1(NativeList<Line> lines, int lineNo, float radius, float2 optVelocity, bool directionOpt, ref float2 result)
        {
            float dotProduct = math.dot(lines[lineNo].Point, lines[lineNo].Direction);
            float discriminant = math.square(dotProduct) + math.square(radius) - math.lengthsq(lines[lineNo].Point);

            if (discriminant < 0f)
            {
                /* Max speed circle fully invalidates line lineNo. */
                return false;
            }

            float sqrtDiscriminant = math.sqrt(discriminant);
            float tLeft = -dotProduct - sqrtDiscriminant;
            float tRight = -dotProduct + sqrtDiscriminant;

            for (int i = 0; i < lineNo; ++i)
            {
                float denominator = RVOMath.Det(lines[lineNo].Direction, lines[i].Direction);
                float numerator = RVOMath.Det(lines[i].Direction, lines[lineNo].Point - lines[i].Point);

                if (math.abs(denominator) <= RVOMath.EPSILON)
                {
                    /* Lines lineNo and i are (almost) parallel. */
                    if (numerator < 0)
                    {
                        return false;
                    }

                    continue;
                }

                float t = numerator / denominator;

                if (denominator >= 0)
                {
                    /* Line i bounds line lineNo on the right. */
                    tRight = math.min(tRight, t);
                }
                else
                {
                    /* Line i bounds line lineNo on the left. */
                    tLeft = math.max(tLeft, t);
                }

                if (tLeft > tRight)
                {
                    return false;
                }
            }

            if (directionOpt)
            {
                /* Optimize direction. */
                if (math.dot(optVelocity, lines[lineNo].Direction) > 0)
                {
                    /* Take right extreme. */
                    result = lines[lineNo].Point + tRight * lines[lineNo].Direction;
                }
                else
                {
                    /* Take left extreme. */
                    result = lines[lineNo].Point + tLeft * lines[lineNo].Direction;
                }
            }
            else
            {
                /* Optimize closest point. */
                float t = math.dot(lines[lineNo].Direction, optVelocity - lines[lineNo].Point);

                if (t < tLeft)
                {
                    result = lines[lineNo].Point + tLeft * lines[lineNo].Direction;
                }
                else if (t > tRight)
                {
                    result = lines[lineNo].Point + tRight * lines[lineNo].Direction;
                }
                else
                {
                    result = lines[lineNo].Point + t * lines[lineNo].Direction;
                }
            }

            return true;
        }
        
        public static int LinearProgram2(NativeList<Line> lines, float radius, float2 optVelocity, bool directionOpt, ref float2 result)
        {
            if (directionOpt)
            {
                /*
                * Optimize direction. Note that the optimization velocity is of
                * unit length in this case.
                */
                result = optVelocity * radius;
            }
            else if (math.lengthsq(optVelocity) > math.square(radius))
            {
                /* Optimize the closest point and outside circle. */
                result = math.normalize(optVelocity) * radius;
            }
            else
            {
                /* Optimize the closest point and inside circle. */
                result = optVelocity;
            }

            for (int i = 0; i < lines.Length; ++i)
            {
                if (RVOMath.Det(lines[i].Direction, lines[i].Point - result) > 0)
                {
                    /* Result does not satisfy constraint i. Compute new optimal result. */
                    var tempResult = result;
                    if (!LinearProgram1(lines, i, radius, optVelocity, directionOpt, ref result))
                    {
                        result = tempResult;

                        return i;
                    }
                }
            }

            return lines.Length;
        }
        
        public static void LinearProgram3(NativeList<Line> lines, int numObstLines, int beginLine, float radius, ref float2 result)
        {
            float distance = 0;

            for (int i = beginLine; i < lines.Length; ++i)
            {
                if (RVOMath.Det(lines[i].Direction, lines[i].Point - result) <= distance)
                {
                    continue;
                }

                /* Result does not satisfy constraint of line i. */
                var projLines = new NativeList<Line>(Allocator.Temp);
                for (int ii = 0; ii < numObstLines; ++ii)
                {
                    projLines.Add(lines[ii]);
                }

                for (int j = numObstLines; j < i; ++j)
                {
                    Line line;

                    float determinant = RVOMath.Det(lines[i].Direction, lines[j].Direction);

                    if (math.abs(determinant) <= RVOMath.EPSILON)
                    {
                        /* Line i and line j are parallel. */
                        if (math.dot(lines[i].Direction, lines[i].Direction) > 0)
                        {
                            /* Line i and line j point in the same direction. */
                            continue;
                        }
                        else
                        {
                            /* Line i and line j point in opposite direction. */
                            line.Point = 0.5f * (lines[i].Point + lines[j].Point);
                        }
                    }
                    else
                    {
                        line.Point = lines[i].Point + (RVOMath.Det(lines[j].Direction, lines[i].Point - lines[j].Point) / determinant) * lines[i].Direction;
                    }

                    var dir = lines[j].Direction - lines[i].Direction;
                    var length = math.length(dir);
                    if (length > 0)
                    {
                        line.Direction = dir / length;
                        projLines.Add(line);
                    }

                }

                float2 tempResult = result;
                if (LinearProgram2(projLines, radius, new float2(-lines[i].Direction.y, lines[i].Direction.x), true, ref result) < projLines.Length)
                {
                    /*
                    * This should in principle not happen. The result is by
                    * definition already in the feasible region of this
                    * linear program. If it fails, it is due to small
                    * FixedInt point error, and the current result is kept.
                    */
                    result = tempResult;
                }

                distance = RVOMath.Det(lines[i].Direction, lines[i].Point - result);
                projLines.Dispose();
            }
        }
        
        public static void AddAgentLine(Agent currentAgent, NativeList<Line> orcaLines, NativeList<AgentNeighbor> agentNeighbors, float timeStampInv)
        {
            float invTimeHorizon = 1 / currentAgent.TimeHorizonAgent;
            
            for (int i = 0; i < agentNeighbors.Length; ++i)
            {
                var other = agentNeighbors[i].Agent;

                float2 relativePosition = other.Position - currentAgent.Position;
                float2 relativeVelocity = currentAgent.Velocity - other.Velocity;
                float distSq = math.lengthsq(relativePosition);
                float combinedRadius = currentAgent.Radius + other.Radius;
                float combinedRadiusSq = math.square(combinedRadius);

                Line line;
                float2 u;

                if (distSq > combinedRadiusSq)
                {
                    // No collision.
                    float2 w = relativeVelocity - invTimeHorizon * relativePosition;

                    /* Vector from cutoff center to relative velocity. */
                    float wLengthSq = math.lengthsq(w);
                    float dotProduct1 = math.dot(w, relativePosition);

                    if (dotProduct1 < 0f && math.square(dotProduct1) > combinedRadiusSq * wLengthSq)
                    {
                        /* Project on cut-off circle. */
                        float wLength = math.sqrt(wLengthSq);
                        float2 unitW = w / wLength;

                        line.Direction = new(unitW.y, -unitW.x);
                        u = (combinedRadius * invTimeHorizon - wLength) * unitW;
                    }
                    else
                    {
                        /* Project on legs. */
                        float leg = math.sqrt(distSq - combinedRadiusSq);

                        if (RVOMath.Det(relativePosition, w) > 0)
                        {
                            /* Project on left leg. */
                            line.Direction = new float2(relativePosition.x * leg - relativePosition.y * combinedRadius, relativePosition.x * combinedRadius + relativePosition.y * leg) / distSq;
                        }
                        else
                        {
                            /* Project on right leg. */
                            line.Direction = -new float2(relativePosition.x * leg + relativePosition.y * combinedRadius, -relativePosition.x * combinedRadius + relativePosition.y * leg) / distSq;
                        }

                        float dotProduct2 = math.dot(relativeVelocity, line.Direction);
                        u = dotProduct2 * line.Direction - relativeVelocity;
                    }
                }
                else
                {
                    /* Vector from cutoff center to relative velocity. */
                    float2 w = relativeVelocity - timeStampInv * relativePosition;


                    float wLength = math.length(w);
                    float2 unitW = w / wLength;

                    line.Direction = new float2(unitW.y, -unitW.x);
                    u = (combinedRadius * timeStampInv - wLength) * unitW;
                }

                line.Point = currentAgent.Velocity + 0.5f * u;
                orcaLines.Add(line);
            }
        }
        
        public static void AddObstacleLine(Agent currentAgent, NativeList<Line> orcaLines, NativeList<ObstacleVertexNeighbor> obstacleNeighbors, NativeList<ObstacleVertex> obstacles)
        {
            float invTimeHorizonObst = 1f / currentAgent.TimeHorizonObstacle;
            float radius = currentAgent.Radius;
            float2 velocity = currentAgent.Velocity;
            float2 position = currentAgent.Position;

            // Create obstacle ORCA lines.
            for (int i = 0; i < obstacleNeighbors.Length; ++i)
            {
                ObstacleVertex obstacle1 = obstacleNeighbors[i].Obstacle;
                ObstacleVertex obstacle2 = obstacles[obstacle1.Next];

                float2 relativePosition1 = obstacle1.Point - currentAgent.Position;
                float2 relativePosition2 = obstacle2.Point - currentAgent.Position;

                //Check if velocity obstacle of obstacle is already taken care
                // of by previously constructed obstacle ORCA lines.
                bool alreadyCovered = false;

                for (int j = 0; j < orcaLines.Length; ++j)
                {
                    if (RVOMath.Det(invTimeHorizonObst * relativePosition1 - orcaLines[j].Point, orcaLines[j].Direction) - invTimeHorizonObst * radius >= -RVOMath.EPSILON
                     && RVOMath.Det(invTimeHorizonObst * relativePosition2 - orcaLines[j].Point, orcaLines[j].Direction) - invTimeHorizonObst * radius >= -RVOMath.EPSILON)
                    {
                        alreadyCovered = true;
                        break;
                    }
                }

                if (alreadyCovered)
                {
                    continue;
                }

                // Not yet covered. Check for collisions.
                float distSq1 = math.lengthsq(relativePosition1);
                float distSq2 = math.lengthsq(relativePosition2);

                float radiusSq = math.square(radius);

                float2 obstacleVector = obstacle2.Point - obstacle1.Point;
                float s = (math.dot(-relativePosition1, obstacleVector) / math.lengthsq(obstacleVector));
                float distSqLine = math.lengthsq(-relativePosition1 - s * obstacleVector);

                Line line;

                if (s < 0 && distSq1 <= radiusSq)
                {
                    // Collision with left vertex. Ignore if non-convex.
                    if (obstacle1.Convex)
                    {
                        line.Point = float2.zero;
                        line.Direction = math.normalize(new float2(-relativePosition1.y, relativePosition1.x));
                        orcaLines.Add(line);
                    }

                    continue;
                }
                else if (s > 1 && distSq2 <= radiusSq)
                {
                    // Collision with right vertex. Ignore if non-convex or if
                    // it will be taken care of by neighboring obstacle.
                    if (obstacle2.Convex && RVOMath.Det(relativePosition2, obstacle2.Direction) >= 0)
                    {
                        line.Point = float2.zero;
                        line.Direction = math.normalize(new float2(-relativePosition2.y, relativePosition2.x));
                        orcaLines.Add(line);
                    }

                    continue;
                }
                else if (s >= 0 && s < 1 && distSqLine <= radiusSq)
                {
                    // Collision with obstacle segment.
                    line.Point = float2.zero;
                    line.Direction = -obstacle1.Direction;
                    orcaLines.Add(line);

                    continue;
                }

                // No collision. Compute legs. When obliquely viewed, both legs
                // can come from a single vertex. Legs extend cut-off line when
                // non-convex vertex.

                float2 leftLegDirection, rightLegDirection;

                if (s < 0 && distSqLine <= radiusSq)
                {
                    //
                    // Obstacle viewed obliquely so that left vertex
                    // defines velocity obstacle.
                    
                    if (!obstacle1.Convex)
                    {
                        // Ignore obstacle.
                        continue;
                    }

                    obstacle2 = obstacle1;

                    float leg1 = math.sqrt(distSq1 - radiusSq);
                    leftLegDirection = new float2(relativePosition1.x * leg1 - relativePosition1.y * radius, relativePosition1.x * radius + relativePosition1.y * leg1) / distSq1;
                    rightLegDirection = new float2(relativePosition1.x * leg1 + relativePosition1.y * radius, -relativePosition1.x * radius + relativePosition1.y * leg1) / distSq1;
                }
                else if (s > 1 && distSqLine <= radiusSq)
                {
                    // Obstacle viewed obliquely so that
                    // right vertex defines velocity obstacle.
                    if (!obstacle2.Convex)
                    {
                        // Ignore obstacle.
                        continue;
                    }

                    obstacle1 = obstacle2;

                    float leg2 = math.sqrt(distSq2 - radiusSq);
                    leftLegDirection = new float2(relativePosition2.x * leg2 - relativePosition2.y * radius, relativePosition2.x * radius + relativePosition2.y * leg2) / distSq2;
                    rightLegDirection = new float2(relativePosition2.x * leg2 + relativePosition2.y * radius, -relativePosition2.x * radius + relativePosition2.y * leg2) / distSq2;
                }
                else
                {
                    // Usual situation.
                    if (obstacle1.Convex)
                    {
                        float leg1 = math.sqrt(distSq1 - radiusSq);
                        leftLegDirection = new float2(relativePosition1.x * leg1 - relativePosition1.y * radius, relativePosition1.x * radius + relativePosition1.y * leg1) / distSq1;
                    }
                    else
                    {
                        // Left vertex non-convex; left leg extends cut-off line.
                        leftLegDirection = -obstacle1.Direction;
                    }

                    if (obstacle2.Convex)
                    {
                        float leg2 = math.sqrt(distSq2 - radiusSq);
                        rightLegDirection = new float2(relativePosition2.x * leg2 + relativePosition2.y * radius, -relativePosition2.x * radius + relativePosition2.y * leg2) / distSq2;
                    }
                    else
                    {
                        // Right vertex non-convex; right leg extends cut-off line.
                        rightLegDirection = obstacle1.Direction;
                    }
                }

                // Legs can never point into neighboring edge when convex
                // vertex, take cutoff-line of neighboring edge instead. If
                // velocity projected on "foreign" leg, no constraint is added.

                ObstacleVertex leftNeighbor = obstacles[obstacle1.Previous];

                bool isLeftLegForeign = false;
                bool isRightLegForeign = false;

                if (obstacle1.Convex && RVOMath.Det(leftLegDirection, -leftNeighbor.Direction) >= 0)
                {
                    // Left leg points into obstacle.
                    leftLegDirection = -leftNeighbor.Direction;
                    isLeftLegForeign = true;
                }

                if (obstacle2.Convex && RVOMath.Det(rightLegDirection, obstacle2.Direction) <= 0)
                {
                    // Right leg points into obstacle.
                    rightLegDirection = obstacle2.Direction;
                    isRightLegForeign = true;
                }

                // Compute cut-off centers.
                float2 leftCutOff = invTimeHorizonObst * (obstacle1.Point - position);
                float2 rightCutOff = invTimeHorizonObst * (obstacle2.Point - position);
                float2 cutOffVector = rightCutOff - leftCutOff;

                // Project current velocity on velocity obstacle.

                // Check if current velocity is projected on cutoff circles.
                float t = obstacle1 == obstacle2 ? 0.5f : (math.dot(velocity - leftCutOff, cutOffVector) / math.lengthsq(cutOffVector));
                float tLeft = math.dot(velocity - leftCutOff, leftLegDirection);
                float tRight = math.dot(velocity - rightCutOff, rightLegDirection);

                if ((t < 0 && tLeft < 0) || (obstacle1 == obstacle2 && tLeft < 0 && tRight < 0))
                {
                    // Project on left cut-off circle.
                    float2 unitW = math.normalize(velocity - leftCutOff);

                    line.Direction = new float2(unitW.y, -unitW.x);
                    line.Point = leftCutOff + radius * invTimeHorizonObst * unitW;
                    orcaLines.Add(line);

                    continue;
                }
                else if (t > 1 && tRight < 0)
                {
                    // Project on right cut-off circle. 
                    float2 unitW = math.normalize(velocity - rightCutOff);

                    line.Direction = new float2(unitW.y, -unitW.x);
                    line.Point = rightCutOff + radius * invTimeHorizonObst * unitW;
                    orcaLines.Add(line);

                    continue;
                }

                // Project on left leg, right leg, or cut-off line, whichever is
                // closest to velocity.
                float distSqCutoff = (t < 0 || t > 1 || obstacle1 == obstacle2) ? float.MaxValue : math.lengthsq(velocity - (leftCutOff + t * cutOffVector));
                float distSqLeft = tLeft < 0 ? float.MaxValue : math.lengthsq(velocity - (leftCutOff + tLeft * leftLegDirection));
                float distSqRight = tRight < 0 ? float.MaxValue : math.lengthsq(velocity - (rightCutOff + tRight * rightLegDirection));

                if (distSqCutoff <= distSqLeft && distSqCutoff <= distSqRight)
                {
                    // Project on cut-off line.
                    line.Direction = -obstacle1.Direction;
                    line.Point = leftCutOff + radius * invTimeHorizonObst * new float2(-line.Direction.y, line.Direction.x);
                    orcaLines.Add(line);

                    continue;
                }

                if (distSqLeft <= distSqRight)
                {
                    // Project on left leg.
                    if (isLeftLegForeign)
                    {
                        continue;
                    }

                    line.Direction = leftLegDirection;
                    line.Point = leftCutOff + radius * invTimeHorizonObst * new float2(-line.Direction.y, line.Direction.x);
                    orcaLines.Add(line);

                    continue;
                }

                // Project on right leg.
                if (isRightLegForeign)
                {
                    continue;
                }

                line.Direction = -rightLegDirection;
                line.Point = rightCutOff + radius * invTimeHorizonObst * new float2(-line.Direction.y, line.Direction.x);
                orcaLines.Add(line);
            }
        }
    }
}