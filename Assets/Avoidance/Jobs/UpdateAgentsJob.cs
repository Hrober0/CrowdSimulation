using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Avoidance
{
    [BurstCompile]
    public struct UpdateAgentJobParallel : IJobParallelFor
    {
        [NativeDisableContainerSafetyRestriction]
        public NativeList<Agent> Agents;

        [ReadOnly] public NativeParallelMultiHashMap<int, int> AgentLookup;
        [ReadOnly] public float ChunkSizeMultiplier;
        
        [ReadOnly] public NativeList<ObstacleVertex> ObstacleVertices;
        [ReadOnly] public NativeParallelMultiHashMap<int, int> ObstacleVerticesLookup;

        [ReadOnly] public float TimeStamp;

        private Agent currentAgent;

        public void Execute(int index)
        {
            currentAgent = Agents[index];
            var timeStampInv = 1f / TimeStamp;

            // Compute Agent Neighbor
            var agentNeighbors = new NativeList<AgentNeighbor>(Allocator.Temp);
            ComputeAgentNeighbor(currentAgent.NeighborDist, agentNeighbors);
            
            // Compute Obstacle Neighbor
            var obstacleNeighbors = new NativeList<ObstacleVertexNeighbor>(Allocator.Temp);
            ComputeObstacleNeighbor(currentAgent.TimeHorizonObstacle * currentAgent.MaxSpeed + currentAgent.Radius, obstacleNeighbors);

            // compute orca lines
            var orcaLines = new NativeList<Line>(Allocator.Temp);
            Linear.AddObstacleLine(currentAgent, orcaLines, obstacleNeighbors, ObstacleVertices);
            int numObstLines = orcaLines.Length;
            Linear.AddAgentLine(currentAgent, orcaLines, agentNeighbors, timeStampInv);

            var newVelocity = currentAgent.Velocity;

            int lineFail = Linear.LinearProgram2(orcaLines, currentAgent.MaxSpeed, currentAgent.PrefVelocity, false, ref newVelocity);
            if (lineFail < orcaLines.Length)
            {
                Linear.LinearProgram3(orcaLines, numObstLines, lineFail, currentAgent.MaxSpeed, ref newVelocity);
            }


            var velocityLengthSqr = math.lengthsq(newVelocity);
            if (velocityLengthSqr > currentAgent.MaxSpeed)
            {
                newVelocity = newVelocity / velocityLengthSqr * currentAgent.MaxSpeed;
            }
            currentAgent.Velocity = newVelocity;
            //_currentAgent.Position += newVelocity * TimeStamp;

            Agents[index] = currentAgent;

            agentNeighbors.Dispose();
            obstacleNeighbors.Dispose();
            orcaLines.Dispose();
        }

        private void ComputeAgentNeighbor(float range, NativeList<AgentNeighbor> agentNeighbors)
        {
            var rangeSq = math.square(range);
            var position = currentAgent.Position;
            var minX = (int)((position.x - range) * ChunkSizeMultiplier);
            var maxX = (int)((position.x + range) * ChunkSizeMultiplier);
            var minY = (int)((position.y - range) * ChunkSizeMultiplier);
            var maxY = (int)((position.y + range) * ChunkSizeMultiplier);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var chunkIndex = RVOMath.ChunkHash(x, y);
                    foreach (var agentIndex in AgentLookup.GetValuesForKey(chunkIndex))
                    {
                        var otherAgent = Agents[agentIndex];
                        InsertAgentNeighbor(otherAgent, rangeSq, agentNeighbors);
                    }
                }
            }
        }
        private void InsertAgentNeighbor(Agent neighbor, float rangeSq, NativeList<AgentNeighbor> agentNeighbors)
        {
            if (currentAgent.ObjectId == neighbor.ObjectId)
            {
                return;
            }

            float distSq = math.lengthsq(currentAgent.Position - neighbor.Position);

            if (distSq >= rangeSq)
            {
                return;
            }
            
            if (agentNeighbors.Length < currentAgent.MaxNeighbors)
            {
                agentNeighbors.Add(new AgentNeighbor { Distance = distSq, Agent = neighbor });
            }

            int i = agentNeighbors.Length - 1;

            while (i != 0 && distSq < agentNeighbors[i - 1].Distance)
            {
                agentNeighbors[i] = agentNeighbors[i - 1];
                --i;
            }

            agentNeighbors[i] = new AgentNeighbor { Distance = distSq, Agent = neighbor };
        }

        private void ComputeObstacleNeighbor(float range, NativeList<ObstacleVertexNeighbor> obstacleNeighbors)
        {
            var rangeSq = math.square(range);
            var position = currentAgent.Position;
            var minX = (int)((position.x - range) * ChunkSizeMultiplier);
            var maxX = (int)((position.x + range) * ChunkSizeMultiplier);
            var minY = (int)((position.y - range) * ChunkSizeMultiplier);
            var maxY = (int)((position.y + range) * ChunkSizeMultiplier);
            
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    foreach (var vertexIndex in ObstacleVerticesLookup.GetValuesForKey(RVOMath.ChunkHash(x, y)))
                    {
                        var vertex = ObstacleVertices[vertexIndex];
                        var startPosition = vertex.Point;
                        var endPosition = ObstacleVertices[vertex.Next].Point;

                        float agentLeftOfLine = RVOMath.LeftOf(startPosition, endPosition, currentAgent.Position);
                        if (agentLeftOfLine >= 0)
                        {
                            continue;
                        }

                        float distSqLine = math.square(agentLeftOfLine) / math.lengthsq(endPosition - startPosition);
                        if (distSqLine < rangeSq)
                        {
                            // Try obstacle at this node only if agent is on right side of
                            // obstacle (and can see obstacle).
                            InsertObstacleNeighbor(vertex, rangeSq, obstacleNeighbors);
                        }
                    }
                }
            }
        }
        private void InsertObstacleNeighbor(ObstacleVertex obstacle, float rangeSq, NativeList<ObstacleVertexNeighbor> obstacleNeighbors)
        {
            var nextObstacle = ObstacleVertices[obstacle.Next];

            float distSq = RVOMath.DistSqPointLineSegment(obstacle.Point, nextObstacle.Point, currentAgent.Position);

            if (distSq >= rangeSq)
            {
                return;
            }

            obstacleNeighbors.Add(new ObstacleVertexNeighbor { Distance = distSq, Obstacle = obstacle });

            int i = obstacleNeighbors.Length - 1;

            while (i != 0 && distSq < obstacleNeighbors[i - 1].Distance)
            {
                obstacleNeighbors[i] = obstacleNeighbors[i - 1];
                --i;
            }
            
            obstacleNeighbors[i] = new ObstacleVertexNeighbor { Distance = distSq, Obstacle = obstacle };
        }
    }
}