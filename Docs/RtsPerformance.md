# RTS performance

A record of what was measured, what was changed, and — mostly — what was deliberately *not* changed and why.
The second list matters more than the first: every item on it was justified by a cost that turned out not to
exist, and rederiving that analysis is the expensive part.

Measured on Unity 6000.3.18f1, Entities 1.4.6, Burst 1.8.29, in the Editor.

## 1. The finding: a missing attribute, not an algorithm

`AgentSpatialHashSystem` cost **~1.35 ms per frame at 20 agents**. `AgentIntegrateSystem` and
`InteriorTransitionSystem` cost about the same. None of the three had `[BurstCompile]`.

The number was never plausible as work. At 20 agents the hash build does ~45 `AddToCell` calls — with
`Radius = 0.42` against `CELL_SIZE = 1f` each agent's AABB covers 1–4 cells, ~2.25 on average — which is
single-digit microseconds. 1.35 ms is 67 µs *per agent*, three orders of magnitude off.

Profiling at 20, 200 and 2000 agents settled it: **the cost was flat.** A per-system cost that does not move
with entity count is not the algorithm; it is the floor under every invocation. Here that floor was the
un-Bursted managed call path, and `[BurstCompile]` removed it.

### The rule this leaves behind

> **Check the slope before reading the code.** Profile the same system at 10x and 100x the entity count. A
> cost that scales is a cost in the loop, and the loop is worth reading. A cost that stays flat is a cost per
> invocation, and no amount of work inside the loop explains it — look at the attribute, the container
> construction, the sync point, or the measuring instrument itself.

Applying that first would have pointed straight at the missing attribute. Reading the code first produced a
correct-but-irrelevant analysis of the query's scaling behaviour (§4), which was not the problem.

### Ruling out the instrument

A flat 1.3 ms on a loop doing ~45 hashmap inserts is also consistent with the profiler's own per-marker
overhead — in which case the cost was never real and no code change would ever have moved it. That is worth
half a minute to exclude, and the way to do it is two systems that do nothing, one Bursted and one not, in the
same group as the suspect:

| unbursted no-op | bursted no-op | verdict |
| --- | --- | --- |
| ~the suspect cost | ~0 | the floor is the managed call path — `[BurstCompile]` is the whole fix |
| ~the suspect cost | ~the same | the floor is profiler overhead — the cost was never real |
| ~0 | ~0 | the cost is real work inside the suspect systems — read them again |

The reading here was the first row. The probe was deleted once it had answered; it is a measuring instrument,
not a feature. Rebuild it rather than keep it — it takes two minutes and a stale probe in the system list is
worse than no probe.

## 2. Applied

`[BurstCompile]` on the type and on `OnUpdate`, following `GridApplySystem`. `OnCreate` is left managed
wherever it calls `CreateSingleton`, which takes a `string`.

| system | group |
| --- | --- |
| `AgentSpatialHashSystem` | `RtsAgentGroup` |
| `AgentIntegrateSystem` | `RtsAgentGroup` |
| `InteriorTransitionSystem` | `RtsAgentGroup` |
| `InteractionSystem` | `RtsAgentGroup` |
| `OrderCompletionSystem` | `RtsAgentGroup` |

Nothing blocked Burst on any of them. The only managed-looking construct was `using
System.Collections.Generic` in `InteriorTransitionSystem` and `OrderAssignSystem`, and both use it solely for
a **struct** `IComparer<T>` handed to `NativeList.Sort` — which Burst compiles. `EntityManager.GetComponentData`
/ `SetComponentData` / `SetComponentEnabled` / `GetBuffer` are all Burst-compatible in Entities 1.4; toggling an
enableable component is not a structural change.

Verified: clean compile, 361/362 EditMode tests passing (the one failure pre-exists — see §6).

## 3. Still un-Bursted

`OrderAssignSystem` and `ThreatDetectionSystem` have since been Bursted (`RtsBattleScaling.md` P1, P3). The
rest are still managed, and §7's real-frame profile says none of them is worth it yet: the most expensive,
`IdleAssignSystem`, is under 0.1 ms a frame averaged at 800 haulers.

`BridgeTransitSystem`, `IdleAssignSystem`, `StorageRequestSystem`, `WorkRequestSystem`,
`ChunkGateGraphSystem`, `FlowFieldCacheSystem`, `GridMapSystem`.

## 4. Deliberately not done

Everything here was justified by cost that Burst removed. **Do not act on any of it without a fresh profile at
a real target agent count.** The analysis is recorded so it does not have to be redone, not because it is owed.

