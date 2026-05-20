using System;
using System.Globalization;
using Microsoft.Xna.Framework;

namespace Vista
{
    public static class UIStyles
    {
        public static int GetPixelValue(string cssValue, int maxValue, int defaultValue)
        {
            if (string.IsNullOrEmpty(cssValue) || cssValue == "auto") 
                return defaultValue;

            cssValue = cssValue.Trim().ToLower();

            if (cssValue.EndsWith("%"))
            {
                if (float.TryParse(cssValue.AsSpan(0, cssValue.Length - 1), CultureInfo.InvariantCulture, out float percent))
                    return (int)(maxValue * (percent / 100f));
            }
            if (cssValue.EndsWith("px"))
            {
                if (float.TryParse(cssValue.AsSpan(0, cssValue.Length - 2), CultureInfo.InvariantCulture, out float px))
                    return (int)px;
            }
            if (float.TryParse(cssValue, CultureInfo.InvariantCulture, out float raw))
            {
                return (int)raw;
            }

            return defaultValue;
        }

        public static Color ParseColor(string cssColor)
        {
            if (string.IsNullOrEmpty(cssColor)) return Color.Transparent;
            cssColor = cssColor.Trim().ToLower();

            // Handle Hex: #RRGGBB or #RRGGBBAA
            if (cssColor.StartsWith("#"))
            {
                string hex = cssColor.Substring(1);
                if (hex.Length == 6) hex += "ff"; // Append alpha if missing
                
                uint rgba = uint.Parse(hex, NumberStyles.HexNumber);
                return new Color(
                    (byte)((rgba >> 24) & 0xFF),
                    (byte)((rgba >> 16) & 0xFF),
                    (byte)((rgba >> 8) & 0xFF),
                    (byte)(rgba & 0xFF)
                );
            }

            // Handle functional modern RGB formats: rgba(255, 0, 0, 0.5)
            if (cssColor.StartsWith("rgb"))
            {
                string clean = cssColor.Replace("rgba(", "").Replace("rgb(", "").Replace(")", "");
                string[] parts = clean.Split(',');
                
                byte r = byte.Parse(parts[0].Trim());
                byte g = byte.Parse(parts[1].Trim());
                byte b = byte.Parse(parts[2].Trim());
                byte a = 255;

                if (parts.Length > 3)
                {
                    float alpha = float.Parse(parts[3].Trim(), CultureInfo.InvariantCulture);
                    a = (byte)(alpha * 255f);
                }
                return new Color(r, g, b, a);
            }

            return Color.Transparent;
        }
    }
}