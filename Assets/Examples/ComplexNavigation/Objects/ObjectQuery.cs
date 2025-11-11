using System.Collections.Generic;
using HCore.Shapes;
using HCore.Systems;
using Objects.Agents;
using Objects.Obstacles;
using UnityEngine;

namespace Objects.GenericSystems
{
    public class ObjectQuery : MonoBehaviour, ISystem
    {
        private ObstacleSystem _obstacleSystem;
        private AgentsSystem _agentsSystem;
        
        void IInitializable.Initialize(ISystemManager systems)
        {
            _obstacleSystem = systems.Get<ObstacleSystem>();
            _agentsSystem = systems.Get<AgentsSystem>();
        }
        void IInitializable.Deinitialize() { }
        
        public List<IObject> FindInRange(IShape bounds)
        {
            // bounds.DrawBorder(Color.red, 2);
            var results = FindInRangeRect(bounds);
            for (int i = 0; i < results.Count; i++)
            {
                // results[i].Bounds.DrawBorder(results[i].Bounds.Intersects(bounds, false) ? Color.green : Color.red, 2);
                if (!results[i].Bounds.Intersects(bounds, false))
                {
                    results.RemoveAt(i);
                    i--;
                }
            }
            return results;
        }
        public List<IObject> FindInRangeRect(IShape bounds)
        {
            var results = new List<IObject>();
            var range = new SimpleRect(bounds.Min.x, bounds.Max.x, bounds.Min.y, bounds.Max.y);
            _agentsSystem.FindAgentsInRangeRect(range, results);
            _obstacleSystem.FindObstacleInRangeRect(range, results);
            return results;
        }
    }
}