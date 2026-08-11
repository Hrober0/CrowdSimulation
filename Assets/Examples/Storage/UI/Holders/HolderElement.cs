using HCore.UI;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace Examples.Storage.UI.Holders
{
    public class HolderElement : UIElement
    {
        // ── Child references ────────────────────────────────────────────────
        private Label _indexLabel;
        private Label _stateBadge;
        private Label _loadLabel;
        private Label _jobLabel;

        // ── State ────────────────────────────────────────────────────────────
        public Entity BoundEntity { get; private set; }

        public bool Hovered { get; private set; } = false;

        // ────────────────────────────────────────────────────────────────────

        public override void Init(VisualElement root)
        {
            base.Init(root);

            // Outer row
            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems = Align.Center;
            root.style.backgroundColor = UIColors.Surface;
            root.style.SetBorderWidth(2);
            root.style.SetBorderColor(UIColors.Border);
            root.style.SetBorderRadius(8);
            root.style.SetPadding(10);
            root.style.paddingLeft = 20;
            root.style.marginBottom = 6;

            // Hover highlight
            root.RegisterHoverEvent(h =>
            {
                root.style.backgroundColor = h ? UIColors.SurfaceHover : UIColors.Surface;
                Hovered = h;
            });

            // ── Entity index ─────────────────────────────────────────────────
            _indexLabel = UIStyledElements.NewLabel(root, "—");
            _indexLabel.style.minWidth = 140;
            _indexLabel.style.color = UIColors.TextMuted;

            // ── State badge ──────────────────────────────────────────────────
            _stateBadge = UIStyledElements.NewLabel(root, "IDLE");
            _stateBadge.style.minWidth = 180;
            _stateBadge.style.fontSize = UIColors.FontSizeXS;
            _stateBadge.style.SetPadding(4);
            _stateBadge.style.paddingLeft = 12;
            _stateBadge.style.paddingRight = 12;
            _stateBadge.style.SetBorderRadius(6);
            _stateBadge.style.unityTextAlign = TextAnchor.MiddleCenter;

            // ── Carried resource + amount ─────────────────────────────────────
            var loadGroup = UIStyledElements.NewHorizontalGroup(root);
            loadGroup.style.flexGrow = 1;
            loadGroup.style.alignItems = Align.Center;
            loadGroup.style.marginLeft = 16;

            UIStyledElements.NewLabel(loadGroup, "carrying:");
            _loadLabel = UIStyledElements.NewLabel(loadGroup, "—");
            _loadLabel.style.marginLeft = 8;
            _loadLabel.style.color = UIColors.TextPrimary;

            // ── Assigned job ──────────────────────────────────────────────────
            _jobLabel = UIStyledElements.NewLabel(root, "");
            _jobLabel.style.color = UIColors.TextMuted;
            _jobLabel.style.fontSize = UIColors.FontSizeXS;
            _jobLabel.style.minWidth = 140;
            _jobLabel.style.unityTextAlign = TextAnchor.MiddleRight;

            UIStyledElements.NewButtonDanger(root, "×", KillHolder);
        }

        // ─────────────────────────────────────────────────────────────────────

        private void KillHolder()
        {
            var em = Main.EntityManager;
            if (!em.Exists(BoundEntity)) return;

            // If a delivery job is in progress, clean up storage reservations first.
            if (em.IsComponentEnabled<DeliveryJobComponent>(BoundEntity))
            {
                var holder = em.GetComponentData<HolderComponent>(BoundEntity);
                var job    = em.GetComponentData<DeliveryJobComponent>(BoundEntity);

                bool hasSrc = em.HasBuffer<StorageSlot>(job.SourceStorage);
                bool hasDst = em.HasBuffer<StorageSlot>(job.DestStorage);

                if (hasSrc && hasDst)
                {
                    var srcSlots = em.GetBuffer<StorageSlot>(job.SourceStorage);
                    var dstSlots = em.GetBuffer<StorageSlot>(job.DestStorage);
                    StorageSlotUtils.CancelJob(ref srcSlots, ref dstSlots, job, holder.CurrentLoad);
                }
                else if (hasSrc && holder.CurrentLoad == 0)
                {
                    var srcSlots = em.GetBuffer<StorageSlot>(job.SourceStorage);
                    StorageSlotUtils.ReleaseOutgoing(ref srcSlots, job.Resource, job.ReservedAmount);
                }
                else if (hasDst)
                {
                    var dstSlots = em.GetBuffer<StorageSlot>(job.DestStorage);
                    StorageSlotUtils.ReleaseIncoming(ref dstSlots, job.Resource, job.ReservedAmount);
                }
            }

            em.DestroyEntity(BoundEntity);
        }

        // ────────────────────────────────────────────────────────────────────

        public void Refresh(Entity entity, HolderComponent h)
        {
            BoundEntity = entity;

            _indexLabel.text = $"Holder #{entity.Index}";

            // State badge — text and color from UIColors lookup
            _stateBadge.text = h.State.ToString();
            var stateColor = PanelStyles.HolderStateColor(h.State);
            _stateBadge.style.color = stateColor;
            _stateBadge.style.backgroundColor = stateColor.WithAlpha(0.12f);

            // Active row tint — left border accent when not idle
            bool active = h.State is HolderState.MovingToDestInput or HolderState.MovingToSourceInput;
            _root.style.borderLeftWidth = active ? 6 : 2;
            _root.style.borderLeftColor = active ? stateColor : UIColors.Border;

            // Carried resource
            if (h.CurrentLoad > 0)
            {
                var resourceColor = PanelStyles.ResourceColor(h.CarriedType);
                _loadLabel.text = $"{h.CarriedType.DisplayName()}  {h.CurrentLoad}";
                _loadLabel.style.color = resourceColor;
            }
            else
            {
                _loadLabel.text = "—";
                _loadLabel.style.color = UIColors.TextMuted;
            }

            // Job reference
            _jobLabel.text = h.AssignedJob != Entity.Null
                ? $"job #{h.AssignedJob.Index}"
                : "";
        }
    }
}