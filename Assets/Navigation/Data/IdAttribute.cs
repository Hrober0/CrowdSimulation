using System.Collections.Generic;

namespace Navigation
{
    public struct IdAttribute : INodeAttributes<IdAttribute>
    {
        private const uint MULTIPLIER = 32;
        private ulong _id;
        private int _n;

        public IdAttribute(int id)
        {
            _id = (uint)id;
            _n = 1;
        }

        public IdAttribute Empty() => new IdAttribute
        {
            _id = 0ul,
            _n = 0
        };

        public void Merge(IdAttribute other)
        {
            var tid = other._id;
            for (int i = other._n; i > 0; i--)
            {
                var c = tid % MULTIPLIER;
                tid /= MULTIPLIER;
                if (!Contains((int)c))
                {
                    _id = _id * MULTIPLIER + c;
                    _n++;
                }
            }
        }

        public readonly List<int> GetIds()
        {
            var result = new List<int>((int)_n);
            var tid = _id;
            for (int i = _n; i > 0; i--)
            {
                var c = tid % MULTIPLIER;
                tid /= MULTIPLIER;
                result.Add((int)c);
            }

            return result;
        }

        public readonly int Entries => _n;

        private readonly bool Contains(int id)
        {
            var tid = _id;
            for (int i = _n; i > 0; i--)
            {
                var c = tid % MULTIPLIER;
                if ((int)c == id)
                {
                    return true;
                }

                tid /= MULTIPLIER;
            }

            return false;
        }
    }
}