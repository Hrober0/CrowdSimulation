using GridNav;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// A building that carries agents across its own footprint, one way.
    ///
    /// It is the fifth use of "an agent is here rather than on the map" (design §6) and the first one that is
    /// not a destination. A hut, a workshop and a warehouse are places an agent *goes*; a bridge is a place it
    /// goes *through*, on its way somewhere the bridge knows nothing about. That difference is why the whole
    /// feature needed a <see cref="NavLink"/> in the grid rather than another task step: a worker walks to a
    /// bench because its order says so, and an agent walks over a bridge because the route does, and only the
    /// routing layer can say that.
    ///
    /// **The structure is two piers and a gap.** The cell just inside each mouth is solid footprint; the ground
    /// between them is left exactly as it was, because the deck is overhead and what is underneath is still
    /// map. So traffic crosses the line of a bridge by walking under it, and the only way to travel *along*
    /// that line is the link - the piers close both ends of it, and nothing here ever clears a cell, so a
    /// bridge can neither open a river it spans nor punch a hole in a wall it crosses.
    ///
    /// **A crossing is a walk with the logic taken out.** The agent keeps its position, its load and its place
    /// in everyone else's avoidance neighbours; what it loses is <see cref="PathFollow"/>, so nothing steers
    /// it, nothing integrates it and the watchdog does not watch it. It is moved along the deck at its own
    /// <see cref="AgentMove.MaxSpeed"/>, which is what makes a bridge cost what walking it would - the flow
    /// field is told the same number, so the route and the crossing agree.
    /// </summary>
    public struct Bridge : ICleanupComponentData
    {
        /// <summary>
        /// The walkable cell on the near bank. An agent standing here whose route says "over the bridge" is a
        /// candidate for admission, and nothing else about the cell is special - it is flagged
        /// <see cref="CellFlags.Entrance"/> and <see cref="CellFlags.NoIdle"/> exactly as a doorstep is.
        /// </summary>
        public int2 Entry;

        /// <summary>The walkable cell on the far bank, where an agent is put down.</summary>
        public int2 Exit;

        /// <summary>
        /// Cells from <see cref="Entry"/> to <see cref="Exit"/>: a three-cell bridge spans four, because the
        /// mouths sit outside the structure at either end.
        /// </summary>
        public int Span;

        /// <summary>
        /// How many agents may be on the deck at once.
        ///
        /// Not a tuning number: it is the span, because occupants are spaced a cell apart and a cell is what
        /// one agent takes up. Making it larger would pack agents closer than they stand anywhere else on the
        /// map, and making it smaller would leave deck standing empty while a queue formed at the mouth.
        /// </summary>
        public int Capacity => math.max(Span, 1);

        /// <summary>
        /// Which way the deck runs, as a unit vector from <see cref="Entry"/> to <see cref="Exit"/>. The
        /// crossing is a straight line, which is the one assumption a bridge is allowed to make about itself.
        /// </summary>
        public float2 Heading =>
            math.normalizesafe(GridCoords.CellCenter(Exit) - GridCoords.CellCenter(Entry));

        /// <summary>
        /// Works out the whole shape of a bridge from its two mouths, or says it is not a bridge.
        ///
        /// **One place, because three callers have to agree.** Validation asks "may this go here", placement
        /// asks "what do I take", and both have to name the same cells or a bridge is checked against one shape
        /// and built as another. The shape is arithmetic on two cells, so sharing it costs nothing and getting
        /// it twice would eventually cost a bridge that passed its own check and then sealed itself.
        /// </summary>
        public static bool TryShape(int2 entry, int2 exit, out BridgeShape shape)
        {
            shape = default;

            int2 delta = exit - entry;
            int span = math.abs(delta.x) + math.abs(delta.y);

            // In line, and long enough to have two distinct abutments with the mouths outside them.
            if ((delta.x != 0 && delta.y != 0) || span < 3)
            {
                return false;
            }

            int2 step = new(math.clamp(delta.x, -1, 1), math.clamp(delta.y, -1, 1));

            shape = new BridgeShape
            {
                Entry = entry,
                Exit = exit,
                Step = step,
                Span = span,
                NearPier = entry + step,
                FarPier = exit - step,
            };

            return true;
        }
    }

    /// <summary>
    /// A bridge's cells, worked out from its two mouths (see <see cref="Bridge.TryShape"/>).
    ///
    /// The structure is **two piers and a gap**: the cell inside each mouth is solid, and everything between
    /// them is left exactly as it was. That is what makes a bridge something traffic passes *under* rather than
    /// a wall with a hole in it - the deck is overhead, so the ground below is still ground, and an agent
    /// walking across the line of the bridge is walking under it.
    ///
    /// Nothing here ever *clears* a cell. The piers add cost and give the same cost back, so a bridge cannot
    /// open a river it was built over, and the gap cannot become a way through a wall the bridge crosses.
    /// </summary>
    public struct BridgeShape
    {
        /// <summary>The walkable cell agents step on from. Outside the structure.</summary>
        public int2 Entry;

        /// <summary>The walkable cell they are put down on. Outside the structure.</summary>
        public int2 Exit;

        /// <summary>Unit step from <see cref="Entry"/> towards <see cref="Exit"/>.</summary>
        public int2 Step;

        /// <summary>Cells from one mouth to the other.</summary>
        public int Span;

        /// <summary>The solid cell just inside the near mouth.</summary>
        public int2 NearPier;

        /// <summary>The solid cell just inside the far mouth. Equal to the near one on the shortest bridge.</summary>
        public int2 FarPier;

        /// <summary>How many cells of open ground the deck passes over. Zero on the shortest bridge.</summary>
        public int GapCells => math.max(Span - 3, 0);

        /// <summary>One of the cells the deck passes over, indexed from the near pier.</summary>
        public int2 GapCell(int index) => NearPier + Step * (index + 1);
    }

    /// <summary>
    /// One agent on the deck, and how far along it is.
    ///
    /// **Ordered front first**, index 0 being the one nearest the exit. That ordering is the whole of "single
    /// file": an occupant may never advance past the one in front of it, so a bridge whose head cannot get off
    /// backs up to its mouth and stops admitting - which is the behaviour a bridge has to have and not a rule
    /// anyone had to write. Nothing overtakes because there is nowhere to overtake into.
    ///
    /// The distance lives here and only here. The agent knows *which* bridge it is on (see
    /// <see cref="OnBridge"/>) because something has to be able to put it back on the map if the bridge stops
    /// existing, but where it is on the deck is the bridge's business - it is the ordering that gives the
    /// number its meaning, and the ordering is this buffer.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct BridgeOccupant : ICleanupBufferElementData
    {
        public Entity Agent;

        /// <summary>Cells travelled from <see cref="Bridge.Entry"/>. Reaches <see cref="Bridge.Span"/> at the exit.</summary>
        public float Distance;

        /// <summary>
        /// How far the agent still is from the deck's centre line, as an offset added to its position.
        ///
        /// An agent is admitted from wherever on the mouth cell it happened to stop, which is almost never the
        /// centre line the deck runs along. Snapping it there on the frame it boards is a visible jump onto the
        /// bridge - small, but the one moment in the crossing that does not look like walking. So the offset it
        /// boarded with is recorded and walked off at the agent's own speed, which is the same rule the rest of
        /// the crossing uses.
        /// </summary>
        public float2 Offset;

        /// <summary>
        /// Seconds spent at the far end unable to get off.
        ///
        /// The head of the deck is the one agent in the game with no watchdog behind it - it is not walking, so
        /// <see cref="WatchdogSystem"/> cannot see it, and it is waiting on a cell rather than on a queue that
        /// promotes. Without a bound on that wait, a far bank somebody never leaves is a bridge that stops
        /// forever with nothing anywhere reporting it, which is precisely the shape of failure the design keeps
        /// refusing to allow. So the wait is counted, and past a limit the agent is put down anyway: a moment of
        /// overlap that avoidance sorts out in a few frames, against a deadlock that nothing sorts out at all.
        /// </summary>
        public float Waiting;
    }
}
