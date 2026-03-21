using HCore.UI;
using Unity.Collections;
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

            _warehouseList = new(scrollView, direction: UIMethods.Direction.Vertical);
        }

        public void Update()
        {
            using var q = Main.EntityManager.CreateEntityQuery(typeof(StorageComponent));
            using var arr = q.ToEntityArray(Allocator.Temp);

            _warehouseList.SetElements(arr, (row, entity) => row.Refresh(entity));
        }

        public void SetActive(bool active)
        {
            _root.SetActive(active);
        }
    }
}