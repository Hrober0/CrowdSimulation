# RTS Template – Design

Status: agreed design. Steps 0 to 8 of §14 are implemented, plus the sandbox scene of §14.1, the doorway work of §14.2 and the bridges of §14.3; steps 9 and 10 are not yet built. Decisions recorded here are settled unless noted as *open*.

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

**Cell size = 1 unit. The nav grid and the building grid are the same grid.** No code ever converts between two cell spaces, which removes an entire class of bugs. Agent radius is **0.42** (inside the `0.45 x cellSize` bound of §3, and deliberately near it: a body that nearly fills its cell is what makes a crowd read as bodies instead of as points, and two of them still pass through a one-cell doorway).

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
Order { Kind(Haul|Work|Fight), Source, Target, ItemId, Amount, Priority, ClaimedBy, PostedTime, LastClaimedTime }
```

Orders are posted by **demand only — pull, never push**:

- Haul — from the storage rule in §7.
- Work — a building with a free work slot and inputs on hand.
- Fight — threat detection.

Claiming an order reserves both ends via the ported `StorageSlotUtils.TryReserveBoth`. Orders are taken in effective-priority order and given to the nearest agent that can reach the first leg, with a cap on claims per tick.

**Not spatially bounded, deliberately.** This said "agents consider orders within range" and there was a sixty-four cell limit implementing it, on the reasoning that beyond it somebody nearer should take the job. It never chose between two agents. The nearest reachable agent anywhere *is* the nearest reachable agent in range whenever one is in range, so the limit only ever fired when nobody was — and then it did not defer the work to somebody nearer, it refused the order. Refused it identically on the next tick, and every tick after, because nothing about the geometry had changed: a permanent deadlock wearing the face of an order patiently waiting its turn. A radius is only a preference if something happens when it is empty. Distance still decides *who* goes; it no longer decides whether anyone does.

The scan is ordered cheap-test-first as a result — squared distance, then reachability only for an agent that is a new nearest. Reachability is a hash probe and a pair of field reads, so gating it on being a new leader turns roughly one probe per agent into roughly one per new nearest, and the unbounded search costs less than the bounded one did.

Congestion and deadlock, handled structurally:

| failure mode | prevention |
| --- | --- |
| everyone converges on one storage | `ReservedIn` caps commitments at real capacity; plus a hard `MaxConcurrentHaulers` per building |
| entrance pile-up | interior-slot claims happen **before** approach; no slot -> take another order or go idle. The queue-slot half is `ArrivalQueueSystem` (below) |
| head-on jams in corridors | player-painted one-way `Exits` |
| mutual stationary block | per-agent watchdog: blocked > T seconds -> release claim, re-plan. One generic rule |
| low-priority starvation | `effective = Priority + age * agingRate` (generalises the old `LastPickupTime` tie-break) |
| one of two equal claimants always wins | age is measured from `LastClaimedTime`, not `PostedTime` — see below |
| an order nobody stands near is never taken | no distance veto on assignment; the nearest reachable agent takes it however far away it is |
| item taken while hauler en route | reservations, plus the existing `ExecutePickup` shortfall path |
| ping-pong between stores | strict priority inequality (§7) |

### Aging measures time since served, not time since asked

Aging was written to stop a low-priority order starving behind a high-priority one, and it does. What it did not do was stop an order starving behind an **equal** one, because age measured from posting is never spent.

Two warehouses of the same priority, both posted in the same request pass, tie exactly, and the sort settles a tie the same way every tick. Even a tenth of a second between their posts is no better: both climb at the same rate, so the gap is a constant, and the sort only reads its sign. The leader wins every tick until it is *fully* satisfied and retired — and a warehouse's standing "always wants more" request is never fully satisfied, so it never yields and its equal never gets a turn. Nothing about this is a tie-break accident; being served simply cost an order nothing, so there was no mechanism by which taking turns could happen.

`LastClaimedTime` is the fix and it is one field: seeded to the post time, stamped on every claim, and what `effective` ages from. Service now costs an order the age it had banked, so two equally hungry buildings alternate, while an order nobody touches climbs exactly as it did before. It is also the more honest metric — *time since anything happened* is what starvation means, and a five-hundred-plank order being delivered ten at a time is not starving however long ago it was posted.

Work orders were already covered by accident: a claimed one is zeroed, pruned, and reposted fresh next tick, so its age resets on its own. The frozen-order case belongs to hauls that are never fully satisfied.

One known wart: an order whose claim later fails — the agent is cut short, the target is demolished — has already spent its age on a haul that never happened. It re-posts with the stock given back, so the cost is bounded by the aging rate and only appears when tasks are being cut short.

### Doorway contention — the queue slot, and why it is not a claim

The interior-slot claim caps how many agents may be **inside** a building. Nothing capped how many may crowd its **step**, and that is a separate jam: several agents converging on one cell are handed mirror-image ORCA constraints, so each gives way to the others and none of them arrives. It is not only an entering problem — a hauler stands on a doorstep for the length of a pickup without ever going in, so the busiest doors belong to agents that never use them.

`ArrivalQueueSystem` is the queue-slot half. Two departures from the row above:

- **It is not written in terms of doors.** The contended thing is a destination cell that agents have to occupy; a doorway is the case that motivated it, not the mechanism. Agents within 5 cells of their goal are ranked by **how far the rest of the walk costs in that destination's flow field**, the front one walks in, and each of the rest is given a distance it may not come closer than. The line therefore forms along the road the traffic actually arrives on, and nothing has to know which wall the door is in.
- **It is not a claim.** Rank is recomputed from the world every frame and nothing is stored. A reservation would need a release path for every way an agent can stop wanting the door — death, re-tasking, the watchdog giving up — which is the same leak the interior claim needs a rule in `IdleAssignSystem` to guard against (§14 step 8). There is nothing to leak if nothing is held.

Two things about *how* it ranks are load-bearing, and both were learned by getting them wrong (§14.2):

**The order comes from the field, not from a straight line.** The field for the destination is already built and shared by everyone walking to it, so the exact remaining path cost is one array read per agent — and it is the only measure that survives an awkward approach. Straight-line distance makes the agent on the wrong side of a one-way road the front of the queue, and then nobody who *can* get in is allowed to try. An agent whose cost reads unreachable is not a contender at all; it is the watchdog's business rather than a queue's.

**A place in the line is a distance, not a point.** Every geometric guess at where an agent should wait is wrong as soon as the way in is not a straight line: a point back along the line to the goal can land in a wall, across a forbidden edge, or off the route the agent was walking. "No closer than this" leaves *how* to the field, which is the one thing that knows the way in — so an agent too close to its place steps back up the field, along a route it can come back down.

**Waiting is worth something, or the queue is a race.** Cost alone is re-run from scratch every frame, and the nearest agent wins it every time: a trickle of arrivals nearer than whoever is waiting takes the front indefinitely, and the agent already there is never served. It is not stuck — it is being politely overtaken forever, which is worse, because nothing anywhere reports it. So seconds spent queueing buy steps of closeness, at the same **2 steps per second** for everybody, exactly as `OrderAgingSystem` ages a waiting order into its priority. Two properties fall out and both matter: an agent behind by N steps reaches the front in at most N/2 seconds *whatever else arrives*, and since everyone in the line ages at the same rate the ordering **within** the line never changes — aging decides the line against newcomers and nothing else.

**Only ranks behind the front are exempt from the watchdog**, and that is what stops a queue deadlocking. The agent at the front is still watched, so a head that is genuinely wedged is still given up on and the line moves up — the queue cannot outlive the thing it is queueing for.

Doorway walks also use a wider arrival radius (`TaskStep.GoToDoor`, 1.1 cells) than a plain one. Nothing about a doorway needs the exact cell — `InteractionSystem` and `InteriorTransitionSystem` both work off the agent's *entity*, never its position — and insisting on the centre is what made every agent bound for a building steer at one point.

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
3. **Done.** Chunk-gate graph (directed intra-chunk edges), hierarchical A*, flow-field window, grid path following on top of existing `Avoidance`. **Includes the reverse-bit one-way unit test.**

   Two things settled while building it. **Which tier steers an agent is decided geometrically:** if the agent stands inside the window the goal's field covers it follows that field, otherwise it steers at the far side of the next gate — so a waypoint is always a *cell*, and everyone crossing one gate shares one field for it exactly as haulers share a warehouse's. Routes are recomputed on chunk change rather than tracked along, which self-corrects when avoidance pushes an agent off course. **Local steering was written in `Rts`** rather than reusing `Navigation.PathMovement`: `Rts` does not reference `Navigation`, and §2 says gameplay no longer builds on it.
4. **Done.** View layer: pooled GameObjects, `TransformAccessArray` sync, acquire/release on spawn and building entry.

   The seam between simulation and view is one enableable tag, `ViewVisible`, and it lives in `Rts`. The simulation answers "should this be shown at all" — an agent inside a building has it disabled (§6) — and the view layer answers "is it close enough to be worth a GameObject". Keeping the second question out of the simulation is what lets the camera move without touching simulation state. Building entry therefore needs no view code in step 5: disabling the tag is the whole change.

   Three things settled while building it. **A frame is stated, not tracked**: `BeginFrame` / `Show` per wanted agent / `EndFrame` releases the rest, so the pool cannot drift out of sync with the world the way an event-per-appearance scheme can. **Culling is a radius around a focus point, not a frustum** — one distance test per agent, identical under an XY or XZ `SimToWorld`, and it bounds the GameObject count just as well as a frustum would. **Pooled instances are left as scene roots**, because `IJobParallelForTransform` only parallelises over root transforms and a tidy parent container would silently serialise the entire sync.

   `SimToWorld.Rotation` was rebuilt out of `math` calls instead of `Quaternion.LookRotation`, which is an engine extern and cannot be called from the Burst job that writes the transforms.

   Also added, as example scaffolding rather than design: `AgentSpawnerAuthoring` — a crowd scattered over a box, all walking to one goal, placed only on passable cells. Nothing in the view layer can be seen working until something puts agents on the map, and the real game will spawn them from buildings.
5. **Done.** Buildings: entrance cells, interior enter/exit, queue slots, haulers' huts, idle claiming. *(The 1:1 static building view §10 asks for was missed here and added later — see §14.1.)*

   **`Interior` has three fields, not two.** "The interior slot is claimed before the walk begins" needs somewhere to record a claim that is not yet an occupant, so `Occupied` (physically inside, what production and display want) is joined by `Claimed` (inside *plus* walking here). `Claimed` is the one capped by `Capacity`: capping occupants instead would let ten agents walk to a hut with two beds and eight arrive to be turned away at the door, which is exactly the entrance pile-up §8 exists to prevent.

   **Queue slots turned out to be the interior claim.** They were listed here as a separate mechanism, and once claim-before-approach exists there is nothing left for them to do: an agent that cannot get a slot never sets off, so no queue forms outside to need slots. Agents waiting for room wait *inside* their own hut, which is §8's point about the thundering herd. Reintroduce them only if something needs agents queued outside a building it has already been admitted to.

   **`AgentMove` became `IEnableableComponent`.** §6 called for it, and it is what makes "an idle agent in a hut costs nothing" structural rather than tuned: every movement system queries `AgentMove`, so one flag removes the agent from the spatial hash, from everyone else's avoidance neighbours, from integration and from the view at once — with its position, load and health untouched for when it comes back.

   **Entrances are authored as a wall cell plus a side**, not as the outside cell. The cell an agent stands on is then derived, which makes "the doorstep is outside the footprint and therefore passable" a consequence of the authoring rather than something the author has to keep true by hand — including after rotation. Doorsteps are flagged `Entrance | NoIdle`; the `NoIdle` half is the one that matters at runtime.

   Resolving entrances lives in `BuildingFootprintSystem` rather than in a system of its own, so that one place both takes and gives back everything a building touches — a split would eventually leak half a building's cells on demolition.

   Nothing queues an `Exit` yet. Idle agents rest in huts until an order arrives, and orders are step 6; `Exit` is implemented and unit-tested but unreachable in play until then.
6. **Done.** Storage module (§7) + order market (§8) + hauler task. Port `StorageSlotUtils`.

   **One order per (target, item), carrying the whole outstanding need** — not one order per unit. §8's construction site wanting 500 planks is one order of 500. Nothing is gained by a 500-entry queue: the units in flight are already capped by the reservations and the trips by `HaulLimit`, so the only thing per-unit orders would add is 500 things to sort. It also makes aging work, because an unmet request is *the same order* a tick older rather than a fresh one that has forgotten how long it has been waiting.

   **Haulers pick up and deposit on the doorstep, not inside.** §6 lists "hauler picking up or depositing" among the uses of the interior mechanism, and that is still available — the steps exist. But claiming an interior slot at the destination *before* walking to the source means holding a slot at one building for the whole outbound leg at another, which is a worse deadlock than the pile-up it prevents. `HaulLimit` already bounds the crowd at a door, so the doorstep interaction is what step 6 builds. Revisit if haulers need to be *hidden* indoors while loading.

   **Interactions go through a queue, like interior transitions.** The step machine says *that* something finished; `InteractionSystem` decides what it costs. That is what makes §13.2 invariant 2 checkable rather than merely intended: `Amount` has exactly one writer, and it is not in the 60 Hz parallel phase.

   **`AssignedOrder.Amount` is the outstanding reservation, not the original claim.** It is set to what was actually picked up (which can be less — something may eat the stock while the hauler walks) and zeroed by a successful deposit. "Nothing reserved and nothing carried" is then the finished case, and every other way a task can end unwinds through one `CancelHaul`. Without that distinction the release path cannot tell a completed delivery from a haul that never collected anything, and silently leaks reservations.

   `TaskStep`'s inline capacity went from 4 to 6: a hauler woken from a hut is `Exit` plus the four-step hauler task, and idle agents are *in* huts, so that is the common case.

   Not built here, and still owed: `WatchdogSystem` (§13.3 #21). Every other row of §8's table of jams is now structural, but "blocked > T seconds → release claim, re-plan" has nothing to hang on until agents can be blocked by things other than walls — which is what the one-way brush of step 8 introduces.
7. **Done.** Crafter: recipe, work slots, worker behaviour.

   **A work slot *is* an interior slot.** §13.3 #7 names "WorkSlots" as a separate input, and it turned out there was nothing for a second occupancy mechanism to do: a worker claims room inside a workshop exactly as an idle agent claims a bed in a hut, and the same enter/exit path releases it. `Interior.Capacity` on a crafter is its number of benches. This is the fourth use of the one mechanism §6 promised, and it needed no new code at all.

   **A crafter needs no special handling in the economy.** Its input slot is an ordinary requesting slot, so haulers fill it; its output slot is an ordinary pure source, so haulers drain it. The recipe only says what becomes what. Feeding a workshop from a farm and hauling its bread away needs nothing authored beyond four threshold numbers — there is a test for exactly that.

   **`Interact(inf)` is a re-queueing loop, not an infinite duration.** When a batch finishes, `InteractionSystem` asks whether another could follow and either queues one more `Interact` or queues the `Exit`. So the worker stays put for a whole shift rather than walking out and back in per loaf, and it leaves precisely when the building stops asking for it — the same condition, evaluated by the same function, so the worker and the building can never disagree about whether there was work.

   Two claim-lifecycle defects surfaced while building it, both now fixed and covered:

   - Leaving a building cleared the agent's interior claim unconditionally. But a hauler resting in a hut has its claim *moved* to its new destination before it walks out, so the exit was dropping the new claim on the floor. It now only clears a claim on the building actually being left.
   - A claim taken but never used — the task cut short before the worker got through the door — leaked a bench permanently. `OrderCompletionSystem` now returns it, and can tell the two cases apart because a claim that *was* used is already gone by the time the steps run out.

   Also: exiting a building that has since been demolished used to fail its liveness check and leave the agent trapped inside it. Only the agent has to still exist now.
8. **Done.** One-way brush + arrow overlay. Includes `WatchdogSystem` (§13.3 #21), which was owed from step 6. *(This built the one-way brush but no brush that lays a road down at all; that was added later — see §14.1.)*

   **Painting a cell forbids the reverse direction; it does not permit only one.** §3 says the brush "drops the mask to the allowed bit(s)", and the allowed bits turn out to be three of the four. A strict single-bit mask would also forbid stepping sideways off the road, so an agent could enter a one-way road and never leave it — the feature meant to unjam corridors would strand everyone who used one. Forbidding the reverse gives what the player actually wants: traffic that cannot double back, on a road you can still get on and off.

   The brush is a **drag gesture**: drag along a road and each cell is painted with the direction of travel, right-click clears a cell back to two-way. Direction comes from the dominant axis of the step, so a fast drag that skips cells still paints something sensible. A corner painted twice keeps the later direction, which is the way out of it.

   The overlay marks the **edge** an agent may not cross, as a red wedge standing on that edge with its point at the cell centre. Arrows were tried both ways round and are wrong both ways round: drawing the permitted directions puts three on every one-way cell and leaves the reader to spot the missing one, and drawing the forbidden direction puts a single arrow on the cell pointing *against* the traffic, which reads as the road running backwards — the first thing anyone asked about it was whether the arrows were inverted. A shape with no direction in it cannot be read backwards; the closed side is the side with the wedge on it.

   A related bug the wedge made visible: the leading cell of a one-way drag was painted with a hardcoded direction, because on mouse-down there is no previous cell to derive one from. It is now laid two-way and its exit mask corrected — with `SetExits` alone, which is an absolute write, since repainting a cell whose first paint is still queued would take the road discount twice — as soon as the drag reveals which way it went. A single click that never reveals a direction stays two-way, which is the honest answer rather than an arbitrary one.

   **The watchdog is the only rule in §8's table of jams that reacts rather than prevents, and that is inherent.** Every other row is prevented structurally — reservations cap commitments, claims precede approach, strict priority forbids ping-pong — but two agents wedged in a corridor is a geometry problem, and geometry cannot be reasoned about in advance. So it is noticed instead: no progress for five seconds while trying to walk, and the task is dropped.

   Dropping it is all the watchdog does. Everything after that is machinery that already existed — `OrderCompletionSystem` unwinds the reservations and the interior claim, `StorageRequestSystem` re-posts the demand because the demand never went away, and the agent is picked up as free. There is no "recovering" state and nothing had to be taught what a deadlock is.

   Progress is measured against **the last place the agent actually got to**, not against last frame. A frame-to-frame test would read RVO jitter as progress forever, and an agent shuffling on the spot in a jam is precisely the case this exists to catch.

   One gap this closed on the way: a claim whose task died left the building a bench or bed short permanently. Releasing it is now a rule in `IdleAssignSystem` — a claim with no task behind it is stale — rather than something each of the several ways a task can end has to remember.
9. Soldier + threat orders.
10. Needs / happiness.

### 14.1 Sandbox scene — done

Everything below was once listed here as "out of scope, but required": scene and asset work that no code step owned, and without which every step above stays invisible. The claim that it *could not be written from here* was wrong. An editor script can author a scene, and generating it beats hand-editing scene YAML in every way that matters — it is reviewable as source, and it can be rebuilt after any change to the example instead of drifting from it.

`Assets/Examples/Rts/Editor/RtsSceneBuilder.cs` is that script (`Tools/RTS/Build Sandbox Scene`). It writes the prefabs, their materials, a `PanelSettings`, and `RtsSandbox.unity`. Views are tinted unlit quads: no sprite assets, no lighting, nothing to import, and one colour per thing is the fastest way to tell agents apart while watching them.

`RtsStartWorld` lays out a working economy on Play — Farm → Mill → Bakery → Warehouse plus a hut for the haulers, which between them exercise two crafting stages, three hauls, strict priority, reservations, claim-before-approach and the concurrency cap. **F5 rebuilds it.** That is also the answer to "save and load": a real save would mean serialising chunked native grid memory and every component of every entity, for a sandbox whose whole point is that any situation can be reconstructed in seconds. Rebuilding *is* the load, through the same code path the first frame uses.

Two things about the rebuild were not obvious and are now enforced in code:

- **Demolition and placement cannot share a frame.** The grid has one writer per frame (§13.2), so a building placed where one just stood cancels its own refund out and leaves the cell walkable but unflagged. There is a one-frame gap between clearing and laying.
- **Ground cost is painted once, ever.** It is a cost delta like every other contribution, so re-painting it per rebuild makes the world monotonically more expensive until nothing is passable.

Ground is laid down deliberately expensive because a road is a *discount* (§3), and a discount off zero changes no routing at all.

Interaction is one tool controller — inspect, build, demolish, road, spawn — which is the single owner of world mouse input, and a UI Toolkit panel built on the existing `HCore.UI` kit. Clicking a building, an agent or a bare cell publishes an `ISelectionHandler` event through the `EventBus`; the panel is a subscriber, not the thing doing the picking, so the inspector is decoupled from the input that feeds it.

Two gaps in earlier steps had to close before any of this could be watched, and they are **retro-fixes to those steps, not scene dressing**:

- **Step 5 owed building views.** §10 specifies a 1:1 static view per building and nothing implemented it — the view layer built in step 4 handled pooled agents only. `BuildingViewSystem` now creates one GameObject per building, sized from the footprint extent and tinted per kind, destroyed on demolish via a cleanup component.
- **Step 2 owed world objects a view.** §10 asks for a view per thing on the map and only agents and buildings ever got one, so a tree was 255 of cost and nothing else — invisible unless the grid overlay was on, where it showed as an unexplained red square that no tool could remove. `CellObjectViewSystem` is `BuildingViewSystem`'s twin (one instance on appearance, destroyed on removal, through a cleanup component) and draws a disc, so round things read as round and square things as square. The demolish tool clears them through the cell → object map, which is what §5 lists that map as being for.
- **Step 8 owed roads themselves.** Step 8 built the *one-way* brush, but there was no brush that laid a road down in the first place, so the discount §3 is built around had never been exercised outside tests. `RoadBrush` now covers both: two-way lays the discount and the `Road`/`NoIdle` flags, one-way adds the reverse-forbid mask, erase gives it all back. Painting is idempotent on the `Road` flag, so dragging over a cell twice does not stack the discount.

Still genuinely out of scope, and unchanged:

| what | why |
| --- | --- |
| a subscene-authored map (`GridAuthoring`, `AgentSpawnerAuthoring`, `StorageAuthoring` placed by hand) | the sandbox builds its world from code instead; authoring is for a real level, which is content work |
| tuning passes on `MaxConcurrentHaulers` / `MaxConcurrentVisitors`, flow-field window size | needs a real map to measure against (§15) |

### 14.2 Doorways: the queue, and the transition that gave it something to queue for — done

Watching the sandbox for long enough produced three complaints, and they turned out to be one story: agents bunched up near a door and never went in; agents that could only reach a door by going round a one-way road stood beside it instead; and haulers waited a long way off while a door stood idle. The §15 open item — door transitions with a duration — is the other half of the same story, so it was built here rather than later.

**Fault one: a queue member could fall out of the queue and keep queueing.** Places were only handed out to agents within the 5-cell radius the queue forms in, but the fourth place *is* 5 cells out. An agent that walked back to its place read as out of range on the next frame, so nothing maintained its rank any more — and it went on holding a place that would never be promoted, exempt from the watchdog because as far as it knew it was politely waiting its turn. Every queue more than three deep stranded its tail permanently. A held agent is now ranked wherever it stands, which is the invariant: *the thing that hands out a hold is the only thing that can take it back, so it must never stop looking at an agent that has one.*

**Fault two: rank and hold points were geometry, and the way in is a route.** The nearest agent as the crow flies was sent in first, and everyone else was told to wait at a point back along the line to the goal. Both are wrong the moment the last step in is not a straight line. On a one-way road the nearest agent is often the one that has to walk the furthest — it is beside the door on the side the road forbids — so the queue put the one agent that could not get in at the front and held back everyone who could. And a hold point on that line lands wherever the geometry says, including on the far side of the road, so agents that *were* walking round were pulled back off the route they needed. Both now go through the destination's flow field, which is already built and shared by everyone heading there: rank is the field's remaining cost, a place in the line is a *distance* rather than a point, and an agent that is too close steps one cell **up** the field — back along a route it can come back down. §8 records the reasoning.

**Fault three: nothing capped how long a door was held, so nothing could queue for it.** This is the §15 item. `DoorUse` gives a transition a duration (0.4 s), and the three consequences the design predicted all arrive with it: a door serves **one agent at a time**, an agent **coming out goes before one going in**, and a transitioning agent is on the map for the whole of it — visible, avoidable, and counted by `ArrivalQueueSystem` as standing on that cell. That last one closes the hole §15 named: an agent holding a doorstep through a `Pickup` is no longer invisible to the queue, because "not walking, but still has something to do here" is now what an occupant *is*, whatever the reason.

Two things about the implementation are worth keeping:

- **The pending transition is the task step, not a queue entry.** A door can be busy for several frames, so a request has to survive them; a queue of requests would then need de-duplicating and a release path for every way an agent can stop wanting the door. Leaving the `Enter`/`Exit` at the head of the `TaskStep` buffer until the agent is through means "who is waiting for a door" is re-read from the world every frame and there is nothing to leak — the same reason the arrival queue stores nothing. `InteriorTransitionQueue` was deleted.
- **The animation is the transition, not a decoration of it.** The view lerps the agent onto the entrance cell and scales it to zero (reversed coming out), inside the existing `IJobParallelForTransform`, so the view layer's "the transform is driven and nothing else is" contract holds.

With a door that serves a known number of agents per second and a line that forms along the road, the concurrency cap could go up: `MaxConcurrentHaulers` defaults to 8 rather than 4. Keeping it at 4 with all of the above in place is what makes haulers sit in a hut waiting for a door that is standing empty.

**Fault four: a queue with no memory is a race.** Found by watching the fixed queue: rank was recomputed from cost every frame and nothing else, so the nearest agent won it every time it was run, and a trickle of arrivals nearer than whoever was waiting took the front indefinitely. Fixed by aging the wait into the rank — §8 has the reasoning and the two properties that make it safe. The one thing this needs from elsewhere is that a *new walk* resets the clock, so a hauler returning to a warehouse it visited a minute ago queues as a newcomer instead of cashing in the wait from its last trip.

**Fault five: "no route" was being timed out instead of read.** An agent whose destination the grid genuinely cannot reach — a road painted one-way across the only way in — sat through the full five-second stall window, had its task dropped, and was handed the *same* impossible walk on the next economy tick: a permanent five-second loop that looks exactly like an agent frozen on the spot, and which demolishing the building it was walking to does not fix, because the task holds a cell and the seal is in the road rather than the building.

Both halves were wrong and both are fixed by asking the question that already had an exact answer. `FlowFieldCache.IsKnownUnreachable` reports whether a **built and fresh** field says there is no route from a given cell — and answers *false* to all three ways of not knowing (no field, stale field, cell outside the window), because callers use it to decide not to try and refusing on a guess is worse than one wasted attempt. With that:

- `WatchdogSystem` separates the two things it notices. A stall is a guess and keeps its five seconds; no route is a fact and gets half of one. It also stops counting shove-by-a-crowd as progress when there is nowhere to progress to.
- `IdleAssignSystem` and `OrderAssignSystem` ask before they hand anything out — a bed, a haul, a shift. The first attempt at any destination is always allowed, which is exactly what builds the field that answers properly from then on, so the whole thing self-corrects after one wasted trip and needs no per-agent memory of failures, no blacklist and no expiry.

`OrderAssignSystem` asks it twice for a haul: once for the agent's walk to the source, and once for the **loaded leg** from the source door to the target door. A source an agent can reach but cannot carry anything out of is a whole round trip thrown away.

**Fault six: parking sent everybody to the same cell.** A road carries `NoIdle`, so on a map painted freely with roads there are only a few cells left that an idle agent may stand on — and `IdleAssignSystem` searched for one ring by ring in a fixed scan order, so every idle agent within range of the same patch of free ground resolved it *identically* and was sent to the same cell. A crowd converging on one cell is a crowd where nobody can reach it, and arrival wanted the centre to within 0.4 cells. Nothing treats that as a queue, because they are not queueing — each has its own destination. So they milled about, the watchdog gave up on each in turn, the idle rule handed them the same cell again, and they circled a few cells from where they started, forever. Watching it, it reads as "agents that have nothing to do with that spot all walk to it and mill around".

Three things were wrong and all three are now stated where they belong: a parking spot needs **room to itself** (`PARK_SPACING`, checked against the agent spatial hash plus the spots already handed out this tick), parking arrives on **the cell rather than its centre** (`TaskStep.Park`, the same argument as a doorway and for a stronger reason), and **nowhere free is a real answer** — an agent standing on a road the player painted under it is owed somewhere to be by nobody, and standing still beats an endless walk. The search is capped by the same per-tick budget as the shelter claims, since a fully roaded map makes every idle agent run it.

**Fault seven: one field could answer for two destinations.** The worst of the lot, the oldest (step 3), and the one that actually produced "a crowd walks to a point nothing about their task refers to, and stays there". `FlowFieldCache` mapped destination → slot, and two things could put that map out of step with the slots themselves:

- eviction removed the evicted destination's mapping **only if its field had been built** — but a slot claimed earlier in the same frame is not built yet, so recycling one left the first destination still pointing at it;
- when every slot had been read or claimed that frame, the search for one to recycle fell through to `chosen = 0` and took slot zero regardless — silently handing one field to two destinations.

Either way a destination went on resolving to a slot that by then belonged to somewhere else, and `TryGetSlot` had no reason to doubt it. Every agent walking to the loser followed the winner's field, which steered it to the winner's cell — a destination with no connection to its task — where it then *stayed*, because arrival is measured against the goal it was still correctly aiming at and it was never going to reach it. It cleared itself whenever the map changed enough to churn the cache, which is why painting a road over the spot "fixed" it and why removing the road did not bring it back, and it re-formed for a different pair of destinations later. The hut doorstep collected the crowds because it is the most-requested cell in the sandbox — every idle agent goes there — so it is the field most likely to be the one still sitting in a slot.

Three changes, of which the first would have been enough on its own:

- **`TryGetSlot` checks that the slot still names the destination asked for.** A mapping that no longer owns its slot reads as "no field", the agent asks again, and the next frame builds it one: wrong-and-invisible becomes late-by-a-frame.
- **Eviction drops the previous mapping whether or not the field was built**, guarded by an ownership test so that a never-used slot's default goal cell cannot strand a real destination living elsewhere.
- **Acquiring can fail.** When every slot is being read or built this frame there is genuinely nowhere to put the field, and saying so is the answer; the request is not served, exactly as one over the per-frame build cap is not, and the agent asks again. Serving it by taking somebody else's slot is not a frame of latency, it is a crowd walking to the wrong place indefinitely.

The lesson is the invariant, not the three edits: **a cache keyed by identity must verify identity on the way out.** A lookup that trusts its own index is one recycle away from being confidently wrong, and nothing downstream can tell.

**Fault seven: one field could answer for two destinations.** The real one, and the oldest — step 3, not any of the work above. `FlowFieldCache` mapped destination → slot, and two paths let a slot be recycled while a mapping to it survived: eviction only cleared the evicted destination's mapping **if its field had been built**, and when every slot was already claimed or read in the current frame the search for a victim fell through to slot 0 and took it anyway. Either way a destination went on resolving to a slot that by then held somebody else's field.

The result is the worst-behaved bug in the whole module, because nothing about it looks wrong from the inside: every agent bound for A follows the field for B, walks to B, and *stays* there — arrival is measured against A, which it never reaches. On screen it is a crowd standing on some cell they have no business being on, each with a perfectly sensible task pointing somewhere far away, and the cell they choose is a *previously* cached destination — in the sandbox, reliably the hauler hut's doorstep, because every idle agent asks for that field. Painting a road over it appears to fix it, and permanently: the grid version bump churns the cache until the slot is properly evicted. Fix one and another appears elsewhere, because the aliasing is per pair of destinations.

Three changes, and the first is the one that matters:

- **A slot is only yours if it says it is yours.** `TryGetSlot` now checks the slot's own `GoalCell`, so a stale mapping reads as "no field" — the agent asks again and gets one next frame. Wrong-and-invisible becomes late-by-a-frame, and the entire class of bug stops being observable however it arises.
- **Eviction clears the mapping whether the field was built or not**, guarded by an ownership test so that a never-used slot's default goal cell cannot strand a real destination living somewhere else.
- **"No slot free" is an answer.** When every field is being read or built this frame there is genuinely nowhere to put another; `TryAcquireSlot` says so and the request waits a frame, exactly as one over the per-frame build cap does. Taking a slot somebody else is using is not a frame of latency, it is a crowd walking to the wrong place indefinitely.

Worth knowing for later: a cache of 32 fields is over-subscribed once more than 32 destinations are wanted in one frame, and the honest failure of that is agents standing still for a frame or two while they wait their turn to be built. Raising `CAPACITY` costs 48 KB a field.

**Reading a stuck agent, from the outside.** All of the above is invisible while it happens, so two things now report it. The panel names the state — walking, queueing (and how far out), waiting for a door, in a doorway, inside — and prints the task as what it *means* rather than as `TaskStepKind`, which collapsed a hauler's round trip into "GoTo → Interact → GoTo → Interact". A destination with no route from where the agent stands is called out in words. And `SelectedAgentOverlay` draws, for the agent you click, its **plan** (the task steps, cyan) against its **route** (the walk the flow field actually hands out, read one cell at a time exactly as `PathFollowSystem` reads it, green). Drawing the field's own answer rather than a path recomputed for the picture is the point: agents store no path, so anything else would be a different algorithm's answer shown in the agent's name. A route that stops dead at the agent's feet is drawn red, and that is the whole diagnosis.

Regression cover, all EditMode: a queue eight deep drains (`ArrivalQueueTests`); a door on a one-way road is queued for in route order and hauls still complete round it (`TrafficTests`); the transition takes time, serialises, prefers exits and is queued for (`DoorwayTests`); and eight haulers push two hundred units through one door, which is the stall detector for all of it. For watching it rather than asserting it, `AgentDebugOverlay` draws the ring an agent has been told not to cross and a box on every door in use, shrinking as its occupant goes through.

### 14.3 Bridges: the grid's first non-geometric adjacency — done

A bridge is the fifth use of §6's "an agent is here rather than on the map", and the first one that is not a *destination*. A hut, a workshop and a warehouse are places an agent goes because its order says so; a bridge is a place it goes **through**, on the way to somewhere the bridge knows nothing about. That single difference is what decided the whole shape of the feature.

**The structure is two piers and a gap, and the connection is stored.** One alternative was tried on paper first: make the deck a walkable one-way corridor, and routing comes free — the flow field and the gate graph see an ordinary road and nothing in `GridNav` changes at all. It was rejected because a walkable deck cannot be *closed*. The whole point of the requested behaviour is that a bridge whose far bank is jammed stops admitting, and a deck an agent can simply walk along has no admission to refuse.

So the structure is footprint and the crossing is a `NavLink` — the one adjacency in the grid that does not fall out of the coordinates. But the structure is **not** the whole line. Only the cell just inside each mouth is solid; the ground between the piers is left exactly as it was found:

```
   . . [M] [P] . . . [P] [M] . .      M = mouth, walkable, flagged Entrance|NoIdle
                ^                     P = pier, blocked footprint
          traffic crosses here        between the piers: untouched
