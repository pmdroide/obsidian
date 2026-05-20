using System.Collections.Generic;
using AngleSharp.Dom;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Vista
{
    public class UIElement
    {
        public IElement DomNode { get; private set; }
        public UIElement Parent { get; private set; }
        public List<UIElement> Children { get; private set; } = new List<UIElement>();

        public Rectangle ScreenBounds { get; private set; }

        public UIElement(IElement domNode, UIElement parent = null)
        {
            DomNode = domNode;
            Parent = parent;
        }

        public void CalculateLayout(Rectangle parentBounds)
        {
            var style = DomNode.ComputeCurrentStyle();

            int width = UIStyles.GetPixelValue(style.GetPropertyValue("width"), parentBounds.Width, parentBounds.Width);
            int height = UIStyles.GetPixelValue(style.GetPropertyValue("height"), parentBounds.Height, parentBounds.Height);

            int localX = UIStyles.GetPixelValue(style.GetPropertyValue("left"), parentBounds.Width, 0);
            int localY = UIStyles.GetPixelValue(style.GetPropertyValue("top"), parentBounds.Height, 0);

            string bottomStyle = style.GetPropertyValue("bottom");
            if (!string.IsNullOrEmpty(bottomStyle) && string.IsNullOrEmpty(style.GetPropertyValue("top")))
            {
                localY = parentBounds.Height - height - UIStyles.GetPixelValue(bottomStyle, parentBounds.Height, 0);
            }

            string rightStyle = style.GetPropertyValue("right");
            if (!string.IsNullOrEmpty(rightStyle) && string.IsNullOrEmpty(style.GetPropertyValue("left")))
            {
                localX = parentBounds.Width - width - UIStyles.GetPixelValue(rightStyle, parentBounds.Width, 0);
            }

            int absoluteX = parentBounds.X + localX;
            int absoluteY = parentBounds.Y + localY;

            ScreenBounds = new Rectangle(absoluteX, absoluteY, width, height);

            foreach (var child in Children)
            {
                child.CalculateLayout(ScreenBounds);
            }
        }

        public virtual void Draw(SpriteBatch spriteBatch, Texture2D whitePixel, UIFontRegistry fonts)
        {
            var style = DomNode.ComputeCurrentStyle();
            string bgValue = style.GetPropertyValue("background-color");

            if (!string.IsNullOrEmpty(bgValue) && bgValue != "transparent")
            {
                Color bgColor = UIStyles.ParseColor(bgValue);
                spriteBatch.Draw(whitePixel, ScreenBounds, bgColor);
            }

            DrawTextContent(spriteBatch, fonts, style);

            foreach (var child in Children)
            {
                child.Draw(spriteBatch, whitePixel, fonts);
            }
        }

        private void DrawTextContent(SpriteBatch spriteBatch, UIFontRegistry fonts, AngleSharp.Css.Dom.ICssStyleDeclaration style)
        {
            if (fonts == null) return;

            // Render direct text content only (children's text is rendered by their own pass).
            string text = GetOwnText();
            if (string.IsNullOrWhiteSpace(text)) return;

            string fontKey = style.GetPropertyValue("font-family");
            SpriteFont font = fonts.Resolve(fontKey);
            if (font == null) return;

            string colorValue = style.GetPropertyValue("color");
            Color textColor = string.IsNullOrEmpty(colorValue) ? Color.White : UIStyles.ParseColor(colorValue);

            int padLeft = UIStyles.GetPixelValue(style.GetPropertyValue("padding-left"), ScreenBounds.Width, 0);
            int padTop = UIStyles.GetPixelValue(style.GetPropertyValue("padding-top"), ScreenBounds.Height, 0);

            spriteBatch.DrawString(font, text, new Vector2(ScreenBounds.X + padLeft, ScreenBounds.Y + padTop), textColor);
        }

        private string GetOwnText()
        {
            // AngleSharp's TextContent walks descendants; we only want immediate text nodes here.
            var sb = new System.Text.StringBuilder();
            foreach (var node in DomNode.ChildNodes)
            {
                if (node.NodeType == NodeType.Text)
                    sb.Append(node.TextContent);
            }
            return sb.ToString();
        }
    }
}
