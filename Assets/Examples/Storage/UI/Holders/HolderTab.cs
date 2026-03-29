using HCore.Extensions;
using HCore.UI;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
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
        private bool _drawEnabled = true;
        private bool _placing;
        private Button _placeButton;

        public VisualElement Content => _root;

        public HoldersTab()
        {
            _holderQuery = Main.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<HolderComponent>(),
                ComponentType.ReadOnly<LocalTransform>());

            _root = new VisualElement();
            _root.style.flexGrow = 1;
            _root.style.flexDirection = FlexDirection.Column;

            BuildToolBar();
            BuildList();

            WorldInputHandler.WorldClicked += OnWorldClick;
        }

        public void Update()
        {
            var entities = _holderQuery.ToEntityArray(Allocator.Temp);
            var components = _holderQuery.ToComponentDataArray<HolderComponent>(Allocator.Temp);

            int active = 0;
            for (int i = 0; i < components.Length; i++)
                if (components[i].State != HolderState.Idle)
                    active++;

            _countLabel.text = entities.Length.ToString();
            _activeLabel.text = active.ToString();
            _activeLabel.style.color = active > 0 ? UIColors.Accent : UIColors.TextMuted;

            _list.Clear();
            for (int i = 0; i < entities.Length; i++)
            {
                var el = _list.ShowElement();
                el.Refresh(entities[i], components[i]);
            }

            entities.Dispose();
            components.Dispose();
        }

        public void DrawGizmos()
        {
            if (!_drawEnabled) return;

            var transforms = _holderQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var components = _holderQuery.ToComponentDataArray<HolderComponent>(Allocator.Temp);

            for (int i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                new Vector2(t.Position.x, t.Position.y).DrawPoint(
                    components[i].State == HolderState.Idle ? Color.red : Color.green, size: .3f);
            }

            transforms.Dispose();
            components.Dispose();

            foreach (var element in _list)
            {
                if (element.Hovered)
                {
                    var t = Main.EntityManager.GetComponentData<LocalTransform>(element.BoundEntity);
                    new Vector2(t.Position.x, t.Position.y).DrawPoint(Color.magenta, size: .3f);
                }
            }
        }

        public void SetActive(bool active)
        {
            _root.SetActive(active);
            if (_placing) TogglePlacing();
        }

        private void OnWorldClick(Vector3 worldPos)
        {
            if (!_placing) return;
            SpawnHolder(new float3(worldPos.x, worldPos.y, 0));
        }

        // ── UI construction ───────────────────────────────────────────────────

        private void BuildToolBar()
        {
            var toolbar = UIStyledElements.NewHorizontalContainer(_root);
            _placeButton = UIStyledElements.NewButtonPrimary(toolbar, "+ Place Holder", TogglePlacing);
            (_, _countLabel) = UIStyledElements.NewLabel(toolbar, "Total", "0", nameWidth: 50, my: 0);
            (_, _activeLabel) = UIStyledElements.NewLabel(toolbar, "Active", "0", nameWidth: 50, my: 0);
            UIStyledElements.NewLabel(toolbar, "Draw");
            UIStyledElements.NewCheckbox(toolbar, _drawEnabled, v => _drawEnabled = v);
        }

        private void BuildList()
        {
            var sv = UIStyledElements.NewScrollView(_root);
            _list = new(sv, direction: UIMethods.Direction.Vertical);
        }

        // ── Spawn ─────────────────────────────────────────────────────────────

        private void TogglePlacing()
        {
            _placing = !_placing;
            _placeButton.text = _placing ? "Cancel Placement" : "+ Place Holder";
            _placeButton.style.backgroundColor = _placing ? UIColors.Warning : UIColors.Accent;
        }

        private static void SpawnHolder(float3 position)
        {
            var em = Main.EntityManager;

            // ArrivalTag and DeliveryJobComponent are IEnableableComponent.
            // They are part of the archetype from birth so they never trigger a
            // structural change when toggled — only a bitmask flip.
            var archetype = em.CreateArchetype(
                typeof(HolderComponent),
                typeof(DeliveryJobComponent), // IEnableableComponent
                typeof(ArrivalTag), // IEnableableComponent
                typeof(LocalTransform),
                typeof(LocalToWorld));

            var entity = em.CreateEntity(archetype);

            em.SetComponentData(entity, new HolderComponent
            {
                CarryCapacity = 2,
                MoveSpeed = 5f,
                State = HolderState.Idle,
                AssignedJob = Entity.Null,
            });

            em.SetComponentData(entity, LocalTransform.FromPosition(position));

            // Disable both enableable components immediately — they are inactive at birth.
            em.SetComponentEnabled<DeliveryJobComponent>(entity, false);
            em.SetComponentEnabled<ArrivalTag>(entity, false);
        }
    }
}