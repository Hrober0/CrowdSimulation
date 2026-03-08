using UnityEngine;

namespace Examples.Storage.UI
{
    public static class PanelStyles
    {
        public static Color ResourceColor(ResourceType r) => r switch
        {
            ResourceType.Wood => new Color(0.7f, 0.5f, 0.2f),
            ResourceType.Stone => new Color(0.6f, 0.6f, 0.6f),
            ResourceType.Iron => new Color(0.5f, 0.7f, 0.9f),
            ResourceType.Food => new Color(0.4f, 0.8f, 0.4f),
            ResourceType.Coal => new Color(0.4f, 0.4f, 0.5f),
            ResourceType.Gold => new Color(1.0f, 0.8f, 0.2f),
            _ => Color.grey,
        };
    }
}