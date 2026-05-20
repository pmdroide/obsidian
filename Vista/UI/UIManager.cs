using System;
using System.IO;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Css;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Vista
{
    public class UIManager
    {
        private IBrowsingContext _context;
        public IDocument Document { get; private set; }
        public UIElement RootView { get; private set; }
        public UIFontRegistry Fonts { get; } = new UIFontRegistry();

        private Texture2D _blankTexture;
        private GraphicsDevice _graphicsDevice;

        public UIManager(GraphicsDevice device)
        {
            _graphicsDevice = device;

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

                    string xmlContent = await File.ReadAllTextAsync(xmlPath).ConfigureAwait(false);
                    string cssContent = await File.ReadAllTextAsync(cssPath).ConfigureAwait(false);

                    // ConfigureAwait(false) prevents deadlocking on the game's main synchronization loop
                    Document = await _context.OpenAsync(req => req.Content($"<style>{cssContent}</style>{xmlContent}"))
                                             .ConfigureAwait(false);

                    if (Document.Body != null)
                    {
                        RootView = BuildTree(Document.Body);
                        System.Diagnostics.Debug.WriteLine("[Vista UI] UI Layout Loaded Successfully.");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Vista UI Critical Crash]: {ex}");
                }
            });
        }

        public async Task LoadUIAsync(string xmlPath, string cssPath)
        {
            string xmlContent = await File.ReadAllTextAsync(xmlPath);
            string cssContent = await File.ReadAllTextAsync(cssPath);

            Document = await _context.OpenAsync(req => req.Content($"<style>{cssContent}</style>{xmlContent}"));

            if (Document.Body != null)
            {
                RootView = BuildTree(Document.Body);
            }
        }

        private UIElement BuildTree(IElement element, UIElement parent = null)
        {
            var node = new UIElement(element, parent);

            foreach (var childElement in element.Children)
            {
                var childNode = BuildTree(childElement, node);
                node.Children.Add(childNode);
            }

            return node;
        }

        public void SetVariable(string selector, string attribute, string value)
        {
            var target = Document?.QuerySelector(selector);
            target?.SetAttribute(attribute, value);
        }

        /// <summary>
        /// Replaces the text content of the first element matching <paramref name="selector"/>.
        /// Use for live-updating values like fps numbers each frame.
        /// </summary>
        public void SetText(string selector, string text)
        {
            var target = Document?.QuerySelector(selector);
            if (target != null) target.TextContent = text;
        }

        public void Update(GameTime gameTime)
        {
            if (RootView == null) return;

            Rectangle screenSpace = _graphicsDevice.Viewport.Bounds;
            RootView.CalculateLayout(screenSpace);
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (RootView == null) return;
            RootView.Draw(spriteBatch, _blankTexture, Fonts);
        }
    }
}
