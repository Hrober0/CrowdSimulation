using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace ComplexNavigation
{
    public class AgentSelectionManager : MonoBehaviour
    {
        private void Update()
        {
            if (Input.GetMouseButtonDown(1))
            {
                SetTarget();
            }
        }

        private void SetTarget()
        {
            var mousePosition = MousePosition.GetMousePosition();
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            EntityQuery entityQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<TargetData>().Build(entityManager);
            var targetDataArray = entityQuery.ToComponentDataArray<TargetData>(Allocator.Temp);
            for (int i = 0; i < targetDataArray.Length; i++)
            {
                var targetData = targetDataArray[i];
                targetData.TargetPosition = mousePosition;
                targetDataArray[i] = targetData;
            }
            entityQuery.CopyFromComponentDataArray(targetDataArray);
        }
    }
}