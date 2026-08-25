using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.BitmapFonts;
using System;
using System.Linq;
using Blish_HUD.Controls;

namespace Blish_HUD {
    public static class DrawUtil {

        public static void DrawAlignedText(SpriteBatch sb, SpriteFont sf, string text, Rectangle bounds, Color clr, HorizontalAlignment ha, VerticalAlignment va) {
            // Filter out any characters our font doesn't support
            text = string.Join("", text.ToCharArray().Where(c => sf.Characters.Contains(c)));

            var textSize = sf.MeasureString(text);

            int xPos = bounds.X;
            int yPos = bounds.Y;

            if (ha == HorizontalAlignment.Center) xPos += bounds.Width / 2 - (int)textSize.X / 2;
            if (ha == HorizontalAlignment.Right) xPos += bounds.Width - (int)textSize.X;

            if (va == VerticalAlignment.Middle) yPos += bounds.Height / 2 - (int)textSize.Y / 2;
            if (va == VerticalAlignment.Bottom) yPos += bounds.Height - (int)textSize.Y;

            sb.DrawString(sf, text, new Vector2(xPos, yPos), clr);
        }

        public static void DrawAlignedText(SpriteBatch sb, BitmapFont sf, string text, Rectangle bounds, Color clr, HorizontalAlignment ha = HorizontalAlignment.Left, VerticalAlignment va = VerticalAlignment.Middle) {
            GameService.Content.EnsureChineseFontCharacters(text, sf);
            sf = GameService.Content.RefreshChineseFont(sf);

            Vector2 textSize = sf.MeasureString(text);

            int xPos = bounds.X;
            int yPos = bounds.Y;

            if (ha == HorizontalAlignment.Center) xPos += bounds.Width / 2 - (int)textSize.X / 2;
            if (ha == HorizontalAlignment.Right) xPos += bounds.Width - (int)textSize.X;

            if (va == VerticalAlignment.Middle) yPos += bounds.Height / 2 - (int)textSize.Y / 2;
            if (va == VerticalAlignment.Bottom) yPos += bounds.Height - (int)textSize.Y;

            sb.DrawString(sf, text, new Vector2(xPos, yPos), clr);
        }

        private static string WrapTextSegment(BitmapFont spriteFont, string text, float maxLineWidth) {
            return CjkTextWrapper.Wrap(text,
                                       maxLineWidth,
                                       element => spriteFont.MeasureStringLogical(element).X);
        }

        public static string WrapText(BitmapFont spriteFont, string text, float maxLineWidth) {
            if (string.IsNullOrEmpty(text)) return "";

            GameService.Content.EnsureChineseFontCharacters(text, spriteFont);
            spriteFont = GameService.Content.RefreshChineseFont(spriteFont);

            return string.Join("\n", text.Split('\n').Select(s => WrapTextSegment(spriteFont, s, maxLineWidth)));
        }

    }
}
