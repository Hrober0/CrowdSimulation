using HCore.UI;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace Examples.Storage.UI
{
    public class WarehouseTab : ITab
    {
        private VisualElement _root;
        private readonly UIElementScrollView<WarehouseElement> _warehouseList;

        public VisualElement Content => _root;
        
        public WarehouseTab()
        {
            _root = new();
            var scrollView = UIStyledElements.NewScrollView(_root);
            
            _warehouseList = new(
                scrollView,
                () => {
                    var ve = new VisualElement();
                    scrollView.Add(ve);
                    var w = new WarehouseElement();
                    w.Init(ve);
                    return w;
                },
                direction: UIMethods.Direction.Vertical,
                hideOther: false
            );
        }
        
        public void Update()
        {
            var em  = World.DefaultGameObjectInjectionWorld.EntityManager;
            var q   = em.CreateEntityQuery(typeof(StorageComponent));
            var arr = q.ToEntityArray(Allocator.Temp);
            
            _warehouseList.SetElements(arr, (row, entity) => row.Refresh(entity, em));

            arr.Dispose();
            q.Dispose();
        }

        public void SetActive(bool active)
        {
            _root.SetActive(active);
        }
    }
}