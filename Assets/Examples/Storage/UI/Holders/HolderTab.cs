using HCore.Extensions;
using HCore.UI;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UIElements;

namespace Examples.Storage.UI.Holders
{
    public class HoldersTab : ITab
    {
        private EntityQuery _holderQuery;
 
        private VisualElement _root;
        private Label _countLabel;
        private Label _activeLabel;
        private UIElementScrollView<HolderElement> _list;
 
        public VisualElement Content => _root;
 
        public HoldersTab()
        {
            _holderQuery = Main.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<HolderComponent>(),
                ComponentType.ReadOnly<LocalTransform>());
 
            _root = new VisualElement();
            _root.style.flexGrow = 1;
            _root.style.flexDirection = FlexDirection.Column;
 
            BuildStatsBar();
            BuildToolbar();
            BuildList();
        }
 
        public void Update()
        {
            var entities   = _holderQuery.ToEntityArray(Allocator.Temp);
            var components = _holderQuery.ToComponentDataArray<HolderComponent>(Allocator.Temp);
            var transforms = _holderQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
 
            int active = 0;
            for (int i = 0; i < components.Length; i++)
                if (components[i].State != HolderState.Idle) active++;
 
            _countLabel.text = entities.Length.ToString();
            _activeLabel.text = active.ToString();
            _activeLabel.style.color = active > 0 ? UIColors.Accent : UIColors.TextMuted;
 
            _list.Clear();
            for (int i = 0; i < entities.Length; i++)
            {
                var el = _list.ShowElement();
                el.Refresh(entities[i], components[i]);
 
                var t = transforms[i];
                new Vector2(t.Position.x, t.Position.y).DrawPoint(
                    components[i].State == HolderState.Idle ? Color.red : Color.green);
            }
 
            entities  .Dispose();
            components.Dispose();
            transforms.Dispose();
        }
 
        public void SetActive(bool active) => _root.SetActive(active);
 
        // ── UI construction ───────────────────────────────────────────────────
 
        private void BuildStatsBar()
        {
            var bar = UIStyledElements.NewHorizontalGroup(_root);
            bar.style.backgroundColor = UIColors.Surface;
            bar.style.SetBorderWidth(1);
            bar.style.SetBorderColor(UIColors.Border);
            bar.style.SetBorderRadius(4);
            bar.style.SetPadding(6);
            bar.style.paddingLeft  = 12;
            bar.style.marginBottom = 6;
 
            (_, _countLabel)  = UIStyledElements.NewLabel(bar, "Total",  "0", 50);
            UIStyledElements.NewSpace(bar, 16);
            (_, _activeLabel) = UIStyledElements.NewLabel(bar, "Active", "0", 50);
        }
 
        private void BuildList()
        {
            var sv = UIStyledElements.NewScrollView(_root);
            _list = new(sv, direction: UIMethods.Direction.Vertical);
        }
 
        private void BuildToolbar()
        {
            var toolbar = UIStyledElements.NewHorizontalGroup(_root);
            toolbar.style.backgroundColor = UIColors.Surface;
            toolbar.style.SetBorderWidth(1);
            toolbar.style.SetBorderColor(UIColors.Border);
            toolbar.style.SetBorderRadius(4);
            toolbar.style.SetPadding(6);
            toolbar.style.marginTop  = 6;
            toolbar.style.alignItems = Align.Center;
 
            UIStyledElements.NewButtonPrimary(toolbar, "+ Spawn Holder", SpawnHolder);
        }
 
        // ── Spawn ─────────────────────────────────────────────────────────────
 
        private void SpawnHolder()
        {
            var em = Main.EntityManager;
 
            // ArrivalTag and DeliveryJobComponent are IEnableableComponent.
            // They are part of the archetype from birth so they never trigger a
            // structural change when toggled — only a bitmask flip.
            var archetype = em.CreateArchetype(
                typeof(HolderComponent),
                typeof(DeliveryJobComponent), // IEnableableComponent
                typeof(ArrivalTag),           // IEnableableComponent
                typeof(LocalTransform),
                typeof(LocalToWorld));
 
            var entity = em.CreateEntity(archetype);
 
            em.SetComponentData(entity, new HolderComponent
            {
                CarryCapacity = 20,
                MoveSpeed     = 5f,
                State         = HolderState.Idle,
                AssignedJob   = Entity.Null,
            });
 
            em.SetComponentData(entity, LocalTransform.FromPosition(
                new(Random.Range(-15f, 15f), Random.Range(-10f, 10f), 0)));
 
            // Disable both enableable components immediately — they are inactive at birth.
            em.SetComponentEnabled<DeliveryJobComponent>(entity, false);
            em.SetComponentEnabled<ArrivalTag>(entity, false);
        }
    }
}