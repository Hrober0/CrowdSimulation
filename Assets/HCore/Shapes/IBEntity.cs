using System.Collections.Generic;

namespace HCore.Shapes
{
    public interface IBEntity
    {
        IShape Bounds { get; }
    }

    public static class BoundsEntityExtensions
    {
        public static IEnumerable<T> FindIntersected<T>(IShape range, ICollection<T> entities) where T : IBEntity
        {
            foreach (var entity in entities)
            {
                if (range.Intersects(entity.Bounds))
                {
                    yield return entity;
                }
            }
        }
        public static IEnumerable<T> FindIntersectedRect<T>(IShape range, ICollection<T> entities) where T : IBEntity
        {
            foreach (var entity in entities)
            {
                if (range.IntersectsRect(entity.Bounds))
                {
                    yield return entity;
                }
            }
        }

        public static bool IsCollision<T>(IShape range, ICollection<T> entities) where T : IBEntity
        {
            foreach (var entity in entities)
            {
                if (range.Intersects(entity.Bounds))
                {
                    return true;
                }
            }
            return false;
        }
        public static bool IsCollisionRect<T>(IShape range, ICollection<T> entities) where T : IBEntity
        {
            foreach (var entity in entities)
            {
                if (range.IntersectsRect(entity.Bounds))
                {
                    return true;
                }
            }
            return false;
        }
    }
}