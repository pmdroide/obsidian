using System;
using System.Collections.Generic;
using Engine.Components;
using Engine.Entities;
using Engine.Renderer.Helper;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Renderer.RenderModules
{
    /// <summary>The mirror image of one Planar Reflection pane (rgb = light, a = coverage; empty where nothing was drawn).</summary>
    public sealed class PlanarReflectionView : IDisposable
    {
        public RenderTarget2D Map;
        /// <summary>Drawn this frame for the current camera.</summary>
        public bool Ready;

        public void Dispose()
        {
            Map?.Dispose();
            Map = null;
            Ready = false;
        }
    }

    /// <summary>
    /// Planar reflections (<see cref="PlanarReflectionComponent"/>): before the forward pass, the
    /// <see cref="MaxReflections"/> nearest visible panes each get the opaque scene drawn from the camera mirrored about
    /// their surface, through the forward shader (sun with cascades, point/spot lights, probe volume, reflection probes;
    /// no fog, decals, SSAO or emissive glow). The mirrored camera keeps the main projection, so the glass reads its
    /// image at its own screen position. Mip mapped for rough glass.
    /// </summary>
    public sealed class PlanarReflectionRenderModule : IDisposable
    {
        public const int MaxReflections = 2;

        private readonly PlanarReflectionView[] _views = new PlanarReflectionView[MaxReflections];
        private readonly List<(BasicEntity Owner, PlanarReflectionComponent Component, Plane Plane, float Distance)> _candidates = new();
        private readonly List<BasicEntity> _attached = new();
        private readonly BoundingFrustum _frustum = new BoundingFrustum(Matrix.Identity);

        /// <summary>Panes drawn this frame.</summary>
        public int Count { get; private set; }

        /// <summary>
        /// Picks the panes in view and draws their mirror images (changes the render target). Attaches each image to
        /// its gameobject's <see cref="TransformMatrix.PlanarReflection"/> for the forward pass.
        /// </summary>
        public void Draw(GraphicsDevice graphics, FullScreenTriangle triangle, IReadOnlyList<BasicEntity> entities, MeshMaterialLibrary meshMat,
            ForwardRenderModule forward, ForwardRenderModule.Context context, Matrix viewProjection, BoundingFrustum viewFrustum, int width, int height)
        {
            foreach (BasicEntity owner in _attached) owner.WorldTransform.PlanarReflection = null;
            _attached.Clear();
            foreach (PlanarReflectionView view in _views)
                if (view != null) view.Ready = false;
            Count = 0;

            Vector3 camera = context.Camera.Position;
            _candidates.Clear();
            if (entities != null)
            {
                for (int i = 0; i < entities.Count; i++)
                {
                    BasicEntity entity = entities[i];
                    PlanarReflectionComponent component = entity?.GetComponent<PlanarReflectionComponent>();
                    if (component == null || !component.Enabled || !entity.IsEnabled || entity.WorldTransform.Hidden) continue;
                    if (!PlanarReflectionComponent.TryGetPlane(entity, camera, out Plane plane, out BoundingSphere bounds)) continue;
                    if (viewFrustum != null && viewFrustum.Contains(bounds) == ContainmentType.Disjoint) continue;
                    float distance = Math.Max(0, Vector3.Distance(camera, bounds.Center) - bounds.Radius);
                    _candidates.Add((entity, component, plane, distance));
                }
            }
            if (_candidates.Count == 0) return;
            _candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            //The passes after this one (Compose's full-screen triangle, the depth reconstruction) rely on the state
            //they find: a depth-writing state left here would keep the reconstructed depth from being written
            BlendState blend = graphics.BlendState;
            DepthStencilState depth = graphics.DepthStencilState;
            RasterizerState rasterizer = graphics.RasterizerState;

            for (int i = 0; i < Math.Min(_candidates.Count, MaxReflections); i++)
            {
                var (owner, component, plane, _) = _candidates[i];
                float scale = component.ClampedResolutionScale;
                PlanarReflectionView view = _views[i] ??= new PlanarReflectionView();
                EnsureTarget(graphics, view, Math.Max(16, (int)(width * scale)), Math.Max(16, (int)(height * scale)));

                Matrix mirror = Matrix.CreateReflection(plane);
                Matrix mirroredViewProjection = mirror * viewProjection;
                _frustum.Matrix = mirroredViewProjection;
                meshMat.ReflectionCull = new MeshMaterialLibrary.ReflectionCulling
                {
                    Frustum = _frustum,
                    Plane = plane,
                    Camera = camera,
                    Distance = component.ClampedRenderDistance,
                    Exclude = owner.WorldTransform,
                };

                graphics.SetRenderTarget(view.Map);
                graphics.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Vector4.Zero, 1, 0);
                Vector3 eye = Vector3.Transform(camera, mirror);
                forward.DrawReflection(meshMat, mirroredViewProjection, context, eye, Vector3.TransformNormal(context.Camera.Forward, mirror), plane);
                //The sky where nothing was drawn (the global probe's captured scene would show there at the wrong parallax)
                forward.DrawReflectionSky(graphics, triangle, mirroredViewProjection, eye, context);

                meshMat.ReflectionCull = default;
                view.Ready = true;
                owner.WorldTransform.PlanarReflection = view;
                _attached.Add(owner);
                Count++;
            }
            //Resolves the mip chain of the last image too
            graphics.SetRenderTarget(null);
            graphics.BlendState = blend;
            graphics.DepthStencilState = depth;
            graphics.RasterizerState = rasterizer;
        }

        private static void EnsureTarget(GraphicsDevice graphics, PlanarReflectionView view, int width, int height)
        {
            if (view.Map != null && !view.Map.IsDisposed && view.Map.Width == width && view.Map.Height == height) return;
            view.Map?.Dispose();
            view.Map = new RenderTarget2D(graphics, width, height, true, SurfaceFormat.HalfVector4, DepthFormat.Depth24, 0,
                RenderTargetUsage.DiscardContents);
        }

        /// <summary>Drops every image (scene swap); the next frame draws them again.</summary>
        public void Clear()
        {
            foreach (BasicEntity owner in _attached) owner.WorldTransform.PlanarReflection = null;
            _attached.Clear();
        }

        public void Dispose()
        {
            Clear();
            for (int i = 0; i < _views.Length; i++)
            {
                _views[i]?.Dispose();
                _views[i] = null;
            }
        }
    }
}
