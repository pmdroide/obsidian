using System.Collections.Generic;
using Engine.Entities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Renderer.Helper
{
    /// <summary>
    /// Screen-to-world ray construction and ray-vs-AABB intersection for editor object picking.
    /// The render-target ID buffer (<see cref="Engine.Renderer.RenderModules.IdAndOutlineRenderer"/>)
    /// stays the primary selection path; these helpers are the fallback used when the ID buffer
    /// is unavailable, and the building blocks for any future Anvil-side pointer-event picking.
    /// </summary>
    public static class Picking
    {
        public static Ray ScreenPointToWorldRay(int mouseX, int mouseY,
            int viewportWidth, int viewportHeight,
            Matrix projection, Matrix view,
            GraphicsDevice graphicsDevice)
        {
            Viewport vp = new Viewport(0, 0, viewportWidth, viewportHeight);
            Vector3 near = vp.Unproject(new Vector3(mouseX, mouseY, 0f), projection, view, Matrix.Identity);
            Vector3 far = vp.Unproject(new Vector3(mouseX, mouseY, 1f), projection, view, Matrix.Identity);
            Vector3 dir = far - near;
            dir.Normalize();
            return new Ray(near, dir);
        }

        public static bool RayIntersectsAabb(Ray ray, BoundingBox box, out float distance)
        {
            float? d = ray.Intersects(box);
            distance = d ?? 0f;
            return d.HasValue;
        }

        public static int? PickEntity(Ray ray, IReadOnlyList<BasicEntity> entities)
        {
            int? hitId = null;
            float hitDistance = float.MaxValue;

            for (int i = 0; i < entities.Count; i++)
            {
                BasicEntity e = entities[i];
                if (!e.IsEnabled) continue;

                BoundingBox world = ComputeWorldBoundingBox(e);
                if (RayIntersectsAabb(ray, world, out float d) && d < hitDistance)
                {
                    hitDistance = d;
                    hitId = e.Id;
                }
            }

            return hitId;
        }

        /// <summary>
        /// Transforms the entity's model-space AABB into world space. Conservative
        /// (axis-aligned around the rotated box), matches the gizmo-side picking
        /// granularity. For tighter picks use the ID buffer.
        /// </summary>
        public static BoundingBox ComputeWorldBoundingBox(BasicEntity entity)
        {
            BoundingBox local = entity.BoundingBox;
            Vector3 offset = entity.BoundingBoxOffset * entity.Scale;
            Matrix world = Matrix.CreateScale(entity.Scale) * entity.RotationMatrix * Matrix.CreateTranslation(entity.Position + offset);

            Vector3[] corners = local.GetCorners();
            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 t = Vector3.Transform(corners[i], world);
                min = Vector3.Min(min, t);
                max = Vector3.Max(max, t);
            }
            return new BoundingBox(min, max);
        }
    }
}
