namespace Examples.Storage
{
    public enum ResourceType : byte
    {
        None  = 0,
        Wood  = 1,
        Stone = 2,
        Iron  = 3,
        Food  = 4,
        Coal  = 5,
        Gold  = 6,
    }

    public static class ResourceTypeExt
    {
        public static string DisplayName(this ResourceType r) => r switch
        {
            ResourceType.Wood  => "Wood",
            ResourceType.Stone => "Stone",
            ResourceType.Iron  => "Iron",
            ResourceType.Food  => "Food",
            ResourceType.Coal  => "Coal",
            ResourceType.Gold  => "Gold",
            _                  => "—",
        };
    }
}