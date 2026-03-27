using System.Collections.Generic;
using HCore.UI;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace Examples.Storage.UI
{
    public class ConnectionsTab : ITab
    {
        private EntityQuery _connQuery;
        private EntityQuery _storageQuery;

        private VisualElement _root;
        private DropdownField _pickerA;
        private DropdownField _pickerB;
        private EnumField     _resourcePicker;
        private UIElementScrollView<ConnectionElement> _list;

        private readonly List<Entity> _storageEntities = new();
        private bool _drawEnabled = true;

        public VisualElement Content => _root;

        public ConnectionsTab()
        {
            var em = Main.EntityManager;
            _connQuery    = em.CreateEntityQuery(typeof(StorageConnectionComponent));
            _storageQuery = em.CreateEntityQuery(typeof(StorageComponent));

            _root = new VisualElement();
            _root.style.flexGrow      = 1;
            _root.style.flexDirection = FlexDirection.Column;

            BuildToolbar();
            BuildList();
        }

        // ── ITab ──────────────────────────────────────────────────────────────

        public void Update()
        {
            using var connEntities    = _connQuery.ToEntityArray(Allocator.Temp);
            using var storageEntities = _storageQuery.ToEntityArray(Allocator.Temp);

            RefreshStoragePickers(storageEntities);
            _list.SetElements(connEntities, (el, entity) => el.Refresh(entity));
        }

        public void DrawGizmos()
        {
            if (!_drawEnabled) return;

            var em = Main.EntityManager;
            using var connEntities = _connQuery.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < connEntities.Length; i++)
            {
                var conn = em.GetComponentData<StorageConnectionComponent>(connEntities[i]);
                if (conn.Mode == ConnectionMode.Disabled) continue;
                if (!em.HasComponent<StorageComponent>(conn.StorageA)) continue;
                if (!em.HasComponent<StorageComponent>(conn.StorageB)) continue;

                var posA  = em.GetComponentData<StorageComponent>(conn.StorageA).WorldPosition;
                var posB  = em.GetComponentData<StorageComponent>(conn.StorageB).WorldPosition;
                var color = conn.Mode == ConnectionMode.TwoWays ? UIColors.Info : UIColors.Accent;

                if (conn.Mode == ConnectionMode.BToA)       DrawArrow(posB.xy, posA.xy, color);
                else if (conn.Mode == ConnectionMode.TwoWays) { DrawArrow(posA.xy, posB.xy, color); DrawArrow(posB.xy, posA.xy, color); }
                else                                         DrawArrow(posA.xy, posB.xy, color);
            }
        }

        public void SetActive(bool active) => _root.SetActive(active);

        // ── UI construction ───────────────────────────────────────────────────

        private void BuildToolbar()
        {
            var toolbar = UIStyledElements.NewHorizontalGroup(_root);
            toolbar.style.backgroundColor = UIColors.Surface;
            toolbar.style.SetBorderWidth(1);
            toolbar.style.SetBorderColor(UIColors.Border);
            toolbar.style.SetBorderRadius(4);
            toolbar.style.SetPadding(6);
            toolbar.style.paddingLeft  = 10;
            toolbar.style.marginBottom = 6;
            toolbar.style.alignItems   = Align.Center;
            toolbar.style.flexWrap     = Wrap.Wrap;

            var aLabel = UIStyledElements.NewLabel(toolbar, "A:");
            aLabel.style.color = UIColors.TextMuted;

            _pickerA = UIStyledElements.NewDropdownPicker(toolbar);
            _pickerA.style.marginLeft = 4;
            _pickerA.style.minWidth   = 110;

            var bLabel = UIStyledElements.NewLabel(toolbar, "B:");
            bLabel.style.color    = UIColors.TextMuted;
            bLabel.style.marginLeft = 8;

            _pickerB = UIStyledElements.NewDropdownPicker(toolbar);
            _pickerB.style.marginLeft = 4;
            _pickerB.style.minWidth   = 110;

            _resourcePicker = UIStyledElements.NewEnumPicker<ResourceType>(toolbar, ResourceType.Wood);
            _resourcePicker.style.marginLeft = 8;

            UIStyledElements.NewButtonPrimary(toolbar, "+ Add", AddConnection);

            var drawLabel = UIStyledElements.NewLabel(toolbar, "Draw");
            drawLabel.style.color     = UIColors.TextMuted;
            drawLabel.style.marginLeft = 10;
            var drawToggle = new Toggle { value = _drawEnabled };
            drawToggle.style.marginLeft = 3;
            drawToggle.RegisterValueChangedCallback(e => _drawEnabled = e.newValue);
            toolbar.Add(drawToggle);
        }

        private void BuildList()
        {
            var sv = UIStyledElements.NewScrollView(_root);
            _list = new(sv, direction: UIMethods.Direction.Vertical);
        }

        // ── Storage picker sync ───────────────────────────────────────────────

        private void RefreshStoragePickers(NativeArray<Entity> storageEntities)
        {
            // Skip rebuild if entities haven't changed.
            if (_storageEntities.Count == storageEntities.Length)
            {
                bool same = true;
                for (int i = 0; i < storageEntities.Length; i++)
                    if (_storageEntities[i] != storageEntities[i]) { same = false; break; }
                if (same) return;
            }

            _storageEntities.Clear();
            var choices = new List<string>(storageEntities.Length);
            for (int i = 0; i < storageEntities.Length; i++)
            {
                choices.Add($"Warehouse {storageEntities[i].Index}");
                _storageEntities.Add(storageEntities[i]);
            }

            // Preserve selection (clamp to new range).
            int ai = Mathf.Max(0, _pickerA.index);
            int bi = Mathf.Max(0, _pickerB.index);
            _pickerA.choices = choices;
            _pickerB.choices = choices;
            if (choices.Count > 0)
            {
                _pickerA.index = Mathf.Min(ai, choices.Count - 1);
                _pickerB.index = Mathf.Min(bi, choices.Count - 1);
            }
        }

        // ── Logic ─────────────────────────────────────────────────────────────

        private void AddConnection()
        {
            int ai = _pickerA.index;
            int bi = _pickerB.index;
            if (ai < 0 || bi < 0 || ai >= _storageEntities.Count || bi >= _storageEntities.Count) return;

            var storageA = _storageEntities[ai];
            var storageB = _storageEntities[bi];
            if (storageA == storageB) return;

            var resource = (ResourceType)_resourcePicker.value;
            ConnectionEditUtils.AddConnection(Main.EntityManager, storageA, storageB, resource, ConnectionMode.AToB);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void DrawArrow(Unity.Mathematics.float2 from, Unity.Mathematics.float2 to, Color color)
        {
            var f   = new Vector3(from.x, from.y, 0);
            var t   = new Vector3(to.x,   to.y,   0);
            var dir = t - f;
            if (dir.sqrMagnitude < 0.01f) return;

            Debug.DrawLine(f, t, color);

            dir.Normalize();
            var perp      = new Vector3(-dir.y, dir.x, 0);
            const float s = 0.8f;
            var arrowBase = t - dir * s;
            Debug.DrawLine(t, arrowBase + perp * (s * 0.5f), color);
            Debug.DrawLine(t, arrowBase - perp * (s * 0.5f), color);
        }
    }
}
