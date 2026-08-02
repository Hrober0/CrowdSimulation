# RTS Template – Design

Status: agreed design. Steps 0 to 2 of §14 are implemented; the rest is not yet built. Decisions recorded here are settled unless noted as *open*.

## 1. Why a grid replaces the navmesh for this game

The triangle navmesh in `Assets/Navigation` stays in the repo as a standalone module and showcase. The RTS layer does not build on it, for three reasons:

1. **Update fragility is structural.** `NavMesh.TryConnect` / `SetConnectionWithEdge` rebuild adjacency by matching `EdgeKey` (exact `float2` equality) through a spatial hash after a chunk is retriangulated. Any coordinate the triangulator emits a bit differently across a chunk border produces `Edge {edge} not found in node` and a silently disconnected graph, which surfaces much later as an unexplained path failure.
2. **Wrong cost shape for crowds.** `PathFinding.FindPath` allocates a `NativeHashMap`, `NativeHashSet` and `NativePriorityQueue` sized `nodes.Length` *per request*, then runs per-agent A*. An RTS has many agents sharing few destinations — the opposite of what this optimises.
3. **Directional routes are not expressible.** `IPathSeeker.CalculateCost(in TAttribute, float2 from, float2 to)` receives the destination node's attributes. A portal is bidirectional by construction and there is no per-edge state to hold "one way". On a grid this is 4 bits per cell.

Kept from the existing codebase: **all of `Avoidance`** (RVO2 is grid-agnostic — positions, velocities, radii), **all of `HCore`** and `CustomNativeCollections`, and the reservation semantics of `StorageSlotUtils` (`ReservedOutgoing` / `ReservedIncoming` / `TryReserveBoth` / `CancelJob`).

Dropped: the 6-state `HolderState` machine, `StorageConnectionComponent` and the connection UI (`ConnectionsTab`, `ConnectionElement`).

## 2. Module layout

```
Assets/GridNav      new asmdef -> HCore, CustomNativeCollections, Entities/Burst/Collections/Mathematics
Assets/Rts          new asmdef -> GridNav, Avoidance, HCore
Assets/Examples/Rts Assembly-CSharp: authoring, GameObject views, UI, debug overlays
Assets/Avoidance    unchanged
Assets/Navigation   unchanged, no longer used by gameplay
Assets/HCore        unchanged
```

Named `GridNav`, not `Grid` — `UnityEngine.Grid` (Tilemap) exists and would make every bare `Grid` reference ambiguous.

No references are added to existing asmdefs. Generic mechanism goes in `GridNav`; RTS-specific rules live in `Rts`; anything crossing into managed UI goes through `EventBus`.

**Coordinates.** The simulation is 2D `float2` throughout, matching `Navigation` and `Avoidance`. The view maps sim to world through a single function `SimToWorld(float2) -> Vector3`; default is XY (2D, matching the URP-2D scene template) and switching to XZ for a 3D look is a one-line change there. Nothing in the simulation knows which was chosen. Note the current example is inconsistent — `HolderMovementSystem` rotates on X/Z while `SpatialGridRebuildSystem.WorldToCell` bins on X/Y — which the single mapping function is there to prevent.

## 3. L0 — Grid

**Cell size = 1 unit. The nav grid and the building grid are the same grid.** No code ever converts between two cell spaces, which removes an entire class of bugs. Agent radius is **0.35** (inside the `0.45 x cellSize` bound of §3 with slack).

| | 1 unit (chosen) | 0.5 unit |
| --- | --- | --- |
| cells at 512x512 units | 262k = 1 MB | 1.05M = 4 MB |
| flow-field window | 128² = 48 KB | 256² = 192 KB |
| building <-> nav conversion | none | every lookup |

Consequence to design around: **a 1-cell corridor is single-file** (two agents at 0.7 diameter do not fit in 1.0 units). Two-way roads must be authored **2 cells wide**, or painted one-way.

If forests later feel too chunky at one tree per cell, do **not** subdivide the grid — lower tree cost from 255 to ~120 so one tree is slow and two block. The cost-sum model provides that granularity for free.

Chunked SoA over `int2`, 32x32 chunks. Four bytes per cell:

| field | size | use |
| --- | --- | --- |
| `CostSum` | ushort | terrain base cost + sum of the costs of all objects on the cell |
| `Flags` | byte | Building, Road, NoIdle, Entrance |
| `Exits` | byte | 4-bit allowed-exit mask N/E/S/W |

