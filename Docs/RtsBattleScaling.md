# RTS battle scaling — 300 vs 300

Proposal, then implemented: P0–P3 are done and measured in §5. Of P4 only the debug overlay was acted on.

Goal: `RtsBigBattle.unity` (180 swords + 120 guns a side, 600 armed agents) runs at a playable frame rate in
the Editor, and no amount of load can lock the Editor up again.

Measured on Unity 6000.3.18f1, Entities 1.4.6, in the Editor with safety checks on, by running every system
in `GridUpdateGroup`, `PathfindingGroup`, `RtsEconomyGroup`, `RtsAgentGroup` and `RtsViewGroup` once by hand
from a paused play mode, with jobs completed after each, timed with a stopwatch.

## 1. What happened

Entering play mode in the big battle froze the Editor for good: one core at 100%, memory flat, the main thread
never returning. It is not a slow frame. It is an infinite loop, and it takes two things to make one.

**The trigger: `ThreatDetectionSystem` costs 450 ms per tick at 600 soldiers.**

| armed agents | `ThreatDetectionSystem` | everything else together |
| --- | --- | --- |
| 200 | 60 ms | ~3 ms |
| 600 | 450 ms (steady, 13 ticks in a row) | ~5 ms |

Nothing else comes close. `OrderAssignSystem` is 14 ms on the first tick and ~2 ms after, `FlowFieldCacheSystem`
~2 ms, `AttackSystem` 2.6 ms. The combat view was 0.3 ms at 200 and 15.7 ms on the pass that first created 600
soldier views (instantiation); its steady state at 600 was not measured.

The cost is the query shape. Each soldier asks the agent hash for its whole leash box: `Home ± Leash`, and the
battle needs a 44-cell leash to see across the field, so that is 89 × 89 = 7,921 cells probed per soldier at
`CELL_SIZE = 1`. Every agent in the box comes back once per cell its body covers (~4), and each candidate gets
two or three `ComponentLookup` reads. At 600 soldiers that is ~4.8 M hash probes and ~1.4 M candidate checks
per tick, in managed code: the system has no `[BurstCompile]`.

**The amplifier: `RtsEconomyGroup`'s rate manager never lets go once a tick is slower than its period.**

`ComponentSystemGroup` runs `while (RateManager.ShouldGroupUpdate(this)) UpdateAllSystems();`.
`RateUtils.VariableRateManager` measures *wall-clock* time since it last ran, resets to zero, and returns true
when that reaches the period (100 ms). It is measured across the group's own update, so a tick that takes
longer than 100 ms has already used up the next period by the time it ends, and the loop goes round again.
At 450 ms a tick it goes round for ever, inside one frame.

At 200 agents the tick is 60 ms, under the period, which is why the 100 vs 100 run was slow (~9 fps) but alive.
Any economy system that ever spikes past 100 ms — a big order book, a rebuild, a GC pause — will do the same.

## 2. What stops it scaling even once it runs

These are measured at 100 vs 100, which is the size that does not lock up.

**Chasers mostly have no path.** `Engage` re-aims at its target's cell every tick, so every chasing soldier wants
its own flow field. With 200 soldiers engaged there were 124–158 distinct fields being requested and only 35–46
of the walking soldiers had a built field to follow. The cache holds `CAPACITY = 32` fields and builds at most
`MAX_BUILDS_PER_FRAME = 4` a frame, and the goals move, so roughly three soldiers in four are standing still
waiting for a field at any moment. At 600 it gets worse, not better.

**Orders reach soldiers slowly.** `OrderAssignSystem` hands out `MAX_CLAIMS_PER_TICK = 16` orders a tick. 584
fight orders at 16 a tick and 10 ticks a second is ~3.7 s before the last soldier is told to move. Measured:
the book drained by exactly 16 per pass.

**Managed main-thread systems.** `ThreatDetectionSystem` and `OrderAssignSystem` are not Burst-compiled
(`RtsPerformance.md` §3 already lists `OrderAssignSystem`; threat detection came after that list was written).

## 3. Proposals

In the order I would do them. P0 and P1 are what makes 300 vs 300 run; P2 and P3 are what makes it look like a
battle rather than a crowd waiting for paths.

### P0 — An economy tick can never run twice in one frame

Replace `VariableRateManager` on `RtsEconomyGroup` with a small `IRateManager` of our own that keeps the 10 Hz
period but answers true **at most once per `UnityEngine.Time.frameCount`** (or once per world update), and does
not count time spent inside the group's own update. A slow tick then makes a slow frame, never a hang.

- Cost: one class (~40 lines) in `Rts/Groups`, one line in `RtsEconomyGroup`.
- Risk: none for correctness — the economy is already written for a variable tick (`Weapon.NextShotTime` is a
  time, not a countdown, for exactly this reason).
- Test: an EditMode test with a system that sleeps 150 ms inside the group, asserting it runs once per world
  update.
- Do this first, whatever else is chosen. It is the difference between "slow" and "kill the Editor".

### P1 — Threat detection by threat, not by soldier

Same rule, same output — "an enemy, alive, within the leash of some post of another side" gets one fight order —
but turned inside out so its cost is per agent rather than per soldier × leash area.

1. Bucket every armed post into a coarse grid (cell size 8, say) keyed by faction, with the largest leash in the
   bucket. A few hundred entries, built once per tick.
2. For each agent, look at the buckets within `maxLeash` of it, nearest first, and **stop at the first post of
   another side whose leash contains it**. In a battle the first bucket almost always answers.
3. Post or refresh the order exactly as now; `Retire` unchanged.

