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
    public class RtsPanel : MonoBehaviour, ISelectionHandler, IPointerOverUiQuery, IUiScreenRectQuery
    {
        [SerializeField] private RtsToolController _tools;

        private readonly List<(RtsTool tool, Button button)> _toolButtons = new();
        private readonly List<(BuildingKind kind, Button button)> _buildButtons = new();
        private readonly List<(RoadBrushMode mode, Button button)> _roadButtons = new();
        private readonly StringBuilder _text = new();

        private UIElementList<RtsSlotRow> _slots;
        private VisualElement _panel;
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

        public Rect UiScreenRect() =>
            UIPanelScale.TryGetScreenRect(_panel, out Rect rect) ? rect : Rect.zero;

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
            EventBus.RegisterSingleHandler<IUiScreenRectQuery>(this);

            var document = GetComponent<UIDocument>();
            UIPanelScale.ScaleWithScreen(document);
            Build(document.rootVisualElement);
        }

        private void OnDisable()
        {
            EventBus.UnregisterHandler<ISelectionHandler>(this);
            EventBus.UnregisterSingleHandler<IPointerOverUiQuery>(this);
            EventBus.UnregisterSingleHandler<IUiScreenRectQuery>(this);
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
            panel.style.width = 760;
            _panel = panel;

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
            slotScroll.style.maxHeight = 440;
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

            // Worth saying out loud rather than leaving to be discovered: a one-way bridge is unusable until
            // you know you can turn it.
            UIStyledElements.NewLabel(_buildRow, "R rotates");
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

            _text.AppendLine(DescribeMovement(agent));

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

        /// <summary>
        /// The one line that answers "why is it standing there".
        ///
        /// Every way an agent can be motionless is a different thing to fix, and they look identical on
        /// screen: inside a building, half way through a door, waiting its turn in a queue, or walking at a
        /// destination the grid has no route to. The last one is the one worth shouting about - it is what a
        /// one-way road painted across the only way in produces.
        /// </summary>
        private string DescribeMovement(Entity agent)
        {
            if (_entities.IsComponentEnabled<InsideBuilding>(agent))
            {
                return "Inside a building";
            }

            if (_entities.HasComponent<DoorUse>(agent) && _entities.IsComponentEnabled<DoorUse>(agent))
            {
                var door = _entities.GetComponentData<DoorUse>(agent);
                return $"In the doorway at {door.Cell.x},{door.Cell.y} "
                       + $"({(door.Kind == DoorUseKind.Enter ? "going in" : "coming out")}, {door.Remaining:0.0}s left)";
            }

            if (_entities.HasComponent<OnBridge>(agent) && _entities.IsComponentEnabled<OnBridge>(agent))
            {
                return DescribeCrossing(agent);
            }

            DynamicBuffer<TaskStep> steps = _entities.GetBuffer<TaskStep>(agent);
            bool atADoor = !steps.IsEmpty
                           && (steps[0].Kind == TaskStepKind.Enter || steps[0].Kind == TaskStepKind.Exit);

            if (!_entities.IsComponentEnabled<PathFollow>(agent))
            {
                return atADoor ? "Waiting for the door to be free" : "Standing";
            }

            var path = _entities.GetComponentData<PathFollow>(agent);
            string goal = $"{path.GoalCell.x},{path.GoalCell.y}";

            if (IsAtABridgeMouth(agent, path))
            {
                return $"Waiting to get on the bridge (heading for {goal})";
            }

            if (path.Holding)
            {
                return $"Queueing for {goal}, waiting {path.HoldDistance:0.#} cells out";
            }

            return HasNoRoute(agent, path)
                ? $"Walking to {goal} - NO ROUTE FROM HERE"
                : $"Walking to {goal}";
        }

        /// <summary>
        /// Where along a bridge the agent is, and whether it is moving.
        ///
        /// "Held up behind somebody" is the line worth having. A bridge is single file, so an agent stopped
        /// half way across is not stuck - it is behind one that cannot get off - and without saying so the
        /// only visible symptom is a row of agents standing on a bridge for no stated reason.
        /// </summary>
        private string DescribeCrossing(Entity agent)
        {
            var carried = _entities.GetComponentData<OnBridge>(agent);
            string headed = $"{carried.Exit.x},{carried.Exit.y}";

            if (!_entities.Exists(carried.Bridge) || !_entities.HasBuffer<BridgeOccupant>(carried.Bridge))
            {
                return $"On a bridge that no longer exists, heading for {headed}";
            }

            DynamicBuffer<BridgeOccupant> occupants = _entities.GetBuffer<BridgeOccupant>(carried.Bridge);

            for (int i = 0; i < occupants.Length; i++)
            {
                if (occupants[i].Agent != agent)
                {
                    continue;
                }

                float span = _entities.HasComponent<Bridge>(carried.Bridge)
                    ? _entities.GetComponentData<Bridge>(carried.Bridge).Span
                    : 0f;

                string place = i == 0 ? "at the front" : $"{i} behind the front";
                string held = i == 0 && span > 0f && occupants[i].Distance >= span
                    ? ", waiting for the far bank to clear"
                    : string.Empty;

                return $"Crossing a bridge to {headed} - {occupants[i].Distance:0.0}/{span:0} cells, "
                       + $"{place}{held}";
            }

            return $"On a bridge that has lost track of it, heading for {headed}";
        }

        /// <summary>
        /// Whether the agent is standing on a bridge mouth its route wants to use.
        ///
        /// The same question <c>BridgeTransitSystem</c> asks to admit an agent, asked again for the display,
        /// because it is the difference between "queueing for a bridge" and "stopped for no reason" - and both
        /// look like an agent standing still on a cell.
        /// </summary>
        private bool IsAtABridgeMouth(Entity agent, in PathFollow path)
        {
            if (!TryGetGrid(out GridMap map))
            {
                return false;
            }

            using EntityQuery query = _entities.CreateEntityQuery(ComponentType.ReadOnly<FlowFieldCache>());
            if (!query.TryGetSingleton(out FlowFieldCache fields) || !fields.IsCreated)
            {
                return false;
            }

            float2 position = _entities.GetComponentData<AgentMove>(agent).Position;
            int2 cell = GridCoords.CellOf(position);

            return (map.GetFlags(cell) & CellFlags.LinkEntry) != CellFlags.None
                   && fields.TryGetSlot(path.WaypointCell, out int slot)
                   && fields.IsLinkStep(slot, cell);
        }

        /// <summary>Whether the destination's own flow field says there is no way in from where it stands.</summary>
        private bool HasNoRoute(Entity agent, in PathFollow path)
        {
            if (!TryGetGrid(out GridMap map))
            {
                return false;
            }

            using EntityQuery query = _entities.CreateEntityQuery(ComponentType.ReadOnly<FlowFieldCache>());
            if (!query.TryGetSingleton(out FlowFieldCache fields) || !fields.IsCreated)
            {
                return false;
            }

            float2 position = _entities.GetComponentData<AgentMove>(agent).Position;
            return fields.IsKnownUnreachable(path.GoalCell, GridCoords.CellOf(position), map);
        }

        private bool TryGetGrid(out GridMap map)
        {
            using EntityQuery query = _entities.CreateEntityQuery(ComponentType.ReadOnly<GridWorld>());

            map = query.TryGetSingleton(out GridWorld grid) ? grid.Map : default;
            return map.IsCreated;
        }

        private void ShowCell(int2 cell)
        {
            _selectionTitle.text = $"Cell {cell.x}, {cell.y}";
            _slots.Clear();

            if (!TryGetGrid(out GridMap map))
            {
                _selectionBody.text = "No grid.";
                return;
            }

            CellData data = map.GetCell(cell);

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

        /// <summary>
        /// The task as the agent is actually running it, head first and marked with brackets.
        ///
        /// Printing <see cref="TaskStep.Kind"/> is not enough to read a task by: a pickup, a deposit and a
        /// shift at a workbench are all <see cref="TaskStepKind.Interact"/>, and the walk to a doorway is an
        /// ordinary <see cref="TaskStepKind.GoTo"/> - so a hauler's whole round trip printed as
        /// "GoTo -> Interact -> GoTo -> Interact", which says nothing about what it is doing or where.
        /// </summary>
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

                // The head is the step being run right now; everything after it is the plan.
                text.Append(i == 0 ? $"[{Describe(steps[0])}]" : Describe(steps[i]));
            }

            return text.ToString();
        }

        private static string Describe(in TaskStep step) => step.Kind switch
        {
            TaskStepKind.GoTo => $"GoTo {step.Cell.x},{step.Cell.y}",
            TaskStepKind.Enter => $"Enter at {step.Cell.x},{step.Cell.y}",
            TaskStepKind.Exit => $"Exit at {step.Cell.x},{step.Cell.y}",
            TaskStepKind.Interact => step.Interaction switch
            {
                InteractionKind.Pickup => "Pickup",
                InteractionKind.Deposit => "Deposit",
                InteractionKind.Work => "Work",
                _ => $"Wait {step.Duration:0.0}s",
            },
            _ => step.Kind.ToString(),
        };

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