512x512 map = 1 MB. Updating an obstacle is a `CostSum` add/subtract plus a version bump — no retriangulation.

**Cost model.** `CostSum` is the exact sum of contributions, so adding an object is `+= cost` and removing it is `-= cost`. A cell is impassable when `CostSum >= BLOCKED (255)`.

`CostSum` **must be 16-bit.** With a byte, three trees at 255 each saturate at 255 and the sum can no longer be reversed — chopping one tree would either leave the cell blocked forever or wrongly open it. 16 bits keeps add/remove exact and the cell struct still fits in 4 bytes. Debug builds assert against overflow (~250 max-cost objects on one cell).

| contributor | cost |
| --- | --- |
| open ground | 0–10 |
| road | 0 (fast lane) |
| tree, rock, any single blocker | 255 (blocks the cell alone) |
| building footprint cell | 255 |

Because one tree blocks a whole cell, flow fields steer to **cell centres** and **agent radius must stay below ~0.45 x cellSize**, otherwise agents clip the corners of blocked cells.

**RVO static obstacles: buildings yes, trees no.** Blocked cells already keep *paths* out of buildings, so obstacles are not needed for routing. They are needed because RVO does not know walls exist — in a dense crowd, agents shoved sideways by neighbours get pushed *into* the footprint. So each building registers its footprint outline as one obstacle via the existing `ObstacleLookup.AddObstacle(vertices, objectId)`; L-shapes are fine, since that code already computes per-vertex `Convex` flags and handles concave outlines.

Trees stay out, on the arithmetic:

| | obstacle vertices |
| --- | --- |
| 200 buildings x ~6 verts | ~1.2k |
| 20k trees x 4 verts | ~80k, all feeding per-agent obstacle-neighbour queries |

For trees, a post-integration **clamp** covers penetration at O(1): if an agent's new position lands in a blocked cell, project it back to the nearest walkable boundary. Keep the clamp for buildings too, as a cheap safety net behind RVO.

**Directions.** `Exits` defaults to `0b1111`: unrestricted everywhere, no authoring required. One-way is a player brush that drops the mask to the allowed bit(s), with an arrow debug overlay. Flow-field generation expands *backwards* from the goal, so relaxing `c -> n` must test the **reverse** bit. This needs a unit test — a backwards one-way road is otherwise silent.

**Two version counters per chunk**, not one:

- `PassabilityVersion` — a cell crossed the blocked threshold, or its `Exits` changed. Rebuild the chunk's gates.
- `CostVersion` — bumped by *every* cost or exit change, passability-changing ones included. Invalidate flow fields.

Harvesting a forest churns cost constantly; without the split it would rebuild the navigation graph on every swing. Bumping `CostVersion` on both kinds of change is what lets each consumer watch exactly one counter.

A passability change on a chunk border also bumps the **neighbouring** chunk's `PassabilityVersion`: a border pair belongs to the gates of both chunks (§4.1), and only one of the two cells is inside the chunk that changed.

## 4. L1 — Pathfinding

Three tiers over one grid:

1. **Long range** — hierarchical A* on the chunk-gate graph (§4.1), rebuilt per chunk on `PassabilityVersion` bump.
2. **Near goal** — one integration + direction flow field per destination, shared by every agent heading there, over a bounded window (128² = 48 KB: int16 integration + byte direction). 200 haulers to one warehouse = one field. LRU cache ~32 fields = ~1.5 MB.
3. **Local** — gradient -> `PathMovement.ComputePreferredVelocity` -> RVO2 -> integrate. Existing code, reused as-is.

Division of labour: **gates cross the map, fields handle the crowded last stretch.** Without gates, a shared destination field would have to cover the whole map — 786 KB and a Dijkstra over 262k cells per destination.

### 4.1 Chunk gates (the coarse graph)

Named **`ChunkGate`**, not "portal": `Navigation.Portal` already exists and means a navmesh edge with Left/Right corners used by the funnel algorithm. Different concept, and reusing the word across two modules invites confusion.

512x512 cells in 32x32-cell chunks = 16x16 = 256 chunks. Along the border shared by two chunks there are 32 cell pairs; a pair is *open* when both cells are passable and `Exits` permits the crossing. Maximal runs of consecutive open pairs become gates:

```
chunk A | chunk B        border cells, . = open, # = blocked
   . . .|. . .   -+
   . . .|. . .    +- gate 1 (rows 0-2)
   . . .|. . .   -+
   # # #|# # #       wall
   . . .|. . .   -+
   . . .|. . .    +- gate 2 (rows 5-6)
```

