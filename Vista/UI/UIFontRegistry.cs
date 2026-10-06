using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework.Graphics;

namespace Vista
{
    /// <summary>
    /// Maps CSS font-family identifiers to loaded MonoGame SpriteFonts.
    /// The host (engine) registers fonts after content load; UIElements look them up at draw time.
    /// One registry can be shared by several <see cref="UIManager"/>s.
    /// </summary>
    public class UIFontRegistry
    {
        private readonly Dictionary<string, SpriteFont> _fonts = new Dictionary<string, SpriteFont>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<SpriteFont, Dictionary<char, SpriteFont.Glyph>> _glyphs = new Dictionary<SpriteFont, Dictionary<char, SpriteFont.Glyph>>();
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

        internal Dictionary<char, SpriteFont.Glyph> Glyphs(SpriteFont font)
        {
            if (!_glyphs.TryGetValue(font, out var glyphs))
                _glyphs[font] = glyphs = font.GetGlyphs();
            return glyphs;
        }

        /// <summary>
        /// Replaces characters the font can't draw (SpriteFont throws on them) with '?', or drops
        /// them when the font has no '?' either. Returns the input when nothing needed replacing.
        /// </summary>
        public string MakeDrawable(SpriteFont font, string text)
        {
            if (font.DefaultCharacter.HasValue || string.IsNullOrEmpty(text)) return text;
            var glyphs = Glyphs(font);
            StringBuilder sb = null;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                bool ok = ch == '\n' || ch == '\r' || glyphs.ContainsKey(ch);
                if (ok && sb == null) continue;
                if (sb == null) sb = new StringBuilder(text, 0, i, text.Length);
                if (ok) sb.Append(ch);
                else if (glyphs.ContainsKey('?')) sb.Append('?');
            }
            return sb?.ToString() ?? text;
        }

        private readonly Dictionary<SpriteFont, float> _capMiddles = new Dictionary<SpriteFont, float>();

        /// <summary>
        /// Distance from the top of a line to the middle of a capital letter. Centring on this
        /// (rather than half the line spacing, which includes descenders and leading) looks centred.
        /// </summary>
        internal float CapMiddle(SpriteFont font)
        {
            if (_capMiddles.TryGetValue(font, out float middle)) return middle;
            middle = Glyphs(font).TryGetValue('H', out var h)
                ? h.Cropping.Y + h.BoundsInTexture.Height * 0.5f
                : font.LineSpacing * 0.5f;
            _capMiddles[font] = middle;
            return middle;
        }

        /// <summary>Horizontal advance of one character, including the font's own spacing.</summary>
        internal float Advance(SpriteFont font, char ch)
        {
            if (!Glyphs(font).TryGetValue(ch, out var glyph))
            {
                if (!font.DefaultCharacter.HasValue || !Glyphs(font).TryGetValue(font.DefaultCharacter.Value, out glyph))
                    return 0f;
            }
            return glyph.LeftSideBearing + glyph.Width + glyph.RightSideBearing + font.Spacing;
        }
    }
}
