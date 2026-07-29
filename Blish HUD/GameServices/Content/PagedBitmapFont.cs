using MonoGame.Extended.BitmapFonts;
using System.Collections.Generic;

namespace Blish_HUD.Content {

    /// <summary>
    /// A bitmap font whose glyph regions may live on multiple texture pages.
    /// Texture ownership remains with the atlas so a newer generation can reuse
    /// all existing pages without copying or rebuilding them.
    /// </summary>
    internal sealed class PagedBitmapFont : BitmapFont {

        public int Generation { get; }

        public PagedBitmapFont(string name, IEnumerable<BitmapFontRegion> regions, int lineHeight, int generation)
            : base(name, regions, lineHeight) {
            this.Generation = generation;
        }

    }

}