- **Nodes** — gates, stored as a span plus a representative cell at the midpoint. ~1–2k map-wide.
- **Edges** — *intra*-chunk: gate P connects to gate Q when a path exists **inside that one chunk**; cost is that path's length, found by a local search over the chunk's 1024 cells. ~8 gates per chunk gives ~15k edges.

A long-range query runs: start cell -> its chunk's gates -> A* over the gate graph -> goal chunk's gates -> hand off to the flow field.

**Intra-chunk edges must be stored directed.** With one-way `Exits`, P->Q can be traversable while Q->P is not. Symmetric edges would let the coarse layer report a route the fine layer cannot walk, and the agent stalls with no visible cause.

Rebuilds stay local: placing a building dirties 1–4 chunks, so only those borders are re-scanned and a handful of 1024-cell searches re-run.

## 5. L2 — World objects

Trees, rocks and other props are entities, individually addressable so they can be harvested, clicked and damaged.

- `CellObject { int2 Cell, ushort Cost, ObjectKind }` on the object entity.
- `NativeParallelMultiHashMap<int2, Entity>` maps cell -> objects, for "what is on this cell" queries (harvest targeting, selection, land clearing).
- Add: insert into the map, `CostSum += cost`, bump the appropriate version.
- Remove: erase from the map, `CostSum -= cost`, bump.

Many objects may share a cell; the sum and the map both handle that exactly.

**Building footprints are arbitrary sets of cells** — a footprint is a list of `int2` offsets plus a rotation, so L, U and ring shapes need no special handling. Entrances are a list of `(offset, direction)` pairs. Neither the grid, the pathfinder nor the order layer cares about footprint shape.

## 6. L3 — Buildings, interiors and occupancy

Cell reservation applies **only to stationary claims** (work spots, queue slots, parking). Moving agents are resolved by RVO alone. Mixing reservation with motion is what produces deadlock, so the line is deliberate.

Every building has `Interior { Capacity, Occupied }` and entrance cells. One generic transition:

```
claim interior slot -> GoTo(entrance) -> Enter -> ...work... -> Exit(entrance)
```

`Enter` disables `PathFollow` and `AgentMove` (both `IEnableableComponent`), deregisters from the RVO agent lookup, **releases the GameObject view to the pool**, and adds `InsideBuilding { Building }`. Carried load, health and identity are untouched; one archetype, zero structural changes.

Four uses of the same mechanism: worker working inside, hauler picking up or depositing, **idle hauler resting in a haulers' hut**, soldier garrisoned.

The interior slot is claimed **before** the walk begins — the same claim-before-approach admission control as queue slots.

**Idle behaviour.** An agent with no order claims an interior slot in the nearest hut with room and goes there; self-balancing, nothing to fix when huts are built or destroyed. Ground parking on a non-`NoIdle` cell is the fallback when no hut has room. Roads and entrances carry `NoIdle`.

Performance consequence: an idle agent in a hut has no path, no steering, no RVO neighbour query, no grid cell and no view object. "Idle agents don't block others" is true by construction rather than tuned.

## 7. L4 — Storage module

Every building carries a `StorageSlot` buffer. Needs are **not** involved (see §11).

Per slot, following Captain of Industry:

| field | meaning |
| --- | --- |
| `Amount` | current units |
| `Capacity` | max units |
| `DeliverInUpTo` | request while `Amount` < this |
| `DeliverOutDownTo` | give away only while `Amount` > this |
| `Priority` | who wins when supply is scarce |
| `ReservedIn` / `ReservedOut` | in-flight commitments (existing semantics) |

**Matching rule.** A slot below `DeliverInUpTo` with `Priority > 0` posts a Haul order. A valid source is a slot holding that item that stays above its own `DeliverOutDownTo` after giving **and whose `Priority` is strictly lower than the requester's**.

Strict inequality is what makes the model provably free of ping-pong; it replaces the symmetric fill-ratio balancing of the old `ConnectionMode.TwoWays`, which invited exactly that.

**Priority 0 is reserved for pure sources.** Both thresholds must be kept — a single desired-amount cannot express "never give away inputs I have already received", and without it a priority-9 crafter strips a priority-8 crafter's input buffer and both starve.

