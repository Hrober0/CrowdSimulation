using System.Collections.Generic;
using System.Text;
using GridNav;
using HCore;
using HCore.UI;
using Rts;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

namespace Examples.Rts.UI
{
    /// <summary>
    /// The whole sandbox panel: pick a tool, pick what to build, and read what you clicked on.
    ///
    /// One panel with sections rather than tabs. There are only three things to show - tools, the selection
    /// and a few world counts - and hiding two thirds of that behind tab buttons would cost a click to learn
    /// anything.
    ///
    /// It talks to the world only through the <see cref="EventBus"/> and a plain reference to the tool
    /// controller: selection arrives as an event, and "is the pointer over me" is answered as one, so the
    /// input side needs no knowledge that a UI exists.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class RtsPanel : MonoBehaviour, ISelectionHandler, IPointerOverUiQuery
    {
        [SerializeField] private RtsToolController _tools;

        private readonly List<(RtsTool tool, Button button)> _toolButtons = new();
        private readonly List<(BuildingKind kind, Button button)> _buildButtons = new();
        private readonly List<(RoadBrushMode mode, Button button)> _roadButtons = new();
        private readonly StringBuilder _text = new();

        private UIElementList<RtsSlotRow> _slots;
        private VisualElement _buildRow;
        private VisualElement _roadRow;
        private Label _selectionTitle;
        private Label _selectionBody;
        private Label _worldCounts;

        private EntityManager _entities;
        private RtsSelection _selection;
        private bool _pointerOverUi;
        private bool _worldReady;

        public bool IsPointerOverUi() => _pointerOverUi;

        public void OnSelectionChanged(RtsSelection selection) => _selection = selection;

        private void OnEnable()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            _worldReady = world is { IsCreated: true };
            if (_worldReady)
            {
                _entities = world.EntityManager;
            }

            EventBus.RegisterHandler<ISelectionHandler>(this);
            EventBus.RegisterSingleHandler<IPointerOverUiQuery>(this);

            Build(GetComponent<UIDocument>().rootVisualElement);
        }

        private void OnDisable()
        {
            EventBus.UnregisterHandler<ISelectionHandler>(this);
            EventBus.UnregisterSingleHandler<IPointerOverUiQuery>(this);
            _pointerOverUi = false;
        }

        private void Update()
        {
            if (!_worldReady)
            {
                return;
            }

            RefreshSelection();
            RefreshWorldCounts();
        }

        // ---- layout ------------------------------------------------------------------------------------

        private void Build(VisualElement root)
        {
            VisualElement panel = UIStyledElements.NewContainer(root);
            panel.style.width = 380;

            panel.RegisterCallback<PointerEnterEvent>(_ => _pointerOverUi = true);
            panel.RegisterCallback<PointerLeaveEvent>(_ => _pointerOverUi = false);

            UIStyledElements.NewHeader(panel, "RTS Sandbox");

            BuildToolRow(panel);
            BuildBuildRow(panel);
            BuildRoadRow(panel);

            UIStyledElements.NewDivider(panel);

            _selectionTitle = UIStyledElements.NewSubHeader(panel, "Nothing selected");
            _selectionBody = UIStyledElements.NewLabel(panel, "Click the world with the Inspect tool.");

            ScrollView slotScroll = UIStyledElements.NewScrollView(panel);
            slotScroll.style.maxHeight = 220;
            _slots = new UIElementList<RtsSlotRow>(slotScroll.contentContainer);

            UIStyledElements.NewDivider(panel);

            UIStyledElements.NewSubHeader(panel, "World");
            _worldCounts = UIStyledElements.NewLabel(panel, "");

            SelectTool(RtsTool.Inspect);
        }

        private void BuildToolRow(VisualElement panel)
        {
            VisualElement row = UIStyledElements.NewHorizontalGroup(panel);

            foreach (RtsTool tool in new[]
                     {
                         RtsTool.Inspect, RtsTool.Build, RtsTool.Road, RtsTool.Demolish, RtsTool.SpawnAgent,
                     })
            {
                RtsTool captured = tool;
                Button button = UIStyledElements.NewButton(row, Label(tool), () => SelectTool(captured));
                _toolButtons.Add((tool, button));
            }
        }

        private void BuildBuildRow(VisualElement panel)
        {
            _buildRow = UIStyledElements.NewHorizontalGroup(panel);

            foreach (BuildingBlueprint blueprint in BuildingCatalog.All)
            {
                BuildingKind captured = blueprint.Kind;
                Button button = UIStyledElements.NewButton(_buildRow, blueprint.Name, () => SelectBuilding(captured));
                button.style.color = blueprint.Tint;
                _buildButtons.Add((blueprint.Kind, button));
            }
        }

        private void BuildRoadRow(VisualElement panel)
        {
            _roadRow = UIStyledElements.NewHorizontalGroup(panel);

            foreach (RoadBrushMode mode in new[]
                     {
                         RoadBrushMode.TwoWay, RoadBrushMode.OneWay, RoadBrushMode.Erase,
                     })
            {
                RoadBrushMode captured = mode;
                Button button = UIStyledElements.NewButton(_roadRow, mode.ToString(), () => SelectRoad(captured));
                _roadButtons.Add((mode, button));
            }
        }