```

That is what makes a bridge something traffic goes *under* rather than a wall with a gate in it, and it falls out of one rule stated once: **nothing about a bridge ever clears a cell.** The piers add cost and give the same cost back, so a bridge cannot drain a river it spans; and because the gap is never cleared either, a bridge across a wall leaves the wall standing under its own deck. Both are tested, because both would be a hole in the map that nothing would report.

Travelling *along* the line is therefore the link and only the link — the piers close both ends of it — while crossing the line on the ground needs no permission from anything. `Bridge.TryShape` is the single place that turns two mouths into piers, gap and span, because validation and placement have to name the same cells: two copies of that arithmetic is a bridge that passes its own check and then seals itself.

**A crossing agent leaves the spatial hash.** It keeps its view and its position but it is nobody's neighbour, because it is over the map rather than on it — and leaving it in would let it shoulder the traffic passing under the deck, which is the one thing the shape exists to allow. It is asked per agent through a `ComponentLookup` rather than filtered on in the query: a query keyed on `OnBridge` would silently drop every agent whose archetype does not carry it, and a missing entry in the spatial hash is invisible until two agents walk through each other.

**Fixed lengths from the menu, not a drag.** A bridge is 3 or 4 cells of structure, chosen like any other blueprint and placed with one click. A drag was built first and thrown away: it made the player state a length that only ever has two useful values, and it put a second placement gesture in a tool controller that already had one. What the drag *was* doing usefully — saying which way the crossing runs — is now `R` to rotate, which uses `BuildingPlacement.Rotation` and `RotationUtils` exactly as authoring already did. `CanPlace` and `DoorstepOf` became rotation-aware in the process, which they should always have been: the doorstep was hardcoded to "below the origin" and was quietly wrong for any rotated building.

Rotation also exposed a placement bug older than bridges. Rotation happens *about* the origin cell, so a rotated footprint runs off in a different direction from an unrotated one — a quarter turn sends a 3x2 building down and to the left of the cells it was authored to occupy. That made the cursor mean a different corner of the building at every rotation: the preview and the placement disagreed, and turning a building walked it away from the mouse. `RtsConstruction.OriginFor` shifts the origin so the *rotated* shape lands under the cursor, and every caller goes through it, so validation, placement and preview cannot drift apart. A bridge is anchored on the mouth agents step on from rather than a corner of its box, because "here is where you get on, and it runs away from you" is the one description of a directed line that survives a rotation.

The placement preview draws every cell it will take rather than one box, because a box is a lie about two of the three shapes: an L is not its extent, and a bridge emphatically is not — an outline over the gap would say those cells were being taken. It also draws the **doorstep**, which is the half of a placement the player otherwise cannot see: a building whose door lands against a wall is sealed, and the rule that decides it is the south wall turned by the rotation, invisible until an agent fails to reach the building.

Two things about drawing a crossing that are not decoration:

**Depth is per agent, not per pool.** An agent on a bridge must draw in front of the deck and an agent walking underneath must stay behind it, and those are two different answers in the same frame — so the lift lives on `AgentViewFrame` beside the door blend rather than on `ViewSyncSystem.Depth`. It has to clear the front face of the building quad, which stands half its thickness in front of its own centre, so the number is a fact about the prefab rather than a taste and is a serialized field.

**Boarding starts from where the agent stands.** An agent is admitted from wherever on the mouth cell it stopped, which is almost never the centre line the deck runs along. Snapping it there was a visible jump onto the bridge — small, and the one moment in the crossing that did not look like walking. So the along-deck part of where it stood becomes distance already travelled and the rest becomes an offset walked off at the agent's own speed. Nothing about getting onto a bridge moves an agent other than its own legs, and there is a test that boarding never moves it further in one frame than a step.

`NavLink { int2 From, int2 To, ushort Cost }`, directed, stored *in* `GridMap` rather than beside it. That last part is the reason the three searches needed no new argument threaded through them: they already carry the map. A two-way crossing is two links, which is the honest way to say it — the pair then has two mouths, two queues and two costs, and nothing special-cases the symmetric case.

**`CellFlags` gains two bits that do affect routing**, which the old comment said none of them did. `LinkEntry` / `LinkExit` are the fast reject in front of a 256-entry table: a Dijkstra asks "does anything unusual happen here" with a byte it was going to read anyway, and only a bridge mouth pays for the scan. They are set only by the link operations, never by `AddFlags`, because a bit that changes where agents can walk has to bump `PassabilityVersion` and the flag operations deliberately bump nothing.

**Three searches, three places a link had to be taught.** They divide exactly as §4 divides the tiers, and each needed a different thing:

| tier | what a link is there | why that one |
| --- | --- | --- |
| flow field (`BuildFlowFieldJob`) | a fifth incoming edge, relaxed backwards from the cell it lands on | the field is what steers the last stretch, and both mouths are inside one 128 window for any sane bridge |
| chunk search (`ChunkSearch`) | an edge followed when both mouths are in the same chunk | a bridge *inside* a chunk needs no gate — the intra-chunk edge costs already say the two banks are joined, which is the only thing the coarse graph asks a chunk |
| gate graph (`GateBorder.Link`) | a gate, when the link leaves its chunk | a gate *is* a way out of a chunk at a known cost in a known direction, and a bridge is one; only the far chunk cannot be derived from a border offset, so it is recorded |

The split on that last row is worth keeping: an intra-chunk link deliberately gets **no** gate, because a gate whose two sides are the same chunk is a node the "gate plus the side you came out on" A* cannot say anything useful about. One rule, stated once: *a link that leaves its chunk is a gate; one that does not is a shortcut the chunk search takes.*

Costs are 16 bits of the same currency everywhere, and it matters that they agree. A crossing is priced at `NavCost.STEP` per cell of span — what a road of the same length costs, because that is what a bridge is: built ground, no slower than the best ground there is. Crucially **not free**: a crossing priced at nothing is a hole in the cost model that every route in range falls into, and half the map detours over a footbridge to save a corner. It also has to match how long the crossing actually *takes*, or the routing layer and the bridge tell the agent two different stories about the same walk — which is why the agent is moved at its own `MaxSpeed` rather than for a flat time.

**`FlowField.LINK_STEP` is a direction value, not a rule the layer above works out.** An agent standing on a bridge mouth whose route uses the bridge, and one standing on the same cell on its way past, are the same agent with the same task; only the field can tell them apart, because pricing the cell is what made it decide. `TryGetDirection` reports the link as "no direction" — which is the right answer for a walker, since standing still on the mouth is exactly what it should do — and `IsLinkStep` is the separate question that says why.

The direction pass writes `LINK_STEP` only when the crossing is the **exact predecessor** the integration used: `integration[To] + step + linkCost == integration[From]`. Both ways of being sloppy about that are invisible from the outside — a false yes puts an agent on a bridge its route never asked for, a false no leaves it stood on a mouth with no direction and no reason it can be told — and the exact test costs one addition. A tie with a walkable neighbour breaks towards walking, because a bridge has a queue at its mouth and open ground does not.

**A crossing is a walk with the logic taken out, not an absence.** `PathFollow` goes off, which is the same three-way consequence `InteriorTransitionSystem` gets from the same flag: nothing steers the agent, nothing integrates it, and the watchdog does not watch it. `AgentMove` stays **on**, and that is the difference from going indoors: the agent is still drawn, still in the spatial hash, and still something the crowd at either mouth has to avoid. An agent that vanished for the length of the crossing would reappear in the middle of whatever had gathered on the far bank, which is the problem `DoorUse` exists to avoid at a door.

Two things fell out of that which were expected to need code and did not. **No task step.** `BridgeTransitSystem` runs between `TaskStepSystem` and the routing systems, so it can take an agent out of the walking set in the same frame that the task machine put it back in — and the agent's `GoTo` stays at the head of its buffer naming the destination it always had. When it is put down on the far bank, `TaskStepSystem` simply starts the walk again and routes from where the agent now is. Nothing had to be taught what a bridge is, and there is no state to unwind if the task dies mid-crossing. **No view code.** The view reads `AgentMove.Position`, and the bridge writes it, so an agent visibly walks the deck for free.

**Single file is the whole of the blocking rule.** Occupants are kept front first and none may advance past the one ahead. So an agent that cannot step off the far bank stops, the one behind stops behind it, the deck fills back to the mouth, and admission ceases because there is no room at the near end. Nothing counts blocked agents or decides when a bridge is "full" — being full is what a queue that cannot drain *is*. `Capacity` is the span for the same reason: occupants are spaced a cell apart because a cell is what an agent takes up everywhere else on the map, so the number is not a tuning knob.

**The head of the deck is the one agent in the game with no watchdog behind it**, and that took noticing. It is not walking, so `WatchdogSystem` cannot see it; it is waiting on a cell rather than on a queue that promotes, so nothing else does either. A far bank that somebody never leaves would be a bridge that stops for good with nothing anywhere reporting it — the exact shape of failure §14.2 spent five faults learning to refuse. So the wait is counted and bounded: past three seconds the agent steps off anyway. A moment of overlap that avoidance sorts out in a few frames beats a deadlock that nothing sorts out at all.

**Two records of one crossing, and the second one earns its keep.** The bridge's `BridgeOccupant` buffer is the authority on *where along the deck*; `OnBridge` on the agent is the authority on *whether at all*. It looks like the same fact written twice, and it is the same shape as `InsideBuilding` beside `Interior.Occupied` for the same reason: a crossing agent has had its steering turned off by something outside itself, so if that something ceases to exist there must be something *on the agent* that says so. Otherwise a bridge destroyed with agents on it leaves them stood on a blocked deck with no steering and nothing that knows to give it back — the stranded-claim bug of step 8, one layer down. Releasing them is one rule in one place, so it cannot be forgotten in one of the several ways a bridge can stop existing.

**Authoring is two cells and the rest is derived**, which is the third instance of the argument entrances won in step 5. `BridgeSpan` names the two mouths; the piers, the link, the `Entrance | NoIdle` mouth flags and the span all come out of `Bridge.TryShape` and are applied by `BuildingFootprintSystem` — the same system that gives every one of them back, so a demolition cannot leak half a bridge. An author asked to keep a structure list in step with a pair of mouths will eventually not, and the failure is a bridge with a hole in it that nothing downstream could tell from a map.

A pier standing in water is not only allowed but the normal case, and the cost sum being exact (§3) is what makes it safe. Under the deck, only *another building* is refused — two views on one cell reads as a mistake — and everything else is left to be walked over, walked under, or blocked on its own account.

Costs of the whole thing, stated rather than discovered later: the gate graph grew from 32 to 40 touching-gate slots per chunk, so its edge-cost table went from about 0.5 MB to 0.8 MB on a 512² map. The *work* is unchanged when no bridges exist, because `BuildEdges` loops over the real gate count and not the slot count. `MAX_LINK_GATES_PER_CHUNK` is 4, deliberately small: each link gate costs a Dijkstra over its chunk on every rebuild that touches it, and a chunk with five bridges out of it is a map that wants a road.

**What is deliberately not built.** A bridge mouth is a contended cell that `ArrivalQueueSystem` does not cover, because the queue ranks agents by the remaining cost to *their own destination* and a mouth is nobody's destination. So agents pile up at a busy mouth and are sorted out by RVO, and one that waits out its stall window has its task dropped and re-planned — which is the designed recovery for every other congested cell on the map, and the deck length bounds how long the wait can be. Revisit only if a bridge mouth in play turns out to be worse than a corridor.

Regression cover, all EditMode (`BridgeTests`): the piers block and the gap and both mouths stay walkable; an agent walks *under* a bridge without using it; a bridge cannot open a wall it crosses; a pier in water gives the water back exactly; a span too short or not in line is refused and takes nothing; rotating a bridge turns the whole thing; the field prices the crossing to the unit and reports `LINK_STEP`; the far bank is unreachable without a bridge and unreachable *backwards* with one; an agent crosses a walled map and carries on to its goal; a carried agent is on the map but not walking, and moves at walking pace; a taken far bank backs the deck up in order and closes the mouth; clearing it drains the queue; a bank that never clears does not stop the bridge for good; demolishing a bridge under an agent puts it back somewhere it can walk; and a bridge across a chunk border appears in the coarse route as a link gate while one inside a chunk correctly does not.

## 15. Open items

- Whether `DeliverInUpTo` / `DeliverOutDownTo` are authored in absolute units or percent of capacity (CoI offers both).
- Whether warehouse-to-warehouse rebalancing is ever wanted; today it is blocked by design and the player can force it by setting different priorities.
- Flow-field window size (128² assumed) wants measuring against real building density.
- `MaxConcurrentHaulers` / `MaxConcurrentVisitors` values are tuning, not design — 8 and 8 today, and both want measuring on a real map. So does the 0.4 s door, which is now a throughput number as much as an animation one.
- **A doorstep interaction does not hold the doorway.** A hauler standing on a step through a `Pickup` is counted by the queue, but it does not hold the `DoorUse` resource, so an agent inside can still start walking out into it. Harmless today — both last well under a second and avoidance sorts out the overlap — and the fix, if it ever matters, is to give the interaction the same hold rather than to invent a second mechanism.
- **A bridge mouth is not queued for** (§14.3). It is a contended cell that `ArrivalQueueSystem` cannot rank, because it ranks by cost to the agent's own destination and a mouth is nobody's destination. RVO and the stall watchdog cover it as they cover any congested cell; whether that is good enough wants watching on a map with a busy bridge on it.
- **`MAX_WAIT_AT_FAR_END` (3 s) and `MAX_LINK_GATES_PER_CHUNK` (4) are tuning, not design.** Both want measuring against a real map, like `MaxConcurrentHaulers` and the 0.4 s door.
- **The avoidance numbers are tuning, not design** (`AgentVelocityJob`): 12 neighbours, a 1.5 s agent horizon, a body half the collision radius, and a 0.25 s speed ramp. They move together — the horizon sets the sight distance, and sight is only worth widening while the neighbour budget can hold what it finds — so a change to one wants the others looked at. The horizon is the one with a ceiling in both directions: too short and a constraint arrives too late to act on, too long and an agent brakes for a crowd it would never have met.

Settled (previously open): cell size is 1 unit, shared by the nav and building grids, agent radius 0.42 (§3). Door transitions have a duration, and with it the doorway is a resource held for a known time (§14.2).
