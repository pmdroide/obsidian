using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xna.Framework;

namespace Vista
{
    public enum LengthUnit { Auto, Pixels, Percent }

    /// <summary>A CSS length as computed by AngleSharp: auto, pixels or a percentage of the parent.</summary>
    public readonly struct CssLength
    {
        public readonly float Value;
        public readonly LengthUnit Unit;

        public CssLength(float value, LengthUnit unit) { Value = value; Unit = unit; }

        public static readonly CssLength Auto = new CssLength(0, LengthUnit.Auto);
        public bool IsAuto => Unit == LengthUnit.Auto;

        public float Resolve(float basis, float fallback) => Unit switch
        {
            LengthUnit.Pixels => Value,
            LengthUnit.Percent => basis * Value / 100f,
            _ => fallback,
        };
    }

    public enum GradientKind { Linear, Radial }

    /// <summary>
    /// A parsed <c>linear-gradient</c>/<c>radial-gradient</c>. Linear angles snap to the nearest axis
    /// (to top/right/bottom/left); colour stops are spaced evenly (stop positions are ignored).
    /// </summary>
    public sealed class CssGradient
    {
        public GradientKind Kind;
        /// <summary>Linear only: 0 = to top, 90 = to right, 180 = to bottom, 270 = to left.</summary>
        public int Angle = 180;
        /// <summary>Straight (non-premultiplied) RGBA stops, 0..1.</summary>
        public readonly List<Vector4> Stops = new List<Vector4>();

        public Vector4 Sample(float t)
        {
            if (Stops.Count == 1) return Stops[0];
            t = MathHelper.Clamp(t, 0f, 1f) * (Stops.Count - 1);
            int i = Math.Min((int)t, Stops.Count - 2);
            return Vector4.Lerp(Stops[i], Stops[i + 1], t - i);
        }
    }

    public static class UIStyles
    {
        public static int GetPixelValue(string cssValue, int maxValue, int defaultValue)
        {
            CssLength length = ParseLength(cssValue);
            return (int)length.Resolve(maxValue, defaultValue);
        }

        /// <summary>Parses px, %, em/rem (16px), unitless numbers, <c>auto</c> and empty values.</summary>
        public static CssLength ParseLength(string cssValue)
        {
            if (string.IsNullOrWhiteSpace(cssValue)) return CssLength.Auto;
            string v = cssValue.Trim().ToLowerInvariant();
            if (v == "auto" || v == "initial" || v == "inherit" || v == "normal" || v == "none") return CssLength.Auto;

            if (v.EndsWith("%") && TryFloat(v.Substring(0, v.Length - 1), out float percent))
                return new CssLength(percent, LengthUnit.Percent);
            if (v.EndsWith("px") && TryFloat(v.Substring(0, v.Length - 2), out float px))
                return new CssLength(px, LengthUnit.Pixels);
            if (v.EndsWith("rem") && TryFloat(v.Substring(0, v.Length - 3), out float rem))
                return new CssLength(rem * 16f, LengthUnit.Pixels);
            if (v.EndsWith("em") && TryFloat(v.Substring(0, v.Length - 2), out float em))
                return new CssLength(em * 16f, LengthUnit.Pixels);
            if (TryFloat(v, out float raw))
                return new CssLength(raw, LengthUnit.Pixels);
            return CssLength.Auto;
        }

        /// <summary>Pixel value of a length, or <paramref name="fallback"/> when it is auto/unparseable.</summary>
        public static float ParsePixels(string cssValue, float fallback = 0f)
        {
            CssLength length = ParseLength(cssValue);
            return length.Unit == LengthUnit.Pixels ? length.Value : fallback;
        }

        /// <summary>Comma-separated CSS times ("350ms, 0.2s") in seconds.</summary>
        public static List<float> ParseTimes(string cssValue)
        {
            var result = new List<float>();
            if (string.IsNullOrWhiteSpace(cssValue)) return result;
            foreach (string raw in cssValue.Split(','))
            {
                string v = raw.Trim().ToLowerInvariant();
                if (v.EndsWith("ms") && TryFloat(v.Substring(0, v.Length - 2), out float ms)) result.Add(ms / 1000f);
                else if (v.EndsWith("s") && TryFloat(v.Substring(0, v.Length - 1), out float s)) result.Add(s);
                else if (TryFloat(v, out float n)) result.Add(n);
                else result.Add(0f);
            }
            return result;
        }

        public static Color ParseColor(string cssColor) => new Color(ParseColorVector(cssColor));

