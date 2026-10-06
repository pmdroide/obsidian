using System;
using System.Collections.Generic;
using System.Text;
using AngleSharp.Css;
using AngleSharp.Css.Dom;
using AngleSharp.Dom;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Vista
{
    /// <summary>
    /// Visual mirror of one DOM element. Layout is absolute: <c>left/top/right/bottom/width/height</c>
    /// (px or % of the parent box) plus margins, relative to the parent's box. Supports backgrounds,
    /// gradients, borders, opacity, text (alignment, wrapping, letter spacing, shadow) and transitions.
    /// </summary>
    public class UIElement
    {
        public IElement DomNode { get; private set; }
        public UIElement Parent { get; internal set; }
        public List<UIElement> Children { get; private set; } = new List<UIElement>();

        /// <summary>Layout box in UI units (screen pixels, or reference pixels when the manager scales).</summary>
        public Rectangle ScreenBounds { get; private set; }
        public Vector2 Position => new Vector2(_x, _y);
        public Vector2 Size => new Vector2(_w, _h);

        /// <summary>Script-driven multiplier on top of the CSS opacity (pulses, fades). 1 = unchanged.</summary>
        public float RuntimeOpacity { get; set; } = 1f;
        /// <summary>Script-driven offset added after layout, in UI units. Moves the children too.</summary>
        public Vector2 RuntimeOffset { get; set; }

        /// <summary>Opacity after transitions, <see cref="RuntimeOpacity"/> and every ancestor's opacity.</summary>
        public float EffectiveOpacity { get; private set; } = 1f;
        public bool IsVisible => Style != null && !Style.DisplayNone && !Style.Hidden && EffectiveOpacity > 0.003f;

        public UIComputedStyle Style { get; private set; }
        internal bool StyleDirty = true;

        // Animated values. Unsettled elements snap to their targets on the next layout.
        private bool _settled;
        private float _x, _y, _w, _h, _opacity;
        private Vector4 _background, _color;
        private readonly Vector4[] _border = new Vector4[4];

        // Wrapped text cache.
        private string _wrapText;
        private float _wrapWidth = -1;
        private SpriteFont _wrapFont;
        private float _wrapSpacing;
        private readonly List<string> _lines = new List<string>();

        private static readonly StringBuilder CharBuffer = new StringBuilder(1);

        public UIElement(IElement domNode, UIElement parent = null)
        {
            DomNode = domNode;
            Parent = parent;
        }

        internal void ComputeStyle()
        {
            try
            {
                var cascaded = DomNode.Owner?.DefaultView?.GetStyleCollection().ComputeCascadedStyle(DomNode);
                Style = UIComputedStyle.From(DomNode.ComputeCurrentStyle(), cascaded);
            }
            catch (Exception ex)
            {
                // A value AngleSharp can't compute must not take the game down: hide this element
                // (and its subtree) until it is restyled.
                System.Diagnostics.Debug.WriteLine($"[Vista UI] style of <{DomNode.LocalName} id='{DomNode.Id}'> failed: {ex.Message}");
                Style = new UIComputedStyle { Hidden = true };
            }
            StyleDirty = false;
            if (Style.DisplayNone) _settled = false;
        }

        internal void MarkSubtreeDirty()
        {
            StyleDirty = true;
            foreach (var child in Children) child.MarkSubtreeDirty();
        }

        /// <summary>Lays out this element inside <paramref name="parentBounds"/> (no animation).</summary>
        public void CalculateLayout(Rectangle parentBounds) =>
            CalculateLayout(new Vector4(parentBounds.X, parentBounds.Y, parentBounds.Width, parentBounds.Height), 1f, 0f);

        internal void CalculateLayout(Vector4 parent, float parentOpacity, float dt)
        {
            if (Style == null || StyleDirty) ComputeStyle();
            UIComputedStyle s = Style;
            if (s.DisplayNone)
            {
                EffectiveOpacity = 0f;
                return;
            }

            float pw = parent.Z, ph = parent.W;
            float ml = s.MarginLeft.Resolve(pw, 0), mr = s.MarginRight.Resolve(pw, 0);
            float mt = s.MarginTop.Resolve(pw, 0), mb = s.MarginBottom.Resolve(pw, 0);
            float w = !s.Width.IsAuto ? s.Width.Resolve(pw, pw)
                    : !s.Left.IsAuto && !s.Right.IsAuto ? pw - s.Left.Resolve(pw, 0) - s.Right.Resolve(pw, 0) - ml - mr
                    : pw;
            float h = !s.Height.IsAuto ? s.Height.Resolve(ph, ph)
                    : !s.Top.IsAuto && !s.Bottom.IsAuto ? ph - s.Top.Resolve(ph, 0) - s.Bottom.Resolve(ph, 0) - mt - mb
                    : ph;

            float x = !s.Left.IsAuto ? s.Left.Resolve(pw, 0) + ml
                    : !s.Right.IsAuto ? pw - w - s.Right.Resolve(pw, 0) - mr
                    : ml;
            float y = !s.Top.IsAuto ? s.Top.Resolve(ph, 0) + mt
                    : !s.Bottom.IsAuto ? ph - h - s.Bottom.Resolve(ph, 0) - mb
                    : mt;

            if (!_settled)
            {
                _x = x; _y = y; _w = w; _h = h;
                _opacity = s.Opacity;
                _background = s.Background;
                _color = s.Color;
                Array.Copy(s.BorderColor, _border, 4);
                _settled = true;
            }
            else
            {
                float horizontal = Math.Max(s.TransitionFor("left"), s.TransitionFor("right"));
                float vertical = Math.Max(s.TransitionFor("top"), s.TransitionFor("bottom"));
                _x = Approach(_x, x, horizontal, dt);
                _y = Approach(_y, y, vertical, dt);
                _w = Approach(_w, w, s.TransitionFor("width"), dt);
                _h = Approach(_h, h, s.TransitionFor("height"), dt);
                _opacity = Approach(_opacity, s.Opacity, s.TransitionFor("opacity"), dt);
                _background = Approach(_background, s.Background, s.TransitionFor("background-color"), dt);
                _color = Approach(_color, s.Color, s.TransitionFor("color"), dt);
                _border[0] = Approach(_border[0], s.BorderColor[0], s.TransitionFor("border-top-color"), dt);
                _border[1] = Approach(_border[1], s.BorderColor[1], s.TransitionFor("border-right-color"), dt);
                _border[2] = Approach(_border[2], s.BorderColor[2], s.TransitionFor("border-bottom-color"), dt);
                _border[3] = Approach(_border[3], s.BorderColor[3], s.TransitionFor("border-left-color"), dt);
            }

            float absX = parent.X + _x + RuntimeOffset.X;
            float absY = parent.Y + _y + RuntimeOffset.Y;
            EffectiveOpacity = parentOpacity * _opacity * MathHelper.Clamp(RuntimeOpacity, 0f, 1f);
            if (s.Hidden) EffectiveOpacity = 0f;

            ScreenBounds = new Rectangle((int)MathF.Round(absX), (int)MathF.Round(absY), (int)MathF.Round(_w), (int)MathF.Round(_h));
            var self = new Vector4(absX, absY, _w, _h);
            _absX = absX; _absY = absY;

            foreach (var child in Children)
                child.CalculateLayout(self, EffectiveOpacity, dt);
        }

        private float _absX, _absY;

        /// <summary>Eases towards <paramref name="target"/>, covering ~99% of the gap in <paramref name="duration"/> seconds.</summary>
        private static float Approach(float current, float target, float duration, float dt)
        {
            if (duration <= 0f) return target;
            if (dt <= 0f) return current;
            float v = current + (target - current) * (1f - MathF.Exp(-dt * 5f / duration));
            return MathF.Abs(target - v) < 0.01f ? target : v;
        }

        private static Vector4 Approach(Vector4 current, Vector4 target, float duration, float dt)
        {
            if (duration <= 0f) return target;
            if (dt <= 0f) return current;
            Vector4 v = Vector4.Lerp(current, target, 1f - MathF.Exp(-dt * 5f / duration));
            return Vector4.DistanceSquared(v, target) < 1e-6f ? target : v;
        }

        ////////////////////////////////////////////////////////////////////////////////
        //  DRAW
        ////////////////////////////////////////////////////////////////////////////////

        public virtual void Draw(SpriteBatch spriteBatch, Texture2D whitePixel, UIFontRegistry fonts) =>
            Draw(spriteBatch, whitePixel, fonts, null);

        internal void Draw(SpriteBatch spriteBatch, Texture2D whitePixel, UIFontRegistry fonts, Func<CssGradient, string, Texture2D> gradients)
        {
            if (!IsVisible) return;
            UIComputedStyle s = Style;
            float alpha = EffectiveOpacity;
            var position = new Vector2(_absX, _absY);
            var size = new Vector2(_w, _h);

            if (_background.W > 0f)
                spriteBatch.Draw(whitePixel, position, null, UIStyles.Premultiply(_background, alpha), 0f, Vector2.Zero, size, SpriteEffects.None, 0f);

            if (s.BackgroundGradient != null && gradients != null)
            {
                Texture2D texture = gradients(s.BackgroundGradient, s.BackgroundGradientKey);
                if (texture != null)
                {
                    var scale = new Vector2(_w / texture.Width, _h / texture.Height);
                    spriteBatch.Draw(texture, position, null, Color.White * alpha, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                }
            }

            DrawBorders(spriteBatch, whitePixel, s, alpha);
            DrawTextContent(spriteBatch, fonts, s, alpha);

            foreach (var child in Children)
                child.Draw(spriteBatch, whitePixel, fonts, gradients);
        }

        private void DrawBorders(SpriteBatch spriteBatch, Texture2D pixel, UIComputedStyle s, float alpha)
        {
            float top = s.BorderWidth[0], right = s.BorderWidth[1], bottom = s.BorderWidth[2], left = s.BorderWidth[3];
            if (top > 0) Fill(spriteBatch, pixel, _absX, _absY, _w, top, _border[0], alpha);
            if (bottom > 0) Fill(spriteBatch, pixel, _absX, _absY + _h - bottom, _w, bottom, _border[2], alpha);
            if (left > 0) Fill(spriteBatch, pixel, _absX, _absY + top, left, _h - top - bottom, _border[3], alpha);
            if (right > 0) Fill(spriteBatch, pixel, _absX + _w - right, _absY + top, right, _h - top - bottom, _border[1], alpha);
        }

        private static void Fill(SpriteBatch spriteBatch, Texture2D pixel, float x, float y, float w, float h, Vector4 color, float alpha)
        {
            if (w <= 0 || h <= 0 || color.W <= 0) return;
            spriteBatch.Draw(pixel, new Vector2(x, y), null, UIStyles.Premultiply(color, alpha), 0f, Vector2.Zero, new Vector2(w, h), SpriteEffects.None, 0f);
        }

        private void DrawTextContent(SpriteBatch spriteBatch, UIFontRegistry fonts, UIComputedStyle s, float alpha)
        {
            if (fonts == null) return;

            // Render direct text content only (children's text is rendered by their own pass).
            string text = GetOwnText(s.PreserveNewlines);
            if (string.IsNullOrWhiteSpace(text)) return;
            if (s.Uppercase) text = text.ToUpperInvariant();
            else if (s.Lowercase) text = text.ToLowerInvariant();

            SpriteFont font = fonts.Resolve(s.FontFamily);
            if (font == null) return;
            text = fonts.MakeDrawable(font, text);

            float left = _absX + s.BorderWidth[3] + s.PaddingLeft;
            float top = _absY + s.BorderWidth[0] + s.PaddingTop;
            float contentW = _w - s.BorderWidth[1] - s.BorderWidth[3] - s.PaddingLeft - s.PaddingRight;
            float contentH = _h - s.BorderWidth[0] - s.BorderWidth[2] - s.PaddingTop - s.PaddingBottom;

            List<string> lines = Wrap(fonts, font, text, s.NoWrap ? float.MaxValue : contentW, s.LetterSpacing);
            float lineHeight = s.LineHeight > 0 ? s.LineHeight : font.LineSpacing * s.LineHeightFactor;
            float glyphOffset = (lineHeight - font.LineSpacing) * 0.5f;
            float blockH = lines.Count * lineHeight;
            float y = s.VerticalAlign switch
            {
                // Centre the capitals, not the line box (which includes descenders and leading).
                VerticalAlign.Middle => top + (contentH - blockH) * 0.5f + font.LineSpacing * 0.5f - fonts.CapMiddle(font),
                VerticalAlign.Bottom => top + contentH - blockH,
                _ => top,
            };

            Color color = UIStyles.Premultiply(_color, alpha);
            Color shadow = UIStyles.Premultiply(s.ShadowColor, alpha);
            foreach (string line in lines)
            {
                float lineW = Measure(fonts, font, line, s.LetterSpacing);
                float x = s.TextAlign switch
                {
                    TextAlign.Center => left + (contentW - lineW) * 0.5f,
                    TextAlign.Right => left + contentW - lineW,
                    _ => left,
                };
                // Whole pixels keep glyphs crisp when the UI isn't scaled.
                var pen = new Vector2(MathF.Round(x), MathF.Round(y + glyphOffset));
                if (s.HasShadow)
                {
                    if (s.ShadowBlur > 0.5f)
                    {
                        // Cheap soft shadow: the offset copy plus four faint copies around it.
                        float r = s.ShadowBlur * 0.5f;
                        Color soft = shadow * 0.35f;
                        DrawLine(spriteBatch, fonts, font, line, pen + s.ShadowOffset + new Vector2(r, 0), soft, s.LetterSpacing);
                        DrawLine(spriteBatch, fonts, font, line, pen + s.ShadowOffset + new Vector2(-r, 0), soft, s.LetterSpacing);
                        DrawLine(spriteBatch, fonts, font, line, pen + s.ShadowOffset + new Vector2(0, r), soft, s.LetterSpacing);
                        DrawLine(spriteBatch, fonts, font, line, pen + s.ShadowOffset + new Vector2(0, -r), soft, s.LetterSpacing);
                    }
                    DrawLine(spriteBatch, fonts, font, line, pen + s.ShadowOffset, shadow * 0.6f, s.LetterSpacing);
                }
                DrawLine(spriteBatch, fonts, font, line, pen, color, s.LetterSpacing);
                y += lineHeight;
            }
        }

        private static void DrawLine(SpriteBatch spriteBatch, UIFontRegistry fonts, SpriteFont font, string line, Vector2 pen, Color color, float spacing)
        {
            if (spacing == 0f)
            {
                spriteBatch.DrawString(font, line, pen, color);
                return;
            }
            foreach (char ch in line)
            {
                if (ch != ' ')
                {
                    CharBuffer.Clear().Append(ch);
                    spriteBatch.DrawString(font, CharBuffer, pen, color);
                }
                pen.X += fonts.Advance(font, ch) + spacing;
            }
        }

        private static float Measure(UIFontRegistry fonts, SpriteFont font, string line, float spacing)
        {
            if (line.Length == 0) return 0f;
            if (spacing == 0f) return font.MeasureString(line).X;
            float w = 0f;
            foreach (char ch in line) w += fonts.Advance(font, ch);
            return w + spacing * (line.Length - 1);
        }

        private List<string> Wrap(UIFontRegistry fonts, SpriteFont font, string text, float width, float spacing)
        {
            if (text == _wrapText && width == _wrapWidth && font == _wrapFont && spacing == _wrapSpacing) return _lines;
            _wrapText = text; _wrapWidth = width; _wrapFont = font; _wrapSpacing = spacing;
            _lines.Clear();

            foreach (string paragraph in text.Split('\n'))
            {
                string[] words = paragraph.Split(' ');
                var current = new StringBuilder();
                foreach (string word in words)
                {
                    if (word.Length == 0) continue;
                    string candidate = current.Length == 0 ? word : current + " " + word;
                    if (current.Length > 0 && Measure(fonts, font, candidate, spacing) > width)
                    {
                        _lines.Add(current.ToString());
                        current.Clear().Append(word);
                    }
                    else
                    {
                        current.Clear().Append(candidate);
                    }
                }
                _lines.Add(current.ToString());
            }
            return _lines;
        }

        private string GetOwnText(bool preserveNewlines)
        {
            // AngleSharp's TextContent walks descendants; we only want immediate text nodes here.
            var sb = new StringBuilder();
            foreach (var node in DomNode.ChildNodes)
            {
                if (node.NodeType == NodeType.Text)
                    sb.Append(node.TextContent);
            }
            // CSS white-space: collapse runs of whitespace (keeping newlines for pre-line).
            var collapsed = new StringBuilder(sb.Length);
            bool space = false;
            foreach (char ch in sb.ToString())
            {
                if (ch == '\n' && preserveNewlines)
                {
                    while (collapsed.Length > 0 && collapsed[collapsed.Length - 1] == ' ') collapsed.Length--;
                    collapsed.Append('\n');
                    space = true;
                }
                else if (char.IsWhiteSpace(ch)) space = collapsed.Length > 0 && collapsed[collapsed.Length - 1] != '\n';
                else
                {
                    if (space) collapsed.Append(' ');
                    space = false;
                    collapsed.Append(ch);
                }
            }
            return collapsed.ToString().Trim('\n');
        }

        ////////////////////////////////////////////////////////////////////////////////
        //  HIT TESTING
        ////////////////////////////////////////////////////////////////////////////////

        /// <summary>Deepest visible element under <paramref name="point"/> (UI units) that accepts pointer events.</summary>
        public UIElement HitTest(Vector2 point)
        {
            if (Style == null || Style.DisplayNone || Style.Hidden || EffectiveOpacity < 0.05f) return null;
            for (int i = Children.Count - 1; i >= 0; i--)
            {
                UIElement hit = Children[i].HitTest(point);
                if (hit != null) return hit;
            }
            bool inside = point.X >= _absX && point.Y >= _absY && point.X < _absX + _w && point.Y < _absY + _h;
            return inside && Style.PointerEvents ? this : null;
        }
    }
}
