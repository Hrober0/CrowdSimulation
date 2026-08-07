using System;
using Rts;
using Unity.Entities;
using UnityEngine;

namespace Examples.Rts
{
    /// <summary>
    /// Turns one set of items into another (design §14 step 7). Goes on the same object as
    /// <see cref="BuildingAuthoring"/> and <see cref="StorageAuthoring"/>: the recipe only says what becomes
    /// what, and the slots it names have to exist on the storage beside it.
    ///
    /// The interior capacity on the <see cref="BuildingAuthoring"/> is this building's number of work
    /// benches - a work slot and an interior slot are the same thing (§6).
    /// </summary>
    public class CrafterAuthoring : MonoBehaviour
    {
        [Serializable]
        private struct Ingredient
        {
            [Min(1)] public ushort Item;

            [Min(1)] public int Amount;
        }

        [SerializeField, Min(0.1f), Tooltip("Seconds of work per batch.")]
        private float _craftSeconds = 2f;

        [SerializeField, Range(0, 10)]
        [Tooltip("Priority of the work order. Nothing to do with the priorities on its storage slots.")]
        private int _priority = 5;

        [SerializeField] private Ingredient[] _inputs = Array.Empty<Ingredient>();

        [SerializeField] private Ingredient[] _outputs = Array.Empty<Ingredient>();

        private class CrafterBaker : Baker<CrafterAuthoring>
        {
            public override void Bake(CrafterAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);

                AddComponent(entity, new Recipe
                {
                    CraftSeconds = authoring._craftSeconds,
                    Priority = (byte)Mathf.Clamp(authoring._priority, 0, 255),
                });

                DynamicBuffer<RecipeInput> inputs = AddBuffer<RecipeInput>(entity);
                foreach (Ingredient ingredient in authoring._inputs)
                {
                    inputs.Add(new RecipeInput
                    {
                        Item = new ItemId(ingredient.Item),
                        Amount = ingredient.Amount,
                    });
                }

                DynamicBuffer<RecipeOutput> outputs = AddBuffer<RecipeOutput>(entity);
                foreach (Ingredient ingredient in authoring._outputs)
                {
                    outputs.Add(new RecipeOutput
                    {
                        Item = new ItemId(ingredient.Item),
                        Amount = ingredient.Amount,
                    });
                }
            }
        }
    }
}
