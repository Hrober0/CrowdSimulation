using System;
using System.Collections.Generic;
using HCore.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Examples.Storage.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class Main : MonoBehaviour
    {
        private UIDocument _doc;
        private List<ITab> _tabs = new();
        private ITab _selectedTab;

        void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            var root = _doc.rootVisualElement;

            var mainContainer = UIStyledElements.NewContainer(root);
            mainContainer.style.width = 400;

            UIStyledElements.NewHeader(mainContainer, "Resource Manager");

            var tabs = UIStyledElements.NewHorizontalGroup(mainContainer);

            var content = new VisualElement();
            mainContainer.Add(content);

            AddTab(content, tabs, "Warehouse", new WarehouseTab());
            // AddTab(content, tabs, "", BuildWarehouseTab());
            // AddTab(content, tabs, "", BuildWarehouseTab());

            ShowTab(_tabs[0]); // default tab
        }

        private void Update()
        {
            _selectedTab?.Update();
        }

        void ShowTab(ITab tab)
        {
            _selectedTab = tab;
            _tabs.ForEach(tab => tab.SetActive(false));
            tab.SetActive(true);
        }

        void AddTab(VisualElement parent, VisualElement tabs, string buttonName, ITab tab)
        {
            _tabs.Add(tab);
            parent.Add(tab.Content);
            UIStyledElements.NewButton(tabs, buttonName, () => ShowTab(tab));
        }
    }
}