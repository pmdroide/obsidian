using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Renderer.Lighting
{
    /// <summary>One cached texture for the forward pass's selected spot cookies, sharing duplicates.</summary>
    public sealed class ForwardCookieAtlas : IDisposable
    {
        private readonly List<Texture2D> _cookies = new();
        private readonly Dictionary<Texture2D, Vector4> _rects = new();
        private SpriteBatch _sprites;
        public RenderTarget2D Texture { get; private set; }

        public Vector4 Rect(Texture2D cookie) => cookie != null && _rects.TryGetValue(cookie, out var rect) ? rect : Vector4.Zero;

        /// <summary>Call before rendering the scene: rebuilding switches render targets. Cookie sources remain externally owned.</summary>
        public void Update(GraphicsDevice graphics, IReadOnlyList<Texture2D> cookies)
        {
            bool same = cookies.Count == _cookies.Count;
            foreach (var cookie in cookies) same &= _rects.ContainsKey(cookie);
            if (same) return;
            _cookies.Clear();
            _cookies.AddRange(cookies);
            _rects.Clear();
            Texture?.Dispose();
            Texture = null;
            if (cookies.Count == 0) return;

            int side = 1;
            foreach (var cookie in cookies) side = Math.Max(side, Math.Max(cookie.Width, cookie.Height));
            side = Math.Min(side, 512); // Bound memory for large user cookies; most cookies are 256 or smaller.
            int columns = (int)Math.Ceiling(Math.Sqrt(cookies.Count));
            int rows = (cookies.Count + columns - 1) / columns;
            Texture = new RenderTarget2D(graphics, side * columns, side * rows, false,
                SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            _sprites ??= new SpriteBatch(graphics);
            var targets = graphics.GetRenderTargets();
            var viewport = graphics.Viewport;
            var blend = graphics.BlendState;
            var depth = graphics.DepthStencilState;
            var rasterizer = graphics.RasterizerState;
            var sampler = graphics.SamplerStates[0];
            try
            {
                graphics.SetRenderTarget(Texture);
                graphics.Clear(Color.Black);
                _sprites.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp,
                    DepthStencilState.None, RasterizerState.CullNone);
                for (int i = 0; i < cookies.Count; i++)
                {
                    int x = i % columns * side, y = i / columns * side;
                    _sprites.Draw(cookies[i], new Rectangle(x, y, side, side), Color.White);
                    // Sample only within this cell's texel centres: no neighbour bleeding with bilinear filtering.
                    _rects[cookies[i]] = new Vector4((x + 0.5f) / Texture.Width, (y + 0.5f) / Texture.Height,
                        (side - 1f) / Texture.Width, (side - 1f) / Texture.Height);
                }
                _sprites.End();
            }
            finally
            {
                graphics.SetRenderTargets(targets);
                graphics.Viewport = viewport;
                graphics.BlendState = blend;
                graphics.DepthStencilState = depth;
                graphics.RasterizerState = rasterizer;
                graphics.SamplerStates[0] = sampler;
            }
        }

        public void Dispose()
        {
            Texture?.Dispose();
            Texture = null;
            _sprites?.Dispose();
            _sprites = null;
            _cookies.Clear();
            _rects.Clear();
        }
    }
}
