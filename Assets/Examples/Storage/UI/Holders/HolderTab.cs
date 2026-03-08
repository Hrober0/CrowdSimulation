using HCore.Extensions;
using HCore.UI;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UIElements;

namespace Examples.Storage.UI.Holders
{
    /// <summary>
    /// Self-contained Holders tab.
    /// StoragePanel creates one instance and calls BuildTab() to get the root element,
    /// then calls Tick() every Update().
    /// </summary>
    public class HoldersTab : ITab
    {
        private EntityManager _em;
        private EntityQuery _holderQuery;

        private VisualElement _root;
        private Label _countLabel;
        private Label _activeLabel;
        private UIElementScrollView<HolderElement> _list;

        public VisualElement Content => _root;


        public HoldersTab(EntityManager em)
        {
            _em = em;
            _holderQuery = em.CreateEntityQuery(
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
            var entities = _holderQuery.ToEntityArray(Allocator.Temp);
            var components = _holderQuery.ToComponentDataArray<HolderComponent>(Allocator.Temp);
            var transforms = _holderQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Update stats
            int active = 0;
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i].State != HolderState.Idle)
                {
                    active++;
                }
            }

            _countLabel.text = entities.Length.ToString();
            _activeLabel.text = active.ToString();
            _activeLabel.style.color = active > 0 ? UIColors.Accent : UIColors.TextMuted;
            
            int count = entities.Length;
            _list.Clear();
            for (int i = 0; i < count; i++)
            {
                var el = _list.ShowElement();
                el.Refresh(entities[i], components[i]);
                
                var transform = transforms[i];
                new Vector2(transform.Position.x, transform.Position.y).DrawPoint(components[i].State == HolderState.Idle ? Color.red : Color.green);
            }
            

            entities.Dispose();
            components.Dispose();
            transforms.Dispose();
        }

        public void SetActive(bool active)
        {
            _root.SetActive(active);
        }


        private void BuildStatsBar()
        {
            var bar = UIStyledElements.NewHorizontalGroup(_root);
            bar.style.backgroundColor = UIColors.Surface;
            bar.style.SetBorderWidth(1);
            bar.style.SetBorderColor(UIColors.Border);
            bar.style.SetBorderRadius(4);
            bar.style.SetPadding(6);
            bar.style.paddingLeft = 12;
            bar.style.marginBottom = 6;

            (_, _countLabel) = UIStyledElements.NewLabel(bar, "Total", "0", 50);
            UIStyledElements.NewSpace(bar, 16);
            (_, _activeLabel) = UIStyledElements.NewLabel(bar, "Active", "0", 50);
        }

        private void BuildList()
        {
            var sv = UIStyledElements.NewScrollView(_root);

            _list = new(
                sv,
                () =>
                {
                    var ve = new VisualElement();
                    sv.Add(ve);
                    var el = new HolderElement();
                    el.Init(ve);
                    return el;
                },
                direction: UIMethods.Direction.Vertical,
                hideOther: false);
        }


        private void BuildToolbar()
        {
            var toolbar = UIStyledElements.NewHorizontalGroup(_root);
            toolbar.style.backgroundColor = UIColors.Surface;
            toolbar.style.SetBorderWidth(1);
            toolbar.style.SetBorderColor(UIColors.Border);
            toolbar.style.SetBorderRadius(4);
            toolbar.style.SetPadding(6);
            toolbar.style.marginTop = 6;
            toolbar.style.alignItems = Align.Center;

            UIStyledElements.NewButtonPrimary(toolbar, "+ Spawn Holder", SpawnHolder);
        }

        private void SpawnHolder()
        {
            var archetype = _em.CreateArchetype(
                typeof(HolderComponent),
                typeof(LocalTransform),
                typeof(LocalToWorld));

            var entity = _em.CreateEntity(archetype);

            _em.SetComponentData(entity, new HolderComponent
            {
                CarryCapacity = 20,
                MoveSpeed = 5f,
                State = HolderState.Idle,
                AssignedJob = Entity.Null,
            });

            _em.SetComponentData(entity, LocalTransform.FromPosition(
                new(
                    Random.Range(-15f, 15f),
                    Random.Range(-10f, 10f),
                    0)));
        }
    }
}