        /// <summary>Straight-alpha RGBA in 0..1. Unknown values are transparent.</summary>
        public static Vector4 ParseColorVector(string cssColor)
        {
            if (string.IsNullOrWhiteSpace(cssColor)) return Vector4.Zero;
            string c = cssColor.Trim().ToLowerInvariant();

            if (c.StartsWith("#"))
            {
                string hex = c.Substring(1);
                if (hex.Length == 3 || hex.Length == 4)
                {
                    var expanded = new char[hex.Length * 2];
                    for (int i = 0; i < hex.Length; i++) expanded[i * 2] = expanded[i * 2 + 1] = hex[i];
                    hex = new string(expanded);
                }
                if (hex.Length == 6) hex += "ff";
                if (hex.Length != 8 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgba))
                    return Vector4.Zero;
                return new Vector4(((rgba >> 24) & 0xFF) / 255f, ((rgba >> 16) & 0xFF) / 255f,
                                   ((rgba >> 8) & 0xFF) / 255f, (rgba & 0xFF) / 255f);
            }

            if (c.StartsWith("rgb"))
            {
                int open = c.IndexOf('('), close = c.LastIndexOf(')');
                if (open < 0 || close <= open) return Vector4.Zero;
                string[] parts = c.Substring(open + 1, close - open - 1).Replace('/', ',').Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3) return Vector4.Zero;
                float Channel(string p) => p.EndsWith("%") && TryFloat(p.TrimEnd('%'), out float pc) ? pc / 100f
                                         : TryFloat(p, out float v) ? v / 255f : 0f;
                float alpha = 1f;
                if (parts.Length > 3)
                    alpha = parts[3].EndsWith("%") && TryFloat(parts[3].TrimEnd('%'), out float ap) ? ap / 100f
                          : TryFloat(parts[3], out float a) ? a : 1f;
                return Vector4.Clamp(new Vector4(Channel(parts[0]), Channel(parts[1]), Channel(parts[2]), alpha), Vector4.Zero, Vector4.One);
            }