**The design doc oversells the parallelism that exists.** §13.4 lists systems 12 (spatial hash), 15 (path
follow), 16 (avoidance), 17 (integrate) and 22 (view sync) as `ScheduleParallel`. Only 16 and 22 actually
schedule jobs; 12, 15 and 17 are main-thread `foreach` loops. That gap is the origin of this whole
investigation and is worth knowing before trusting the table.

**Parallelising integrate and path follow.** *Done in §7, along with the watchdog.* Both are genuinely
trivial to convert — `AgentIntegrateSystem`
reads `GridMap` read-only (invariant 1 makes it immutable through `SimulationSystemGroup`) and writes only its
own entity's `AgentMove` and two enableable flags, which is exactly `IJobEntity.ScheduleParallel`. Cheap to do,
but there is currently no cost to recover.

**The avoidance sync point.** *Rewritten in §7 as an entity job; the `Complete()` stays, for the reason
§5 now gives.* `AgentAvoidanceSystem` builds `query.ToEntityArray(Allocator.TempJob)` every
frame, then reads and writes `AgentMove` through a `ComponentLookup` marked
`[NativeDisableParallelForRestriction]` — random access by entity rather than linear chunk iteration, with the
safety system switched off — and then immediately calls `state.Dependency.Complete()`. As `IJobEntity` it would
need no entity array, no `TempJob` allocation and no disabled safety, would get sequential access, and could
hand `state.Dependency` straight to a parallel integrate, removing the sync point. Four problems, one rewrite.

**Parallelising the spatial hash build.** The blocker is the container, not the loop: `NativeSpatialHash` is a
`NativeHashMap` head table plus a free-stack linked list, which is inherently single-writer and cannot be
given a `ParallelWriter`. Two routes if it ever matters — a flat two-pass build (count per cell → prefix sum →
scatter), which also fixes the cache problem below; or `NativeSpatialLookup`, which already wraps
`NativeParallelMultiHashMap` and has a `BuildLookupJob` with a `ParallelWriter` sitting commented out.

**Read-path cache layout.** `Node<AgentMove>` is 48 bytes (`AgentMove` is 40). Queries walk a linked list via
`Next` indices into `Nodes`, where one cell's nodes are scattered across the whole list by insertion order. At
2000 agents that is ~4500 nodes ≈ 216 KB of pointer-chasing per frame, past L2. The flat two-pass layout above
removes the chasing.

### The one thing that does scale

If a future profile *is* scaling-bound, look here first. With `MaxSpeed = 3`, `Radius = 0.42`,
`TIME_HORIZON_AGENT = 1.5` and `BODY_RADIUS_SCALE = 0.5`:

```
SightDistance = 3 × 1.5 + 0.42 × 2   = 5.34
range         = 0.21 + 5.34          = 5.55
```

`ForEachInAABB` is floor-based and inclusive at both ends, so a query of ±5.55 against `CELL_SIZE = 1f` probes
**12 × 12 = 144 cells per walking agent per frame** to find at most `MAX_NEIGHBOURS = 12`. At 2000 agents that
is 288,000 hashmap probes a frame, and in a sparse crowd ~140 of every 144 are misses.

The sight distance cannot shrink — `AgentVelocityJob` argues correctly that an agent blind inside its own
planning horizon is worse than a slow one. The cell size is the free variable, and it is not load-bearing:

| `CELL_SIZE` | cells probed per query | cells written per agent |
| --- | --- | --- |
| 1 (current) | 144 | ~2.25 |
| 2 | ~49 | ~1.5 |
| 3 | ~20 | ~1.2 |

Cell size 2–3 trades ~120 probes for a longer candidate list per cell, and `NeighbourInsertion.Process`
already early-outs on squared distance. One constant, and the largest single win available *at scale*.

*Done in §7: cell size 2, and each agent filed once at its centre rather than under every cell its body
overlaps.*

## 5. Sync points

Seven hard `.Complete()` calls: `GridApplySystem`, `ChunkGateGraphSystem`, `FlowFieldCacheSystem`,
`AgentAvoidanceSystem`, and since §7 `AttackSystem`, `AgentIntegrateSystem` and `WatchdogSystem`.

**They all have the same cause, and it is not the work.** Every one of these jobs reads or writes a native
container held *inside a singleton component* - the grid map, the field cache, the agent hash, the damage
queue. ECS tracks dependencies per component type, not per container, so nothing makes the next writer of the
container wait for a job still reading it. §7 found out the hard way: the watchdog job, left running, was
still reading the field cache when the next frame's `FlowFieldCacheSystem` wrote it, and the safety system
threw every frame. Each job still spreads across every worker; it just cannot overlap the systems after it.