| slot role | DeliverInUpTo | DeliverOutDownTo | Priority |
| --- | --- | --- | --- |
| mine / crafter **output** | 0 (never requests) | 0 (gives everything) | 0 |
| warehouse | Capacity | 0 | 1 |
| crafter **input** | ~2 batches | = DeliverInUpTo (never gives) | 5–9 |
| construction site | required amount | = required | 10 |

Consequences that fall out for free:

- two warehouses never trade (equal priority fails strict inequality);
- crafter inputs are never raided, and the player opts in by lowering `DeliverOutDownTo`;
- outputs never request;
- surplus drains downhill into storage whenever haulers are idle;
- a priority-8 crafter input pulls straight from a priority-0 output, skipping the warehouse round trip.

A warehouse's standing low-priority request reproduces the whole point of the old connection pairs with no pairs to author and no connection UI.

## 8. L5 — Order market

```
Order { Kind(Haul|Work|Fight), Source, Target, ItemId, Amount, Priority, ClaimedBy, PostedTime }
```

Orders are posted by **demand only — pull, never push**:

- Haul — from the storage rule in §7.
- Work — a building with a free work slot and inputs on hand.
- Fight — threat detection.

Claiming an order reserves both ends via the ported `StorageSlotUtils.TryReserveBoth`. Assignment is spatially bounded: agents consider orders within range, scored `effectivePriority / travelCost`, with a cap on claims per tick.

Congestion and deadlock, handled structurally:

| failure mode | prevention |
| --- | --- |
| everyone converges on one storage | `ReservedIn` caps commitments at real capacity; plus a hard `MaxConcurrentHaulers` per building |
| entrance pile-up | queue-slot and interior-slot claims happen **before** approach; no slot -> take another order or go idle |
| head-on jams in corridors | player-painted one-way `Exits` |
| mutual stationary block | per-agent watchdog: blocked > T seconds -> release claim, re-plan. One generic rule |
| low-priority starvation | `effective = Priority + age * agingRate` (generalises the old `LastPickupTime` tie-break) |
| item taken while hauler en route | reservations, plus the existing `ExecutePickup` shortfall path |
| ping-pong between stores | strict priority inequality (§7) |

### Mass transport — preventing the thundering herd

The failure to avoid: a demand appears (food, ammo) and every idle agent runs to the same source. Three levels prevent it.

**1. The order count is bounded by real inventory.** `ReservedOut` means if 5 bread exist, only 5 bread of orders exist. Haulers cannot over-dispatch to a source — this alone removes the stampede for hauling.

**2. Consumption is delivered, not fetched.** Agents never walk to the granary. The haulers' hut is a building with a storage slot (`DeliverInUpTo` = 10 bread, `Priority` = 6), so it requests food exactly like a crafter requests inputs. One hauler brings 20; 20 agents eat from local storage while resting inside. **20 agent round-trips collapse into 1 hauler trip**, and the eating agents never enter the road network. No new mechanism — the hut is just another requesting storage.

**3. Demand is never synchronized.** Agents spawned together with identical thresholds cross them on the same frame. Randomize each agent's need threshold by ±15% and its decay rate at spawn — one `Random` per agent at creation, permanently desynchronized. This is a requirement on the needs module (§11) even though it is built last.

**Backstop:** `MaxConcurrentVisitors` per building. Agents over the cap do not path there; they wait *inside* their hut, costing nothing per frame and invisible to traffic.

Related case: a construction site demanding 500 planks does **not** generate 500 trips. It posts the orders, but only `MaxConcurrentHaulers` are ever claimed; the rest sit unclaimed with aging. Throughput is capped by hauler count, never by demand size.

## 9. L6 — Three behaviours, one agent

One archetype: `AgentMove` (position, velocity, prefVelocity, radius, speed), `PathFollow`, `Carry`, `AssignedOrder` (`IEnableableComponent`), plus a `TaskStep` buffer (<=4 entries) running one generic step machine:

```
GoTo(target) -> Interact(target, duration) -> GoTo(target2) -> Interact(...)
```

- **worker in building**: `GoTo(slot) -> Interact(inf)` producing work ticks
- **hauler**: `GoTo(src) -> Interact(pickup) -> GoTo(dst) -> Interact(deposit)`
- **soldier**: `GoTo(enemy) -> Interact(attack)` plus a retarget rule

No per-type state machine. What `Interact` means is the only per-kind code.

## 10. View layer — GameObjects

The simulation owns all state in native memory; GameObjects are a one-way-synced view.