In a dense fight this is O(agents) instead of O(soldiers × leash²). Expected cost at 600: well under 1 ms with
Burst, a few ms without. Burst-compile it either way: `OrderBook`, `NativeHashSet` and the lookups are all
Burst-compatible.

- Keeps the design: a threat is still an order, not a reflex (§14 step 13), and the leash is still measured
  from the post.
- Tests: the existing threat/leash tests in `RtsTests` must pass unchanged; add one with two posts of different
  leash in the same bucket, and one with a third faction.
- Cheaper stop-gap if P1 has to wait: just add `[BurstCompile]` and replace `QueryAABB` + list with
  `ForEachInAABB` and an early-out processor. Probably 10–20x, which is enough for 600 with P0 in place but
  still scales with leash area.

### P2 — Chasers that can see their target walk straight at it

Most of a melee happens at short range, and a flow field per moving target is the wrong tool there. In
`TaskStepSystem`'s `Engage`, when the target is within N cells (8, say) and a grid line from chaser to target is
passable (DDA over `GridMap`, Burst-cheap), skip the field: set the walk target directly and let avoidance do
the rest. Only chasers further away, or with a wall between, ask for a field.

Alongside it, **quantise far goals**: a chaser more than N cells out asks for a field to its target's cell
rounded to a 4×4 block, so a column of soldiers converging on a formation shares a handful of fields rather
than one each. And raise `FlowFieldCache.CAPACITY` (32 → 128) and `MAX_BUILDS_PER_FRAME` (4 → 16): the build is
already a parallel Burst job, so the cap was budgeting for a cost that is ~0.5 ms per field.

- Risk: a straight-line walk that ignores cost will cross expensive ground (a road discount, a breachable wall)
  that a field would route round. Inside 8 cells that is the right trade for a fight; it should not be used for
  ordinary `GoTo`.
- Measure with the same probe as §2: the fraction of walking soldiers with a usable direction should go from
  ~25% to ~100%.

### P3 — Fight orders get their own claim budget

Keep `MAX_CLAIMS_PER_TICK = 16` for economy orders, where it protects reservations and the thundering herd. Give
fight orders a separate, larger budget (256), since they reserve nothing. To keep that affordable, find the
nearest eligible soldier through the agent spatial hash (expanding rings from the target) instead of the linear
`TryNearestAgent` scan over every free agent, and Burst-compile the system (`RtsPerformance.md` §3).

### P4 — Smaller things worth doing on the way

- **Re-target on kill.** When an `Engage` ends because the target died, today the soldier waits for the order
  market to hand it the next one (P3 makes that faster). Letting it claim the nearest unclaimed fight order in
  range on the spot would make a melee flow; it is still one order per threat, so it does not reintroduce the
  pile-on. A design call, so flagged rather than proposed outright.
- **Combat view.** Measure its steady state at 600 once the rest runs; if it is more than a couple of ms, move the soldier root transforms into
  the agent pool's `IJobParallelForTransform` and keep only swings and bullets on the main thread.
- **Debug overlays.** The big battle scene has `AgentDebugOverlay` and `SelectedAgentOverlay`, which draw gizmos
  per agent in the Scene view. Leave them out of the big scene or default them off.
- **Spatial hash cell size.** `RtsPerformance.md` §4 already recommends `CELL_SIZE` 2–3; every query above
  benefits, including P1's stop-gap.

## 4. Order, and how to know it worked

| step | expected effect at 600 armed agents |
| --- | --- |
| P0 | no more hang; ~2 fps instead of a frozen Editor |
| P1 | threat detection 450 ms → < 1 ms; economy tick < 10 ms |
| P2 | ~all chasing soldiers moving instead of ~25% |
| P3 | every soldier engaged within ~1 s of the start instead of ~4 s |

Acceptance: `RtsBigBattle.unity` at 300 vs 300 holds 30+ fps in the Editor through the whole fight, no economy
tick over 10 ms, and the existing 195 `RtsTests` pass.

The measurements here came from three throwaway editor scripts (run every system once with timings; count
walking soldiers with a built field; open the big battle with a smaller army in memory). Worth turning the first
into a permanent `Tools/RTS/Profile Systems` menu item so the numbers can be re-taken after each step.

## 5. Result

Implemented as proposed, with two changes found while measuring:

- **P3's budget is 1024, not 256.** A cap below the size of a battle is a bias as well as a delay. Orders are
  posted in entity order, so one side's targets are handed out first and the *other* side marches a tick or
  two early. Between evenly matched armies that snowballs: at 256 a tick whichever side spawned first won
  131 to 0, every time, both ways round. With every fight order out in the same tick, spawn order stopped
  deciding it.
- **Both sides' ranks share a spawn seed** (`RtsBattleWorld`). With a seed each, the slightly better random
  formation won by about 90 to nothing; with one seed the result goes either way and stays close for most of
  the fight.

`RtsBigBattle.unity`, 300 vs 300, free-running in the Editor:

| | before | after |
| --- | --- | --- |
| first frames | Editor hangs for good | ~6 ms a frame |
| `ThreatDetectionSystem` | 450 ms a tick | under 1 ms |
| fight orders still waiting after 3 ticks | 552 of 584 | under 30 |
| walking soldiers with nowhere to go | ~75% (at 100 vs 100) | 0 |
| distinct flow fields asked for | ~one per chaser | ~70 at the peak, shared by 250+ chasers |
| frame time through the fight | — | 6 ms at the start, falling to under 2 ms |

An accidental 600 vs 600 (1,200 agents) ran at ~12 ms a frame and stayed even to the end.

All 457 EditMode tests pass, including three new ones for the rate manager (`RateManagerTests`).