Removing them is one change applied five times, and is the largest piece of parallelism left (§8): give each
singleton container its own dependency - a `JobHandle` stored beside it that every reader combines into its
own dependency and every writer completes first - so that the overlap the design table promises becomes
real.

`ViewSyncSystem` is a deliberate exception and should stay one: its transform job handle is held manually
rather than in the ECS dependency graph, and is completed at the top of its own next update. The job therefore
overlaps the following frame's simulation. That is safe because the pool copies its frame data before
scheduling, so the job touches no ECS container — but it does mean the handle is invisible to
`CompleteDependencyBefore*`, and anything that starts reading `AgentMove` from that job would be a race with
no safety check to catch it.

## 6. Open, found on the way

Not performance, but found while reading and not yet fixed.

**`NativeSpatialHash.Count` is written to a discarded copy.** `AgentSpatialHashSystem.OnUpdate` takes
`...ValueRW.Hash` into a local, which copies the struct. `Nodes`, `CellHeads` and `_freeStack` are handles so
their mutations persist, but `Count` is a plain `int` field on the copy, so `AllocateNode`'s `Count++` is
thrown away and the singleton's `Count` stays 0 for ever. Nothing in `Rts` reads it today, which makes it a
trap for the next consumer rather than a live bug.

**`SpatialHashMethods.Hash` collides on negative coordinates.** `(cell.y << 16) + cell.x` maps cell `(-1, y)`
onto `(65535, y-1)`. Harmless while the grid is origin-at-zero and non-negative; silently returns the wrong
neighbours the day anything goes negative.

**~~`ArrivalQueueTests.ALongQueueDrainsInsteadOfStrandingItsTail` fails.~~** Resolved — it was the test, not
the queue. The commit that added bridges also raised the test agent radius from `0.35f` to `0.42f` to match
production, and the test asserted that all eight agents got within 1.2 cells of the destination. Since nothing
in that scenario goes inside a building, arrived agents park on the cell for ever, so the eight settle into a
blob whose outer edge sits wherever eight bodies of the current radius pack — 1.03 at radius 0.35, 1.234 at
0.42. The threshold was measuring body packing with no margin, and the radius change tipped it over. The queue
mechanism was never involved: instrumenting it showed hold places handed out to 7 cells, every agent released,
and everything settled by frame 725 of 1200. The assertion now watches what the queue actually writes — nobody
left holding, nobody left walking, everybody let in past `FIRST_HOLD_DISTANCE`, and at least one place handed
out beyond `ENGAGE_DISTANCE` so the test cannot pass on a world with no queue in it. Verified by mutation:
reintroducing the original fault makes it fail naming the stranded agent.

**A bare `GoTo` cannot arrive at an occupied cell.** `DEFAULT_ARRIVE_DISTANCE` is 0.4, but two agents of the
production radius cannot come closer than `2 × 0.42 × 0.5 = 0.42` centre-to-centre, so once one agent is parked
on a cell no second agent can ever satisfy the arrival test for it — it stalls until the watchdog gives up on
it, which is what the five stalled agents above were doing. Not live: no production system issues a bare
`TaskStep.GoTo`. `GoToDoor` (1.1) and `Park` (0.7) are both clear of the separation, and the only bare `GoTo` in
the tree is the `AgentSpawnSystem` debug spawner. It is a trap for whoever adds the next task kind, not a bug
today.
</content>

## 7. Second pass: 2000 armed agents, 800 haulers

Measured in **real frames** with `ProfilerRecorder` on each system's own marker - `"Default World <System>"`,
which a Bursted system records under the **"Burst Jobs"** category, not "Scripts" - and on the engine's frame
markers (`PlayerLoop`, `Inl_RenderCameraStack`, draw-call and SetPass counters). Not by running systems by hand
from a paused play mode, which is how the first pass of `RtsBattleScaling.md` was measured: paused, time
does not advance, no weapon is ever loaded, and the worst system in the game read as 0.8 ms.

**The battle**, `RtsBigBattle.unity` at 600 swords + 400 guns a side, 1550-1700 alive in the sample windows:

| | before | after |
| --- | --- | --- |
| `PlayerLoop` | 13.0 ms | 6.2 ms |
| `SimulationSystemGroup` | 6.1 ms | 1.8 ms |
| `AttackSystem`, worst tick | 15 ms | 0.5 ms |
| `AgentAvoidanceSystem` | 3.3 ms | 1.0 ms |
| combat view | 2.2 ms | 1.4 ms |
| SetPass calls | 231 | 23 |

**The economy**, `RtsSandbox.unity` with 800 haulers and the debug overlays on, as the scene ships:

| | before | after |
| --- | --- | --- |
| `PlayerLoop` | 139 ms | 7.7 ms |
| camera render | 135 ms | 6.0 ms |
| draw calls | 127,639 | 950 |

What changed, in order of what it bought:

1. **The debug overlays drew one gizmo per shape.** Every `Gizmos.DrawLine` / `DrawSphere` / `DrawCube` is a
   draw call and a material switch, and the overlays draw several per agent and per grid cell - 127k draws a
   frame at 800 haulers, against a simulation that took 1.2 ms. `GizmoBatch` collects a whole draw into one
   `DrawLineList` and one mesh per colour, with continuous colours (speed, trail age, cost heat) rounded into a
   few steps so they share a batch. Trails, the last big cost, are kept for the 100 agents nearest the
   overlay (`_maxTrails`) and their dots are drawn as line crosses rather than filled discs.
2. **Soldiers searched for targets one at a time on the main thread.** Every loaded soldier queried the hash
   for its weapon's whole range box and sorted through a candidate list, every economy tick: a 15 ms hitch ten
   times a second. Now a parallel entity job - design §13.5's "search parallel, commit serial" - with each
   soldier writing only its own `Weapon` and its shot into `DamageQueue.AsParallelWriter()`, and a nearest-enemy
   processor instead of a candidate list.
3. **The agent hash filed a body under every cell it overlapped, at cell size 1.** Every reader measures to an
   agent's *centre*, so a box round the query circle already contains every centre it needs: the copies bought
   nothing. They cost up to four nodes per agent, four times the candidates for every query, and avoidance
   spending its twelve neighbour slots on the same neighbour twice. Now one node per agent at cell size 2 (§4).
4. **A per-renderer property block took every soldier out of the SRP batcher.** The combat view now uses one
   material per side, made once from the prefab's.
5. **Per-frame agent systems ran as main-thread loops** (§4). Path follow is an entity job; avoidance an entity
   job over chunks instead of an entity array with a lookup; integrate and the watchdog are chunk jobs.
   **An entity job cannot read a component and write its enabled bit** - `in PathFollow` beside
   `EnabledRefRW<PathFollow>` is two handles to one type and fails the safety check - so the two that switch
   `PathFollow` off on arrival or on giving up are `IJobChunk`s, which read the data and the bit through one
   handle.
6. **The combat view wrote every soldier's transform on the main thread.** Soldiers are now scene roots in a
   `TransformAccessArray` written by a parallel transform job, as the agent pool's are; only a hand mid-swing,
   a gun mid-kick or a flash is written on the main thread, and only while it moves.

## 8. Parallelism worth doing next

In order of expected value at the 1000-agent budget (§12 of the design). None is needed for 300 a side; all of
them are for the day the count or the map grows.

1. **Track the singleton containers** (§5). One `JobHandle` per container, combined by readers and completed by
   writers, turns seven hard syncs into dependencies and lets the agent phase overlap itself the way design
   §13.4 draws it. The biggest structural win left, and the one every item below benefits from.
2. **`TaskStepSystem` as a parallel job.** 0.3 ms at 1600 agents, single-threaded. Each agent's steps are its
   own; what it shares is the interaction queue, which needs the same `AsParallelWriter()` the damage queue
   got, and the lookups it reads are read-only.
3. **The combat view's read side** (0.5 ms at 1600). Two dictionary operations per soldier a frame -
   `NoteShot` and `Show` - plus a dictionary-wide scan for the unseen. A `NativeHashMap` from entity to slot,
   filled by a Burst job reading `Weapon` and `AgentMove`, would make the main thread's share the acquire and
   release of views and nothing per soldier.
4. **Fewer renderers per soldier.** 3600 draw calls at 1600 soldiers is three or four renderers each - body,
   blade or barrel, guard, flash. Batched by the SRP batcher they are cheap per call, but one mesh per soldier
   kind, or GPU instancing of the body, would cut the count by three or four.
5. **The index phase** (design §13.5). The agent hash is the only index today; a resource-node index or a
   faction target index built beside it would run in parallel with it for free, once item 1 lets them.