- A pool of prefab instances, acquired on spawn / entering view, released on despawn, on entering a building, or when culled.
- Positions and rotations written by an `IJobParallelForTransform` over a `TransformAccessArray` — the only way to move many `Transform`s without a main-thread stall.
- A `viewIndex <-> simIndex` mapping maintained on acquire/release.
- Buildings get a 1:1 static view placed once at construction; only agents need per-frame sync.
- Prefabs, animators, selection colliders and the normal editor workflow all keep working, since a view is an ordinary GameObject.

World placement goes through `SimToWorld` (§2), so 2D and 3D presentation differ only in that function.

## 11. Needs — built last, as a leaf

`NeedBuffer` -> happiness score -> global modifiers. Needs belong to agents only, are **not** consulted by order claiming, movement or production, and are the final milestone. If the module is deleted the simulation still runs.

One requirement carries back from §8: **thresholds and decay rates are randomized ±15% per agent at spawn**, so demand never synchronizes across a cohort.

## 12. Performance budget (1000 agents, 512² map)

| item | cost |
| --- | --- |
| grid | 1 MB |
| chunk-gate graph | ~1–2k nodes, ~15k directed edges |
| flow field cache | 32 x 48 KB = ~1.5 MB |
| cell -> object map | 1 hashmap entry per world object |
| idle agent in hut | ~0 per frame, no view object |
| haulers sharing one goal | 1 field regardless of count |
| view sync | 1 parallel transform job over visible agents |

## 13. World organisation and system flow

### 13.1 One world, explicit groups

The RTS runs in the **default world**. A second world was considered and rejected: the apparent win — skipping `TransformSystemGroup`, since GameObject views mean agents need no `LocalTransform`/`LocalToWorld` — disappears once you notice that agents simply *do not have* those components, so the transform systems match zero entities and cost nothing. Staying in the default world keeps subscene baking, the Entities inspector and singleton lookups working with no bootstrap machinery.

What is needed is explicit groups, one of them fixed-rate:

```
InitializationSystemGroup
  +- GridUpdateGroup     per frame   - the only grid writer      (GridNav)
SimulationSystemGroup
  +- PathfindingGroup    per frame, bounded work                 (GridNav)
  +- RtsEconomyGroup     10 Hz via ComponentSystemGroup.RateManager
  +- RtsAgentGroup       per frame
PresentationSystemGroup
  +- RtsViewGroup        per frame
```

The first two groups live in `GridNav`, not `Rts`, and are named for what they are rather than `Rts*`: `GridApplySystem` and the flow-field systems need `[UpdateInGroup]` on a type their own assembly can see, and `GridNav` does not reference `Rts`. `Rts` orders itself behind them, which it can, since the reference runs that way.

Running the economy at 10 Hz rather than 60 is a 6x cut on the most expensive matching work and is imperceptible in play.

*Revisit a second world only if* headless simulation, deterministic replay, or a simulation rate decoupled from rendering becomes a requirement.

### 13.2 The four invariants

The system order below is a consequence of these. They matter more than the list.

1. **The grid has exactly one writer.** Every mutation — trees, buildings, brush strokes — is queued and applied by `GridApplySystem`. All other systems hold `GridMap` read-only, which is what allows pathfinding, steering and integration to run in parallel against it.
2. **Reservations and amounts have disjoint writers.** `ReservedIn` / `ReservedOut` are written only by the assign and release systems; `Amount` only by `InteractionSystem`. Different fields, different phases — the race that would corrupt the reservation invariant cannot occur.
3. **Flow fields are built in one phase and read in another.** A field requested on frame N is served on frame N+1. That one frame of latency is invisible and removes every sync point between field generation and steering.
4. **Global matching is deliberately single-threaded.** `OrderAssignSystem` and the slot-claim systems run main-thread in fixed order, because reservation-with-rollback across two entities has no clean parallel form. They run at 10 Hz over a bounded candidate set; parallel work lives in the 60 Hz agent phase.

### 13.3 Frame flow

