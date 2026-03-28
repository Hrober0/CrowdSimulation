using System.Collections.Generic;
using Examples.Storage.UI.Holders;
using HCore.UI;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace Examples.Storage.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class Main : MonoBehaviour
    {
        private UIDocument _doc;
        private List<(ITab content, Button button)> _tabs = new();
        private ITab _selectedTab;

        public static EntityManager EntityManager { get; private set; }

        void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            var root = _doc.rootVisualElement;

            var zoom = 2;
            var mainContainer = UIStyledElements.NewContainer(root);
            mainContainer.style.scale     = new Scale(new Vector2(zoom, zoom));
            mainContainer.style.translate = new Translate(new Length(50, LengthUnit.Percent), new Length(50, LengthUnit.Percent));
            mainContainer.style.width     = 400;

            // Report UI hover state to the input handler.
            mainContainer.RegisterCallback<PointerEnterEvent>(_ => WorldInputHandler.MouseOverUI = true);
            mainContainer.RegisterCallback<PointerLeaveEvent>(_ => WorldInputHandler.MouseOverUI = false);

            UIStyledElements.NewHeader(mainContainer, "Resource Manager");

            var tabs = UIStyledElements.NewHorizontalGroup(mainContainer);

            UIStyledElements.NewDivider(mainContainer);

            var content = new VisualElement();
            mainContainer.Add(content);

            EntityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            AddTab(content, tabs, "Warehouse", new WarehouseTab());
            AddTab(content, tabs, "Holders",   new HoldersTab());
            AddTab(content, tabs, "Connections", new ConnectionsTab());

            ShowTab(_tabs[0].content);
        }

        private void Update()
        {
            _selectedTab?.Update();
            foreach (var (tab, _) in _tabs)
                tab.DrawGizmos();
        }

        void ShowTab(ITab tab)
        {
            _selectedTab = tab;
            _tabs.ForEach(t =>
            {
                t.content.SetActive(false);
                t.button.style.backgroundColor = UIColors.Background;
            });

            var (tabContent, button) = _tabs.Find(item => item.content.Equals(tab));
            tabContent.SetActive(true);
            button.style.backgroundColor = UIColors.SurfaceRaised;
        }

        void AddTab(VisualElement parent, VisualElement tabs, string buttonName, ITab tab)
        {
            parent.Add(tab.Content);
            var button = UIStyledElements.NewButton(tabs, buttonName, () => ShowTab(tab));
            _tabs.Add((tab, button));
        }
    }
}