        // ---- tool selection ----------------------------------------------------------------------------

        private void SelectTool(RtsTool tool)
        {
            if (_tools != null)
            {
                _tools.Tool = tool;
            }

            foreach ((RtsTool candidate, Button button) in _toolButtons)
            {
                Highlight(button, candidate == tool);
            }

            _buildRow.SetActive(tool == RtsTool.Build);
            _roadRow.SetActive(tool == RtsTool.Road);

            if (tool == RtsTool.Build)
            {
                SelectBuilding(_tools != null ? _tools.BuildKind : BuildingKind.Farm);
            }
            else if (tool == RtsTool.Road)
            {
                SelectRoad(_tools != null ? _tools.RoadMode : RoadBrushMode.TwoWay);
            }
        }

        private void SelectBuilding(BuildingKind kind)
        {
            if (_tools != null)
            {
                _tools.BuildKind = kind;
            }

            foreach ((BuildingKind candidate, Button button) in _buildButtons)
            {
                Highlight(button, candidate == kind);
            }
        }

        private void SelectRoad(RoadBrushMode mode)
        {
            if (_tools != null)
            {
                _tools.RoadMode = mode;
            }

            foreach ((RoadBrushMode candidate, Button button) in _roadButtons)
            {
                Highlight(button, candidate == mode);
            }
        }

        private static void Highlight(Button button, bool active)
        {
            button.style.backgroundColor = active ? UIColors.AccentSurface : UIColors.Surface;
        }

        private static string Label(RtsTool tool) => tool switch
        {
            RtsTool.Inspect => "Inspect",
            RtsTool.Build => "Build",
            RtsTool.Road => "Road",
            RtsTool.Demolish => "Demolish",
            RtsTool.SpawnAgent => "Agents",
            _ => tool.ToString(),
        };

        // ---- inspector ---------------------------------------------------------------------------------

        private void RefreshSelection()
        {
            switch (_selection.Kind)
            {
                case SelectionKind.Building when _entities.Exists(_selection.Entity):
                    ShowBuilding(_selection.Entity);
                    return;

                case SelectionKind.Agent when _entities.Exists(_selection.Entity):
                    ShowAgent(_selection.Entity);
                    return;

                case SelectionKind.Cell:
                    ShowCell(_selection.Cell);
                    return;

                default:
                    _selectionTitle.text = "Nothing selected";
                    _selectionBody.text = "Click the world with the Inspect tool.";
                    _slots.Clear();
                    return;
            }
        }

        private void ShowBuilding(Entity building)
        {
            string name = _entities.HasComponent<BuildingLabel>(building)
                ? BuildingCatalog.Of(_entities.GetComponentData<BuildingLabel>(building).Kind).Name
                : "Building";

            _selectionTitle.text = $"{name}  (entity {building.Index})";

            _text.Clear();

            if (_entities.HasComponent<Interior>(building))
            {
                var interior = _entities.GetComponentData<Interior>(building);
                _text.AppendLine($"Inside: {interior.Occupied} here, {interior.Claimed} claimed of {interior.Capacity}");
            }

            if (_entities.HasComponent<Recipe>(building))
            {
                var recipe = _entities.GetComponentData<Recipe>(building);
                _text.Append("Recipe: ");
                _text.Append(Describe(_entities.GetBuffer<RecipeInput>(building)));
                _text.Append(" -> ");
                _text.Append(Describe(_entities.GetBuffer<RecipeOutput>(building)));
                _text.AppendLine($"  ({recipe.CraftSeconds:0.#}s)");
            }

            if (_entities.HasBuffer<BuildingEntranceCell>(building))
            {
                DynamicBuffer<BuildingEntranceCell> doors = _entities.GetBuffer<BuildingEntranceCell>(building);
                if (!doors.IsEmpty)
                {
                    _text.AppendLine($"Door at {doors[0].Cell.x}, {doors[0].Cell.y}");
                }
            }

            _selectionBody.text = _text.ToString().TrimEnd();

            if (_entities.HasBuffer<StorageSlot>(building))
            {
                // AsNativeArray is a view onto the buffer, so refreshing the panel every frame allocates
                // nothing - which matters when the panel is the thing you leave open while watching.
                _slots.SetElements(_entities.GetBuffer<StorageSlot>(building).AsNativeArray(),
                                   (row, slot) => row.Refresh(slot));
            }
            else
            {
                _slots.Clear();
            }
        }

