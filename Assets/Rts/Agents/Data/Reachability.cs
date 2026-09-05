using GridNav;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// The two grid readings that answer "is it even worth sending anyone there", carried together so the
    /// question can be asked in one line wherever a destination is being chosen.
    ///
    /// It only ever *refuses* on a definite no - see <see cref="FlowFieldCache.IsKnownUnreachable"/>. The
    /// first attempt at a destination nobody has walked to is always allowed, which is what builds the field
    /// that answers the question properly from then on.
    ///
    /// It lives out here rather than inside the one system that first needed it because choosing a
    /// destination is not an order-assignment idea: a gatherer picking which tree to fell asks exactly the
    /// same question, and so will a soldier picking what to walk at.
    /// </summary>
    public readonly struct Reachability
    {
        private readonly FlowFieldCache _fields;
        private readonly GridMap _map;

        public Reachability(FlowFieldCache fields, GridMap map)
        {
            _fields = fields;
            _map = map;
        }

        public bool CanTry(int2 destination, int2 from) =>
            !_fields.IsKnownUnreachable(destination, from, _map);

        public bool CanTry(int2 destination, float2 from) =>
            CanTry(destination, GridCoords.CellOf(from));
    }
}
