using System.Collections.Generic;
using AngleSharp.Css.Dom;
using Microsoft.Xna.Framework;

namespace Vista
{
    public enum TextAlign { Left, Center, Right }
    public enum VerticalAlign { Top, Middle, Bottom }

    /// <summary>
    /// The CSS an element uses, parsed once from AngleSharp's computed style. Computing a style
    /// costs ~0.15 ms, so <see cref="UIManager"/> only recomputes elements whose class, attributes
    /// or position in the tree changed.
    /// </summary>
    public sealed class UIComputedStyle
    {
        public bool DisplayNone;
        public bool Hidden;
        public bool PointerEvents = true;

        public CssLength Left, Top, Right, Bottom, Width, Height;
        /// <summary>Percent margins resolve against the parent's width, as in CSS.</summary>
        public CssLength MarginLeft, MarginTop, MarginRight, MarginBottom;
        public float PaddingLeft, PaddingTop, PaddingRight, PaddingBottom;

        /// <summary>Top, right, bottom, left.</summary>
        public readonly float[] BorderWidth = new float[4];
        public readonly Vector4[] BorderColor = new Vector4[4];

        public Vector4 Background;
        public CssGradient BackgroundGradient;
        public string BackgroundGradientKey;
        public Vector4 Color = Vector4.One;
        public float Opacity = 1f;

        public string FontFamily;
        public TextAlign TextAlign;
        public VerticalAlign VerticalAlign;
        public float LetterSpacing;
        /// <summary>Line box height in pixels; 0 = the font's own line spacing.</summary>
        public float LineHeight;
        public float LineHeightFactor = 1f;
        public bool Uppercase, Lowercase;
        public bool NoWrap;
        public bool PreserveNewlines;

        public bool HasShadow;
        public Vector2 ShadowOffset;
        public float ShadowBlur;
        public Vector4 ShadowColor;

        private Dictionary<string, float> _transitions;
        private float _transitionAll;

        /// <summary>Transition length in seconds for a property ("opacity", "left", ...); 0 = snap.</summary>
        public float TransitionFor(string property)
        {
            if (_transitions != null && _transitions.TryGetValue(property, out float d)) return d;
            return _transitionAll;
        }