            switch (c)
            {
                case "white": return Vector4.One;
                case "black": return new Vector4(0, 0, 0, 1);
                case "red": return new Vector4(1, 0, 0, 1);
                case "green": return new Vector4(0, 0.5f, 0, 1);
                case "blue": return new Vector4(0, 0, 1, 1);
                case "gray":
                case "grey": return new Vector4(0.5f, 0.5f, 0.5f, 1);
                default: return Vector4.Zero; // transparent, initial, currentcolor, unknown names
            }
        }

        /// <summary>Premultiplies a straight colour for SpriteBatch's default (premultiplied) blending.</summary>
        public static Color Premultiply(Vector4 straight, float opacity)
        {
            float a = MathHelper.Clamp(straight.W * opacity, 0f, 1f);
            return new Color(straight.X * a, straight.Y * a, straight.Z * a, a);
        }

        /// <summary>Parses <c>linear-gradient(...)</c> or <c>radial-gradient(...)</c>; null for anything else.</summary>
        public static CssGradient ParseGradient(string cssValue)
        {
            if (string.IsNullOrWhiteSpace(cssValue)) return null;
            string v = cssValue.Trim().ToLowerInvariant();
            GradientKind kind;
            if (v.StartsWith("linear-gradient(")) kind = GradientKind.Linear;
            else if (v.StartsWith("radial-gradient(")) kind = GradientKind.Radial;
            else return null;

            int open = v.IndexOf('('), close = v.LastIndexOf(')');
            if (close <= open) return null;
            var gradient = new CssGradient { Kind = kind };
            List<string> parts = SplitTopLevel(v.Substring(open + 1, close - open - 1), ',');
            for (int i = 0; i < parts.Count; i++)
            {
                string part = parts[i].Trim();
                if (i == 0 && TryParseDirection(part, kind, out int angle, out bool radialMarker))
                {
                    gradient.Angle = angle;
                    if (radialMarker) gradient.Kind = GradientKind.Radial;
                    continue;
                }
                string colorToken = FirstColorToken(part);
                if (colorToken != null) gradient.Stops.Add(ParseColorVector(colorToken));
            }
            return gradient.Stops.Count > 0 ? gradient : null;
        }

        /// <summary>
        /// Parses a single <c>text-shadow</c> ("0 2px 4px rgba(0,0,0,.6)", colour first or last).
        /// Returns false for <c>none</c>.
        /// </summary>
        public static bool TryParseShadow(string cssValue, out Vector2 offset, out float blur, out Vector4 color)
        {
            offset = Vector2.Zero; blur = 0; color = new Vector4(0, 0, 0, 1);
            if (string.IsNullOrWhiteSpace(cssValue)) return false;
            string v = cssValue.Trim().ToLowerInvariant();
            if (v == "none" || v == "initial") return false;
            // Only the first shadow of a list is drawn.
            v = SplitTopLevel(v, ',')[0];

            string colorToken = FirstColorToken(v);
            if (colorToken != null)
            {
                color = ParseColorVector(colorToken);
                v = v.Replace(colorToken, " ");
            }
            var lengths = new List<float>();
            foreach (string token in v.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                CssLength length = ParseLength(token);
                if (length.Unit == LengthUnit.Pixels) lengths.Add(length.Value);
            }
            if (lengths.Count < 2) return false;
            offset = new Vector2(lengths[0], lengths[1]);
            blur = lengths.Count > 2 ? Math.Max(0, lengths[2]) : 0;
            return color.W > 0;
        }

        // AngleSharp.Css throws (NullReferenceException) computing any radial-gradient, which would
        // fail the element's whole style. Stylesheets are therefore rewritten on load: a radial
        // gradient becomes a linear one at this marker angle, which reads back as radial.
        private const float RadialMarkerDegrees = 0.5f;

        /// <summary>
        /// Rewrites <c>radial-gradient(shape, stops)</c> to <c>linear-gradient(0.5deg, stops)</c> so
        /// AngleSharp can compute it; <see cref="ParseGradient"/> turns the marker back into a radial
        /// gradient (an ellipse reaching the corners).
        /// </summary>
        public static string RewriteRadialGradients(string css)
        {
            if (string.IsNullOrEmpty(css)) return css;
            const string token = "radial-gradient(";
            var sb = new System.Text.StringBuilder(css.Length);
            int pos = 0;
            while (true)
            {
                int start = css.IndexOf(token, pos, StringComparison.OrdinalIgnoreCase);
                if (start < 0) break;
                int depth = 0, end = -1;
                for (int i = start + token.Length - 1; i < css.Length; i++)
                {
                    if (css[i] == '(') depth++;
                    else if (css[i] == ')' && --depth == 0) { end = i; break; }
                }
                if (end < 0) break;
                List<string> args = SplitTopLevel(css.Substring(start + token.Length, end - start - token.Length), ',');
                // Drop the shape/size/position argument ("ellipse at center"); keep the stops.
                if (args.Count > 0 && FirstColorToken(args[0].Trim().ToLowerInvariant()) == null) args.RemoveAt(0);
                sb.Append(css, pos, start - pos);
                sb.Append("linear-gradient(").Append(RadialMarkerDegrees.ToString(CultureInfo.InvariantCulture)).Append("deg");
                foreach (string arg in args) sb.Append(", ").Append(arg.Trim());
                sb.Append(')');
                pos = end + 1;
            }
            sb.Append(css, pos, css.Length - pos);
            return sb.ToString();
        }

        private static bool TryParseDirection(string part, GradientKind kind, out int angle, out bool radialMarker)
        {
            angle = 180;
            radialMarker = false;
            if (kind == GradientKind.Radial)
            {
                // Shape/size/position ("ellipse at center", "circle") — not a colour stop.
                return FirstColorToken(part) == null;
            }
            if (part.StartsWith("to "))
            {
                angle = part.Contains("top") ? 0 : part.Contains("right") ? 90 : part.Contains("left") ? 270 : 180;
                return true;
            }
            float degrees;
            if (part.EndsWith("deg") && TryFloat(part.Substring(0, part.Length - 3), out degrees)) { }
            else if (part.EndsWith("turn") && TryFloat(part.Substring(0, part.Length - 4), out float turns)) degrees = turns * 360f;
            else if (part.EndsWith("rad") && TryFloat(part.Substring(0, part.Length - 3), out float rad)) degrees = MathHelper.ToDegrees(rad);
            else return false;
            radialMarker = Math.Abs(degrees - RadialMarkerDegrees) < 0.05f;
            int snapped =(int)Math.Round(((degrees % 360f) + 360f) % 360f / 90f) % 4;
            angle = snapped * 90;
            return true;
        }

        /// <summary>The colour inside a token list: an rgb()/rgba() call, a #hex, or a named colour.</summary>
        private static string FirstColorToken(string text)
        {
            int fn = text.IndexOf("rgb", StringComparison.Ordinal);
            if (fn >= 0)
            {
                int close = text.IndexOf(')', fn);
                if (close > fn) return text.Substring(fn, close - fn + 1);
            }
            foreach (string token in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.StartsWith("#")) return token;
                switch (token)
                {
                    case "transparent": case "white": case "black": case "red":
                    case "green": case "blue": case "gray": case "grey":
                        return token;
                }
            }
            return null;
        }

        private static List<string> SplitTopLevel(string text, char separator)
        {
            var parts = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '(') depth++;
                else if (ch == ')') depth--;
                else if (ch == separator && depth == 0)
                {
                    parts.Add(text.Substring(start, i - start));
                    start = i + 1;
                }
            }
            parts.Add(text.Substring(start));
            return parts;
        }

        private static bool TryFloat(string s, out float value) =>
            float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
