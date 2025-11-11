using System;
using System.Collections.Generic;
using HCore.Extensions;
using HCore.Shapes;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Navigation
{
    public static class DebugUtils
    {
        public static void Draw<TKey, TValue>(this NativeParallelMultiHashMap<TKey, TValue> map, Color color, float? duration = null)
            where TValue : unmanaged, IOutline where TKey : unmanaged, IEquatable<TKey>
        {
            using NativeArray<TKey> keys = map.GetKeyArray(Allocator.Temp);
            foreach (var key in keys)
            {
                foreach (var value in map.GetValuesForKey(key))
                {
                    value.DrawBorder(color, duration);
                }
            }
        }

        public static void DrawLoop(this IEnumerable<float2> points, Color color, float? duration = null)
        {
            float2? start = null;
            float2 last = float2.zero;
            foreach (var point in points)
            {
                if (!start.HasValue)
                {
                    start = point;
                }
                else
                {
                    Draw(point, last, color, duration);
                }
                last = point;
            }

            if (start != null)
            {
                Draw(last, start.Value, color, duration);
            }
        }

        public static void Draw(float2 p, float2 p2, Color color, float? duration = null)
        {
            if (duration.HasValue)
            {
                Debug.DrawLine(p.To3D(), p2.To3D(), color, duration.Value);
            }
            else
            {
                Debug.DrawLine(p.To3D(), p2.To3D(), color);
            }
        }

        public static bool IsSelected(this GameObject gameObject) => IsGameObjectSelected(gameObject);
        public static bool IsGameObjectSelected(GameObject gameObject)
        {
#if UNITY_EDITOR
            return UnityEditor.Selection.gameObjects != null && System.Array.IndexOf(UnityEditor.Selection.gameObjects, gameObject) >= 0;
#else
            return false;
#endif
        }
        
        /// <summary>
        /// Use position, scale and rotation to calculate verticies in CCW order
        /// </summary>
        public static float2[] GetRectangleFromTransform(Transform transform)
        {
            var p = (float2)(Vector2)transform.position;
            var halfSize = (float2)(Vector2)transform.lossyScale * 0.5f;

            // rotation in radians
            float rad = math.radians(transform.rotation.eulerAngles.z);
            float cos = math.cos(rad);
            float sin = math.sin(rad);

            // define local rectangle corners (unrotated, relative to center)
            float2[] localCorners =
            {
                new float2(-halfSize.x, -halfSize.y),
                new float2(halfSize.x, -halfSize.y),
                new float2(halfSize.x, halfSize.y),
                new float2(-halfSize.x, halfSize.y),
            };

            // rotate and translate to world space
            var result = new float2[4];
            for (int i = 0; i < 4; i++)
            {
                float2 c = localCorners[i];
                float2 rotated = new(
                    c.x * cos - c.y * sin,
                    c.x * sin + c.y * cos
                );
                result[i] = p + rotated;
            }

            return result;
        }
        
        public static async Awaitable WaitForClick(KeyCode key = KeyCode.Space)
        {
            do
            {
                await Awaitable.NextFrameAsync();
            } while (!Input.GetKeyDown(key));
        }
    }
}