        /// <param name="s">AngleSharp's computed style (inheritance applied; colours as rgba()).</param>
        /// <param name="box">
        /// The cascaded (declared) style. Box geometry comes from here: AngleSharp resolves
        /// percentages in the computed style against its render device, not the parent box.
        /// </param>
        public static UIComputedStyle From(ICssStyleDeclaration s, ICssStyleDeclaration box = null)
        {
            box ??= s;
            var style = new UIComputedStyle
            {
                DisplayNone = s.GetPropertyValue("display") == "none",
                Hidden = s.GetPropertyValue("visibility") is "hidden" or "collapse",
                PointerEvents = s.GetPropertyValue("pointer-events") != "none",

                Left = UIStyles.ParseLength(box.GetPropertyValue("left")),
                Top = UIStyles.ParseLength(box.GetPropertyValue("top")),
                Right = UIStyles.ParseLength(box.GetPropertyValue("right")),
                Bottom = UIStyles.ParseLength(box.GetPropertyValue("bottom")),
                Width = UIStyles.ParseLength(box.GetPropertyValue("width")),
                Height = UIStyles.ParseLength(box.GetPropertyValue("height")),

                MarginLeft = UIStyles.ParseLength(box.GetPropertyValue("margin-left")),
                MarginTop = UIStyles.ParseLength(box.GetPropertyValue("margin-top")),
                MarginRight = UIStyles.ParseLength(box.GetPropertyValue("margin-right")),
                MarginBottom = UIStyles.ParseLength(box.GetPropertyValue("margin-bottom")),
                PaddingLeft = UIStyles.ParsePixels(box.GetPropertyValue("padding-left")),
                PaddingTop = UIStyles.ParsePixels(box.GetPropertyValue("padding-top")),
                PaddingRight = UIStyles.ParsePixels(box.GetPropertyValue("padding-right")),
                PaddingBottom = UIStyles.ParsePixels(box.GetPropertyValue("padding-bottom")),

                Background = UIStyles.ParseColorVector(s.GetPropertyValue("background-color")),
                FontFamily = s.GetPropertyValue("font-family"),
                LetterSpacing = UIStyles.ParsePixels(s.GetPropertyValue("letter-spacing")),
            };

            string color = s.GetPropertyValue("color");
            if (!string.IsNullOrEmpty(color)) style.Color = UIStyles.ParseColorVector(color);

            string opacity = s.GetPropertyValue("opacity");
            if (!string.IsNullOrEmpty(opacity) && float.TryParse(opacity, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float o))
                style.Opacity = MathHelper.Clamp(o, 0f, 1f);

            string image = s.GetPropertyValue("background-image");
            style.BackgroundGradient = UIStyles.ParseGradient(image);
            if (style.BackgroundGradient != null) style.BackgroundGradientKey = image;

            string[] sides = { "top", "right", "bottom", "left" };
            for (int i = 0; i < 4; i++)
            {
                string borderStyle = s.GetPropertyValue("border-" + sides[i] + "-style");
                if (string.IsNullOrEmpty(borderStyle) || borderStyle == "none" || borderStyle == "hidden") continue;
                style.BorderWidth[i] = UIStyles.ParsePixels(s.GetPropertyValue("border-" + sides[i] + "-width"), 3f);
                string borderColor = s.GetPropertyValue("border-" + sides[i] + "-color");
                style.BorderColor[i] = string.IsNullOrEmpty(borderColor) ? style.Color : UIStyles.ParseColorVector(borderColor);
            }

            style.TextAlign = s.GetPropertyValue("text-align") switch
            {
                "center" => TextAlign.Center,
                "right" or "end" => TextAlign.Right,
                _ => TextAlign.Left,
            };
            style.VerticalAlign = s.GetPropertyValue("vertical-align") switch
            {
                "middle" => VerticalAlign.Middle,
                "bottom" or "text-bottom" => VerticalAlign.Bottom,
                _ => VerticalAlign.Top,
            };

            string lineHeight = s.GetPropertyValue("line-height");
            CssLength lh = UIStyles.ParseLength(lineHeight);
            if (lh.Unit == LengthUnit.Pixels && lineHeight.EndsWith("px")) style.LineHeight = lh.Value;
            else if (lh.Unit == LengthUnit.Pixels) style.LineHeightFactor = lh.Value; // unitless multiplier
            else if (lh.Unit == LengthUnit.Percent) style.LineHeightFactor = lh.Value / 100f;

            string transform = s.GetPropertyValue("text-transform");
            style.Uppercase = transform == "uppercase";
            style.Lowercase = transform == "lowercase";

            string whiteSpace = s.GetPropertyValue("white-space");
            style.NoWrap = whiteSpace is "nowrap" or "pre";
            style.PreserveNewlines = whiteSpace is "pre" or "pre-line" or "pre-wrap";

            style.HasShadow = UIStyles.TryParseShadow(s.GetPropertyValue("text-shadow"),
                out style.ShadowOffset, out style.ShadowBlur, out style.ShadowColor);

            ParseTransitions(style, s.GetPropertyValue("transition-property"), s.GetPropertyValue("transition-duration"));
            return style;
        }

        private static void ParseTransitions(UIComputedStyle style, string properties, string durations)
        {
            List<float> times = UIStyles.ParseTimes(durations);
            if (times.Count == 0 || string.IsNullOrWhiteSpace(properties)) return;
            string[] names = properties.Split(',');
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i].Trim();
                float duration = times[i % times.Count];
                if (name == "all") { style._transitionAll = duration; continue; }
                if (name == "none") continue;
                style._transitions ??= new Dictionary<string, float>();
                style._transitions[name] = duration;
                // Shorthands animate their longhands.
                if (name == "border-color")
                    foreach (string side in new[] { "top", "right", "bottom", "left" })
                        style._transitions["border-" + side + "-color"] = duration;
                if (name == "inset")
                    foreach (string side in new[] { "top", "right", "bottom", "left" })
                        style._transitions[side] = duration;
            }
        }
    }
}
