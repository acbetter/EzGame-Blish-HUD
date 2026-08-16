using MonoGame.Extended.BitmapFonts;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Blish_HUD.Content {

    /// <summary>
    /// A bitmap font whose glyph regions may live on multiple texture pages.
    /// Texture ownership remains with the atlas so a newer generation can reuse
    /// all existing pages without copying or rebuilding them.
    /// </summary>
    internal sealed class PagedBitmapFont : BitmapFont {

        private static readonly FieldInfo CharacterMapField = typeof(BitmapFont).GetField("_characterMap",
                                                                                         BindingFlags.Instance |
                                                                                         BindingFlags.NonPublic)
                                                              ?? throw new MissingFieldException(typeof(BitmapFont).FullName,
                                                                                                 "_characterMap");

        private readonly Dictionary<int, BitmapFontRegion> _characterMap;

        public int Generation { get; private set; }

        public PagedBitmapFont(string name, IEnumerable<BitmapFontRegion> regions, int lineHeight, int generation)
            : base(name, regions, lineHeight) {
            _characterMap = (Dictionary<int, BitmapFontRegion>)CharacterMapField.GetValue(this);
            this.Generation = generation;
        }

        /// <summary>
        /// Appends glyphs without replacing the font instance held
        /// by controls and cached renders. This runs on the render thread.
        /// </summary>
        public void AppendRegions(IEnumerable<BitmapFontRegion> regions, int generation) {
            foreach (var region in regions) {
                _characterMap[region.Character] = region;
            }

            this.Generation = generation;
        }

        public BitmapFontRegion TryGetCharacterRegionRaw(int character) {
            return _characterMap.TryGetValue(character, out var region) ? region : null;
        }

        public void RemoveRegions(IEnumerable<char> characters, int generation) {
            foreach (char character in characters) {
                _characterMap.Remove(character);
            }

            this.Generation = generation;
        }

    }

}
