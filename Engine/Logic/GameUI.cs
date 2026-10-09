using System;
using System.Collections.Generic;
using System.IO;
using Engine.Editor;
using Engine.Recources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Vista;

namespace Engine.Logic
{
    /// <summary>
    /// Vista UI layers opened by scripts (menus, HUDs). Layers draw over the scene, under the debug
    /// overlay, in the order they were opened. A script opens its layer in Start and closes it in
    /// Stop; anything left open is closed when Play stops or the scene changes, except layers opened by
    /// scripts on persistent gameobjects, which stay open while those carry on into the next scene.
    ///
    /// Documents are loose files: <c>Content/&lt;path&gt;.xml</c> + <c>.css</c>. In a dev checkout the
    /// source copies are read and watched, so saving the XML/CSS reloads the layer in place
    /// (<see cref="UIManager.Loaded"/> fires so the script can re-apply its state).
    /// </summary>
    public static class GameUI
    {
        private sealed class Layer
        {
            public UIManager UI;
            // The gameobject whose script opened it (null for the main camera or outside a script).
            public Entities.BasicEntity Owner;
            public string Xml, Css;
            public DateTime XmlTime, CssTime;
        }

        private static GraphicsDevice _device;
        private static UIFontRegistry _fonts;
        private static readonly List<Layer> Layers = new List<Layer>();
        private static double _watchTimer;

        /// <summary>Fonts available to game UI ("display", "heading", "body", "caption", "default", "monospace").</summary>
        public static UIFontRegistry Fonts => _fonts;

        public static int Count => Layers.Count;

        internal static void Bind(GraphicsDevice device, UIFontRegistry fonts)
        {
            _device = device;
            _fonts = fonts;
        }

        /// <summary>
        /// Opens <c>Content/&lt;path&gt;.xml</c> with <c>Content/&lt;path&gt;.css</c> ("UI/MainMenu").
        /// <paramref name="referenceHeight"/> is the design height: the layer scales with the window
        /// (0 = one UI unit per pixel). Returns null (and logs) when the files can't be loaded.
        /// </summary>
        public static UIManager Open(string path, float referenceHeight = 1080f)
        {
            if (_device == null)
            {
                EditorBridge.Log($"GameUI.Open('{path}'): no graphics device yet");
                return null;
            }
            var layer = new Layer
            {
                Owner = Scripting.ScriptBehaviour.Running?.GameObject,
                Xml = GameInfo.ResolveContentFile(path + ".xml"),
                Css = GameInfo.ResolveContentFile(path + ".css"),
            };
            var ui = new UIManager(_device, _fonts) { ReferenceHeight = referenceHeight };
            try
            {
                ui.Load(layer.Xml, layer.Css);
            }
            catch (Exception ex)
            {
                EditorBridge.Log($"GameUI.Open('{path}') failed: {ex.Message}");
                ui.Dispose();
                return null;
            }
            layer.UI = ui;
            layer.XmlTime = File.GetLastWriteTimeUtc(layer.Xml);
            layer.CssTime = File.GetLastWriteTimeUtc(layer.Css);
            Layers.Add(layer);
            return ui;
        }

        public static void Close(UIManager ui)
        {
            if (ui == null) return;
            int index = Layers.FindIndex(l => l.UI == ui);
            if (index >= 0) Layers.RemoveAt(index);
            ui.Dispose();
        }

        public static void CloseAll() => CloseAll(null);

        /// <summary>Closes every layer except those opened by scripts on <paramref name="keepOwnedBy"/> (carried gameobjects).</summary>
        internal static void CloseAll(IReadOnlyCollection<Entities.BasicEntity> keepOwnedBy)
        {
            for (int i = Layers.Count - 1; i >= 0; i--)
            {
                Layer layer = Layers[i];
                if (layer.Owner != null && keepOwnedBy?.Contains(layer.Owner) == true) continue;
                layer.UI.Dispose();
                Layers.RemoveAt(i);
            }
        }

        /// <summary>Topmost layer element under a viewport position, or null (for "is the cursor over UI").</summary>
        public static UIElement ElementAt(Point viewportPosition)
        {
            for (int i = Layers.Count - 1; i >= 0; i--)
            {
                UIElement hit = Layers[i].UI.ElementAt(viewportPosition);
                if (hit != null && hit != Layers[i].UI.RootView) return hit;
            }
            return null;
        }

        internal static void Update(GameTime gameTime)
        {
            WatchFiles(gameTime);
            foreach (var layer in Layers.ToArray()) layer.UI.Update(gameTime);
        }

        internal static void Draw(SpriteBatch spriteBatch)
        {
            foreach (var layer in Layers)
            {
                spriteBatch.Begin(transformMatrix: layer.UI.Transform);
                layer.UI.Draw(spriteBatch);
                spriteBatch.End();
            }
        }

        private static void WatchFiles(GameTime gameTime)
        {
            if (Layers.Count == 0) return;
            _watchTimer -= gameTime.ElapsedGameTime.TotalSeconds;
            if (_watchTimer > 0) return;
            _watchTimer = 0.5;

            foreach (var layer in Layers.ToArray())
            {
                DateTime xml, css;
                try
                {
                    xml = File.GetLastWriteTimeUtc(layer.Xml);
                    css = File.GetLastWriteTimeUtc(layer.Css);
                }
                catch { continue; }
                if (xml == layer.XmlTime && css == layer.CssTime) continue;
                layer.XmlTime = xml;
                layer.CssTime = css;
                try
                {
                    layer.UI.Load(layer.Xml, layer.Css);
                    EditorBridge.Log($"GameUI: reloaded '{Path.GetFileName(layer.Xml)}'");
                }
                catch (Exception ex)
                {
                    // Usually caught mid-save; the next change retries.
                    EditorBridge.Log($"GameUI: reload of '{Path.GetFileName(layer.Xml)}' failed: {ex.Message}");
                }
            }
        }
    }
}
