using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;

namespace Vista
{
    /// <summary>
    /// Maps CSS font-family identifiers to loaded MonoGame SpriteFonts.
    /// The host (engine) registers fonts after content load; UIElements look them up at draw time.
    /// </summary>
    public class UIFontRegistry
    {
        private readonly Dictionary<string, SpriteFont> _fonts = new Dictionary<string, SpriteFont>(StringComparer.OrdinalIgnoreCase);
        private SpriteFont _default;

        public void Register(string fontFamily, SpriteFont font, bool isDefault = false)
        {
            if (string.IsNullOrEmpty(fontFamily) || font == null) return;
            _fonts[fontFamily] = font;
            if (isDefault || _default == null) _default = font;
        }

        public SpriteFont Resolve(string cssFontFamily)
        {
            if (string.IsNullOrEmpty(cssFontFamily)) return _default;

            // CSS allows comma-separated fallbacks: "monospace, Arial"
            foreach (var raw in cssFontFamily.Split(','))
            {
                string key = raw.Trim().Trim('"', '\'');
                if (_fonts.TryGetValue(key, out var font)) return font;
            }
            return _default;
        }
    }
}
