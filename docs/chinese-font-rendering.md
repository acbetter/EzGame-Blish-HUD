# Chinese font rendering

Chinese text is handled at the `MonoGame.Extended.BitmapFont` boundary so existing
BHM modules do not need to be rebuilt.

## Rendering path

1. Harmony patches `BitmapFont.GetGlyphs` and prepares every CJK character in the
   complete string before measurement or drawing.
2. `BitmapFont.GetCharacterRegion` supplies a Microsoft YaHei UI fallback glyph
   when a module-owned bitmap font does not contain that character.
3. Glyphs are added in independent texture pages. Existing pages are never rebuilt
   when new text appears.
4. Atlas pages are persisted under the Blish HUD cache directory and validated
   against the installed font file before reuse.
5. Resident CJK textures have a 128 MB process-wide limit. The least recently used
   non-base page is removed when space is required.

The bundled prewarm corpus and the scan of text resources in installed `.bhm`
archives reduce first-use work. They are startup optimizations only; missing glyph
correctness does not depend on either list.

Modules that render text into their own images without using MonoGame.Extended
`BitmapFont` remain responsible for their own font handling.
