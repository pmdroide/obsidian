using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Css;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Vista
{
    /// <summary>
    /// One XML/CSS document: parses it with AngleSharp, mirrors it as a <see cref="UIElement"/> tree,
    /// lays it out and draws it with a SpriteBatch.
    ///
    /// Styles are cached. Changes made through this class (<see cref="SetClass"/>,
    /// <see cref="SetVariable"/>) restyle the affected subtree automatically; after editing the DOM
    /// directly, call <see cref="Invalidate"/> (attributes/classes) or <see cref="Refresh"/> (added or
    /// removed elements).
    /// </summary>
    public class UIManager : IDisposable
    {
        // Browsers give <body> an 8px margin; Vista lays out from the window edge.
        private const string BaseCss = "html, body { margin: 0; padding: 0; }";

        private IBrowsingContext _context;
        public IDocument Document { get; private set; }
        public UIElement RootView { get; private set; }
        public UIFontRegistry Fonts { get; }

        /// <summary>
        /// Design height in UI units. When set (e.g. 1080), layout happens in a virtual canvas of that
        /// height and everything is scaled to the real viewport, so the UI looks the same at any
        /// resolution. 0 = one UI unit per pixel.
        /// </summary>
        public float ReferenceHeight { get; set; }
        /// <summary>Viewport pixels per UI unit (1 unless <see cref="ReferenceHeight"/> is set).</summary>
        public float Scale { get; private set; } = 1f;
        /// <summary>Pass to <c>SpriteBatch.Begin(transformMatrix: ...)</c> before <see cref="Draw"/>.</summary>
        public Matrix Transform => Matrix.CreateScale(Scale, Scale, 1f);

        /// <summary>Raised on the calling thread after <see cref="Load"/> replaces the document.</summary>
        public event Action Loaded;

        private Texture2D _blankTexture;
        private GraphicsDevice _graphicsDevice;
        private readonly Dictionary<IElement, UIElement> _views = new Dictionary<IElement, UIElement>();
        private readonly Dictionary<string, Texture2D> _gradients = new Dictionary<string, Texture2D>();
        private readonly Func<CssGradient, string, Texture2D> _gradientLookup;

        public UIManager(GraphicsDevice device, UIFontRegistry fonts = null)
        {
            _graphicsDevice = device;
            Fonts = fonts ?? new UIFontRegistry();
            _gradientLookup = GetGradientTexture;

            var renderDevice = new VistaRenderDevice
            {
                ViewPortWidth = device.Viewport.Width,
                ViewPortHeight = device.Viewport.Height,
                DeviceWidth = device.Viewport.Width,
                DeviceHeight = device.Viewport.Height,
                RenderWidth = (double)device.Viewport.Width,  // Safely cast to double
                RenderHeight = (double)device.Viewport.Height, // Safely cast to double
                FontSize = 16.0
            };

            // Inject your customized render layout properties into the runtime configuration
            var config = Configuration.Default
                .WithCss()
                .WithRenderDevice(renderDevice);

            _context = BrowsingContext.New(config);

            _blankTexture = new Texture2D(device, 1, 1);
            _blankTexture.SetData(new[] { Color.White });
        }

        /// <summary>
        /// Fixed: Fires initialization on a clean thread-pool thread
        /// without blocking MonoGame's main startup thread.
        /// </summary>
        public void LoadUI(string xmlPath, string cssPath)
        {
            Task.Run(async () =>
            {
                try
                {
                    if (!File.Exists(xmlPath) || !File.Exists(cssPath))
                    {
                        System.Diagnostics.Debug.WriteLine("[Vista UI] Critical Error: XML or CSS file not found.");
                        return;
                    }

                    await LoadUIAsync(xmlPath, cssPath).ConfigureAwait(false);
                    System.Diagnostics.Debug.WriteLine("[Vista UI] UI Layout Loaded Successfully.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Vista UI Critical Crash]: {ex}");
                }
            });
        }

        public async Task LoadUIAsync(string xmlPath, string cssPath)
        {
            string xmlContent = await File.ReadAllTextAsync(xmlPath).ConfigureAwait(false);
            string cssContent = UIStyles.RewriteRadialGradients(await File.ReadAllTextAsync(cssPath).ConfigureAwait(false));

            // ConfigureAwait(false) prevents deadlocking on the game's main synchronization loop
            IDocument document = await _context.OpenAsync(req => req.Content($"<style>{BaseCss}\n{cssContent}</style>{xmlContent}"))
                                               .ConfigureAwait(false);
            if (document.Body == null) return;

            // Build the whole tree before publishing it, so the game thread never sees half of it.
            var views = new Dictionary<IElement, UIElement>();
            UIElement root = BuildTree(document.Body, null, views, null);
            lock (_views)
            {
                _views.Clear();
                foreach (var pair in views) _views[pair.Key] = pair.Value;
            }
            Document = document;
            RootView = root;
        }

        /// <summary>
        /// Loads the document on the calling thread (blocking). AngleSharp runs on the thread pool,
        /// so this is safe from the game thread. Throws when a file is missing.
        /// </summary>
        public void Load(string xmlPath, string cssPath)
        {
            if (!File.Exists(xmlPath)) throw new FileNotFoundException("Vista UI document not found", xmlPath);
            if (!File.Exists(cssPath)) throw new FileNotFoundException("Vista UI stylesheet not found", cssPath);
            Task.Run(() => LoadUIAsync(xmlPath, cssPath)).GetAwaiter().GetResult();
            Loaded?.Invoke();
        }

        internal static UIElement BuildTree(IElement element, UIElement parent, Dictionary<IElement, UIElement> views,
                                           Dictionary<IElement, UIElement> reuse)
        {
            UIElement node = null;
            if (reuse != null && reuse.TryGetValue(element, out node))
            {
                node.Parent = parent;
                node.Children.Clear();
            }
            node ??= new UIElement(element, parent);
            views[element] = node;

            foreach (var childElement in element.Children)
                node.Children.Add(BuildTree(childElement, node, views, reuse));

            return node;
        }

        /// <summary>
        /// Rebuilds the visual tree after elements were added, removed or moved in <see cref="Document"/>.
        /// Existing elements keep their animation state; the changed elements are restyled.
        /// </summary>
        public void Refresh()
        {
            if (Document?.Body == null) return;
            var views = new Dictionary<IElement, UIElement>();
            UIElement root;
            lock (_views)
            {
                root = BuildTree(Document.Body, null, views, _views);
                _views.Clear();
                foreach (var pair in views) _views[pair.Key] = pair.Value;
            }
            // Selectors like :nth-child or :empty may now match differently: restyle everything
            // (cheap enough for an explicit, occasional call).
            root.MarkSubtreeDirty();
            RootView = root;
        }

        /// <summary>Restyles <paramref name="element"/> and its descendants on the next update.</summary>
        public void Invalidate(IElement element)
        {
            UIElement view = Find(element);
            if (view != null) view.MarkSubtreeDirty();
        }

        /// <summary>Restyles every element on the next update.</summary>
        public void InvalidateAll() => RootView?.MarkSubtreeDirty();

        public UIElement Find(IElement element)
        {
            if (element == null) return null;
            lock (_views) return _views.TryGetValue(element, out var view) ? view : null;
        }

        /// <summary>The visual element for the first match of <paramref name="selector"/>, or null.</summary>
        public UIElement Get(string selector) => Find(Query(selector));

        /// <summary>The first DOM element matching <paramref name="selector"/>, or null.</summary>
        public IElement Query(string selector) => Document?.QuerySelector(selector);

        public IEnumerable<IElement> QueryAll(string selector) =>
            (IEnumerable<IElement>)Document?.QuerySelectorAll(selector) ?? Array.Empty<IElement>();

        /// <summary>Adds or removes a class on every match of <paramref name="selector"/> and restyles them.</summary>
        public void SetClass(string selector, string className, bool enabled)
        {
            foreach (IElement element in QueryAll(selector)) SetClass(element, className, enabled);
        }

        public void SetClass(IElement element, string className, bool enabled)
        {
            if (element == null || element.ClassList.Contains(className) == enabled) return;
            if (enabled) element.ClassList.Add(className);
            else element.ClassList.Remove(className);
            Invalidate(element);
        }

        public void SetVariable(string selector, string attribute, string value)
        {
            var target = Document?.QuerySelector(selector);
            if (target == null) return;
            target.SetAttribute(attribute, value);
            Invalidate(target);
        }

        /// <summary>
        /// Replaces the text content of the first element matching <paramref name="selector"/>.
        /// Use for live-updating values like fps numbers each frame.
        /// </summary>
        public void SetText(string selector, string text)
        {
            var target = Document?.QuerySelector(selector);
            if (target != null && target.TextContent != text) target.TextContent = text;
        }

        /// <summary>
        /// Topmost visible element under a viewport-pixel position that accepts pointer events
        /// (<c>pointer-events: none</c> lets clicks through). Use <c>DomNode.Closest(".button")</c>
        /// to find the interactive ancestor.
        /// </summary>
        public UIElement ElementAt(Point viewportPosition)
        {
            if (RootView == null) return null;
            return RootView.HitTest(new Vector2(viewportPosition.X / Scale, viewportPosition.Y / Scale));
        }

        public void Update(GameTime gameTime)
        {
            UIElement root = RootView;
            if (root == null) return;

            Rectangle viewport = _graphicsDevice.Viewport.Bounds;
            Scale = ReferenceHeight > 0 ? Math.Max(0.01f, viewport.Height / ReferenceHeight) : 1f;
            float dt = gameTime == null ? 0f : (float)gameTime.ElapsedGameTime.TotalSeconds;
            root.CalculateLayout(new Vector4(0, 0, viewport.Width / Scale, viewport.Height / Scale), 1f, dt);
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            UIElement root = RootView;
            if (root == null) return;
            root.Draw(spriteBatch, _blankTexture, Fonts, _gradientLookup);
        }

        /// <summary>Bakes a gradient into a small texture that SpriteBatch stretches over the element.</summary>
        private Texture2D GetGradientTexture(CssGradient gradient, string key)
        {
            if (_gradients.TryGetValue(key, out var cached)) return cached;

            Texture2D texture;
            if (gradient.Kind == GradientKind.Radial)
            {
                const int size = 128;
                var data = new Color[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        // Ellipse reaching the corners (CSS's default farthest-corner).
                        float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                        data[y * size + x] = UIStyles.Premultiply(gradient.Sample(MathF.Sqrt(u * u + v * v) / MathF.Sqrt(2f)), 1f);
                    }
                texture = new Texture2D(_graphicsDevice, size, size);
                texture.SetData(data);
            }
            else
            {
                const int steps = 256;
                bool vertical = gradient.Angle == 0 || gradient.Angle == 180;
                bool reverse = gradient.Angle == 0 || gradient.Angle == 270;
                var data = new Color[steps];
                for (int i = 0; i < steps; i++)
                {
                    float t = (i + 0.5f) / steps;
                    data[i] = UIStyles.Premultiply(gradient.Sample(reverse ? 1f - t : t), 1f);
                }
                texture = vertical ? new Texture2D(_graphicsDevice, 1, steps) : new Texture2D(_graphicsDevice, steps, 1);
                texture.SetData(data);
            }
            _gradients[key] = texture;
            return texture;
        }

        public void Dispose()
        {
            foreach (var texture in _gradients.Values) texture.Dispose();
            _gradients.Clear();
            _blankTexture?.Dispose();
            _blankTexture = null;
            _context?.Dispose();
            _context = null;
        }
    }
}