        private void ShowAgent(Entity agent)
        {
            _selectionTitle.text = $"Agent {agent.Index}";
            _slots.Clear();
            _text.Clear();

            if (_entities.HasComponent<AgentMove>(agent))
            {
                var move = _entities.GetComponentData<AgentMove>(agent);
                _text.AppendLine($"At {move.Position.x:0.0}, {move.Position.y:0.0}   speed {math.length(move.Velocity):0.0}");
            }

            if (_entities.HasComponent<Carry>(agent))
            {
                var carry = _entities.GetComponentData<Carry>(agent);
                _text.AppendLine(carry.IsEmpty
                                     ? $"Carrying nothing (holds {carry.Capacity})"
                                     : $"Carrying {carry.Amount} {ItemCatalog.Name(carry.Item)}");
            }

            if (_entities.IsComponentEnabled<InsideBuilding>(agent))
            {
                _text.AppendLine("Inside a building");
            }

            _text.AppendLine(_entities.IsComponentEnabled<AssignedOrder>(agent)
                                 ? DescribeOrder(_entities.GetComponentData<AssignedOrder>(agent))
                                 : "No order");

            _text.Append("Task: ");
            _text.AppendLine(DescribeTask(_entities.GetBuffer<TaskStep>(agent)));

            if (_entities.HasComponent<MovementWatchdog>(agent))
            {
                float stalled = _entities.GetComponentData<MovementWatchdog>(agent).StalledSeconds;
                if (stalled > 0.5f)
                {
                    _text.AppendLine($"Stuck for {stalled:0.0}s");
                }
            }

            _selectionBody.text = _text.ToString().TrimEnd();
        }

        private void ShowCell(int2 cell)
        {
            _selectionTitle.text = $"Cell {cell.x}, {cell.y}";
            _slots.Clear();

            using EntityQuery query = _entities.CreateEntityQuery(ComponentType.ReadOnly<GridWorld>());
            if (!query.TryGetSingleton(out GridWorld grid) || !grid.Map.IsCreated)
            {
                _selectionBody.text = "No grid.";
                return;
            }

            CellData data = grid.Map.GetCell(cell);

            _text.Clear();
            _text.AppendLine($"Cost {data.CostSum}   {(data.IsPassable ? "passable" : "BLOCKED")}");
            _text.AppendLine($"Flags: {(data.Flags == CellFlags.None ? "none" : data.Flags.ToString())}");
            _text.AppendLine(data.IsOneWay ? $"One-way, no exit {ForbiddenExits(data)}" : "Two-way");

            _selectionBody.text = _text.ToString().TrimEnd();
        }

        // ---- world -------------------------------------------------------------------------------------

        private void RefreshWorldCounts()
        {
            using EntityQuery agents = _entities.CreateEntityQuery(ComponentType.ReadOnly<AgentMove>());
            using EntityQuery buildings = _entities.CreateEntityQuery(ComponentType.ReadOnly<BuildingPlacement>());
            using EntityQuery working = _entities.CreateEntityQuery(ComponentType.ReadOnly<AssignedOrder>());
            using EntityQuery books = _entities.CreateEntityQuery(ComponentType.ReadOnly<OrderBook>());

            int open = books.TryGetSingleton(out OrderBook book) && book.IsCreated ? book.Length : 0;

            _worldCounts.text =
                $"Agents {agents.CalculateEntityCount()}   "
                + $"Buildings {buildings.CalculateEntityCount()}   "
                + $"Busy {working.CalculateEntityCount()}   "
                + $"Open orders {open}";
        }

        // ---- text --------------------------------------------------------------------------------------

        private static string DescribeOrder(in AssignedOrder order) => order.Kind switch
        {
            OrderKind.Haul => $"Hauling {order.Amount} {ItemCatalog.Name(order.Item)}",
            OrderKind.Work => "Working a shift",
            _ => order.Kind.ToString(),
        };

        private static string DescribeTask(in DynamicBuffer<TaskStep> steps)
        {
            if (steps.IsEmpty)
            {
                return "idle";
            }

            var text = new StringBuilder();
            for (int i = 0; i < steps.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(" -> ");
                }

                text.Append(steps[i].Kind);
            }

            return text.ToString();
        }

        private static string Describe(in DynamicBuffer<RecipeInput> inputs)
        {
            if (inputs.IsEmpty)
            {
                return "nothing";
            }

            var text = new StringBuilder();
            for (int i = 0; i < inputs.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(" + ");
                }

                text.Append($"{inputs[i].Amount} {ItemCatalog.Name(inputs[i].Item)}");
            }

            return text.ToString();
        }

        private static string Describe(in DynamicBuffer<RecipeOutput> outputs)
        {
            var text = new StringBuilder();
            for (int i = 0; i < outputs.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(" + ");
                }

                text.Append($"{outputs[i].Amount} {ItemCatalog.Name(outputs[i].Item)}");
            }

            return text.ToString();
        }

        private static string ForbiddenExits(CellData data)
        {
            var text = new StringBuilder();
            for (int i = 0; i < DirectionUtils.DIRECTION_COUNT; i++)
            {
                var direction = (Direction)i;
                if (!data.CanExit(direction))
                {
                    text.Append(direction).Append(' ');
                }
            }

            return text.ToString().TrimEnd();
        }
    }
}
