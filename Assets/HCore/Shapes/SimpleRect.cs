namespace HCore.Shapes
{
    public readonly struct SimpleRect
    {
        public float MinX { get; }
        public float MaxX { get; }
        public float MinY { get; }
        public float MaxY { get; }

        public SimpleRect(float minX, float maxX, float minY, float maxY)
        {
            MinX = minX;
            MaxX = maxX;
            MinY = minY;
            MaxY = maxY;
        }

        public override string ToString() => $"{MinX} {MinY} {MaxX} {MaxY}";
    }
}