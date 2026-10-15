using System.Collections.Generic;
using Engine.Components;
using Engine.Entities;
using Microsoft.Xna.Framework;

namespace Engine.Logic
{
    /// <summary>
    /// Blends the scene's post processing with the Post Process Volume components around the camera (Source 2 look,
    /// phase 5.1). The renderer calls <see cref="Evaluate"/> every frame before copying the result into GameSettings.
    /// </summary>
    public static class PostProcessVolumes
    {
        private static readonly List<(int Priority, int Order, float Weight, PostProcessVolumeComponent Volume)> Active = new();

        /// <summary>
        /// Writes into <paramref name="result"/> the scene's settings (<paramref name="scene"/>, defaults when null)
        /// moved towards each volume by its weight at <paramref name="camera"/>, lowest priority first. Returns it.
        /// Also keeps the volumes' meshes hidden.
        /// </summary>
        public static PostProcessSettings Evaluate(PostProcessSettings scene, IReadOnlyList<BasicEntity> entities, Vector3 camera,
            PostProcessSettings result)
        {
            result ??= new PostProcessSettings();
            result.BlendTowards(scene ?? Defaults, 1);
            result.BlendLutPath = null;
            result.LutBlend = 0;

            Active.Clear();
            if (entities != null)
            {
                for (int i = 0; i < entities.Count; i++)
                {
                    BasicEntity entity = entities[i];
                    PostProcessVolumeComponent volume = entity?.GetComponent<PostProcessVolumeComponent>();
                    if (volume == null) continue;
                    bool enabled = volume.Enabled && entity.IsEnabled;
                    PostProcessVolumeComponent.SetMeshHidden(entity, enabled);
                    if (!enabled || volume.Overrides == null || volume.Overrides.Count == 0) continue;
                    float weight = volume.WeightAt(entity, camera);
                    if (weight > 0) Active.Add((volume.Priority, i, weight, volume));
                }
            }
            if (Active.Count == 0) return result;

            Active.Sort((a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : a.Order.CompareTo(b.Order));
            foreach (var (_, _, weight, volume) in Active)
                result.BlendTowards(volume.Values, weight, volume.Overrides);
            result.Normalize();
            return result;
        }

        private static readonly PostProcessSettings Defaults = new PostProcessSettings();
    }
}
