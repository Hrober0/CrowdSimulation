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
            UIPanelScale.ScaleWithScreen(_doc);
            var root = _doc.rootVisualElement;

            // Width in the same reference units as everything in UIStyledElements; the panel scale takes it
            // from there, so this stays the same share of the window on every screen.
            var mainContainer = UIStyledElements.NewContainer(root);
            mainContainer.style.width = 800;

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