| # | system | reads | writes |
| --- | --- | --- | --- |
| **GridUpdateGroup** | | | |
| 1 | `CellObjectRegistrationSystem` | CellObject spawn/destroy | cell -> object map, cost-delta queue |
| 2 | `BuildingFootprintSystem` | placement events | cost-delta queue, RVO obstacle queue |
| 3 | `GridApplySystem` | queues | **GridMap**, chunk versions |
| **PathfindingGroup** | | | |
| 4 | `ChunkGateGraphSystem` | GridMap, `PassabilityVersion` | gate graph, dirty chunks only |
| 5 | `FlowFieldCacheSystem` | GridMap, gate graph, field requests | field cache, max N fields per frame |
| **RtsEconomyGroup — 10 Hz** | | | |
| 6 | `StorageRequestSystem` | StorageSlot | order buffer |
| 7 | `WorkRequestSystem` | WorkSlots, StorageSlot | order buffer |
| 8 | `ThreatDetectionSystem` | positions, factions | order buffer |
| 9 | `OrderAgingSystem` | orders | effective priority |
| 10 | `OrderAssignSystem` | orders, agent hash | **reservations**, ClaimedBy, TaskStep, slot claims |
| 11 | `IdleAssignSystem` | huts, agent hash | TaskStep, interior claims |
| **RtsAgentGroup — per frame** | | | |
| 12 | `AgentSpatialHashSystem` | AgentMove | agent hash |
| 13 | `TaskStepSystem` | TaskStep, arrival flags | path requests, interior queue, TaskStep |
| 14 | `PathRequestSystem` | path requests | field requests (served next frame), PathFollow |
| 15 | `PathFollowSystem` | field cache, GridMap | PrefVelocity |
| 16 | `AvoidanceSyncSystem` | AgentMove | AgentLookup, RVO velocities |
| 17 | `AgentIntegrateSystem` | GridMap | Position, **blocked-cell clamp**, arrival flags, watchdog |
| 18 | `InteriorTransitionSystem` | interior queue | enableable flags, Interior.Occupied, view release |
| 19 | `InteractionSystem` | TaskStep | **StorageSlot amounts**, Carry, health |
| 20 | `OrderCompletionSystem` | completed tasks | order release, agent idle |
| 21 | `WatchdogSystem` | watchdog timers | release claim, requeue order |
| **RtsViewGroup** | | | |
| 22 | `ViewSyncSystem` | AgentMove | pooled GameObject transforms |
| 23 | `DebugOverlaySystem` | all, read-only | gizmos |

System 13 runs **before** 17 on purpose: an arrival detected during integration is consumed at the top of the next frame. One frame of lag, no mid-frame ECB playback, and it matches the existing `ArrivalTag` pattern.

### 13.4 Parallelism

| parallel (`ScheduleParallel`) | main thread / single job |
| --- | --- |
| 12 spatial hash, 15 path follow, 16 avoidance, 17 integrate, 22 view sync | 3 grid apply, 10 order assign, 11 idle assign, 18 interior transition |

4 (gates) is parallel across dirty chunks; 5 (fields) is one job per field with a per-frame cap so a burst of new destinations cannot spike a frame.

## 14. Build order

0. **Done.** Group scaffolding: the five groups of §13.1 with the `RateManager` on `RtsEconomyGroup`, empty but ordered, so every later system lands in a defined slot.
1. **Done.** `GridNav`: `GridMap` with `ushort CostSum`, split version counters, the queue + `GridApplySystem` single-writer path, authoring, debug overlay.
2. **Done.** World objects: `CellObject`, cell -> object multi-hashmap, add/remove maintaining `CostSum`. Arbitrary building footprints. *(The RVO obstacle half of `BuildingFootprintSystem` waits for the avoidance integration in step 3; nothing owns an `ObstacleLookup` yet.)*
3. Chunk-gate graph (directed intra-chunk edges), hierarchical A*, flow-field window, grid path following on top of existing `Avoidance`. **Includes the reverse-bit one-way unit test.**
4. View layer: pooled GameObjects, `TransformAccessArray` sync, acquire/release on spawn and building entry.
5. Buildings: entrance cells, interior enter/exit, queue slots, haulers' huts, idle claiming.
6. Storage module (§7) + order market (§8) + hauler task. Port `StorageSlotUtils`.
7. Crafter: recipe, work slots, worker behaviour.
8. One-way brush + arrow overlay.
9. Soldier + threat orders.
10. Needs / happiness.

## 15. Open items

- Whether `DeliverInUpTo` / `DeliverOutDownTo` are authored in absolute units or percent of capacity (CoI offers both).
- Whether warehouse-to-warehouse rebalancing is ever wanted; today it is blocked by design and the player can force it by setting different priorities.
- Flow-field window size (128² assumed) wants measuring against real building density.
- `MaxConcurrentHaulers` / `MaxConcurrentVisitors` values are tuning, not design — start at 4 and 8 and measure.

Settled (previously open): cell size is 1 unit, shared by the nav and building grids, agent radius 0.35 (§3).
