using System.Collections.Generic;
using Objects.Agents;
using UnityEngine;

namespace AvoidanceTest
{
    public class SetRandomAgentVelcity : MonoBehaviour
    {
        [SerializeField] private List<SampleAgent> _agents;
        
        void Start()
        {
            foreach (var agent in _agents)
            {
                agent.TargetVelocity = Random.insideUnitCircle.normalized;
            }
        }
    }
}
