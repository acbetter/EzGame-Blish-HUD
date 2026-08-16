using HarmonyLib;
using MonoGame.Extended.BitmapFonts;
using System;
using System.Reflection;
using System.Text;

namespace Blish_HUD.Content {

    /// <summary>
    /// Adds a process-wide CJK fallback to MonoGame.Extended bitmap fonts.
    /// Existing modules keep calling the public BitmapFont API and therefore do
    /// not need to be rebuilt to benefit from the fallback.
    /// </summary>
    internal static class BitmapFontFallbackPatches {

        private const string HARMONY_ID = "cc.ezgame.blishhud.bitmapfont-cjk-fallback";

        private static readonly Logger Logger = Logger.GetLogger(typeof(BitmapFontFallbackPatches));
        private static bool _installed;

        public static void Install() {
            if (_installed) return;

            try {
                var harmony = new Harmony(HARMONY_ID);
                Patch(harmony,
                      AccessTools.Method(typeof(BitmapFont), nameof(BitmapFont.GetGlyphs), new[] { typeof(string), typeof(MonoGame.Extended.Point2?) }),
                      nameof(PrepareString));
                Patch(harmony,
                      AccessTools.Method(typeof(BitmapFont), nameof(BitmapFont.GetGlyphs), new[] { typeof(StringBuilder), typeof(MonoGame.Extended.Point2?) }),
                      nameof(PrepareStringBuilder));

                harmony.Patch(AccessTools.Method(typeof(BitmapFont), nameof(BitmapFont.GetCharacterRegion), new[] { typeof(int) }),
                              postfix: new HarmonyMethod(typeof(BitmapFontFallbackPatches), nameof(ResolveMissingCharacter)));

                _installed = true;
                Logger.Info("Installed global bitmap-font Chinese fallback.");
            } catch (Exception ex) {
                Logger.Error(ex, "Unable to install global bitmap-font Chinese fallback.");
            }
        }

        private static void Patch(Harmony harmony, MethodInfo original, string prefixName) {
            if (original == null) throw new MissingMethodException(typeof(BitmapFont).FullName, prefixName);

            harmony.Patch(original,
                          prefix: new HarmonyMethod(typeof(BitmapFontFallbackPatches), prefixName));
        }

        private static void PrepareString(BitmapFont __instance, string text) {
            GameService.Content?.PrepareChineseFontText(__instance, text);
        }

        private static void PrepareStringBuilder(BitmapFont __instance, StringBuilder text) {
            if (text != null) {
                GameService.Content?.PrepareChineseFontText(__instance, text.ToString());
            }
        }

        private static void ResolveMissingCharacter(BitmapFont __instance, int character, ref BitmapFontRegion __result) {
            if (__result == null) {
                __result = GameService.Content?.ResolveChineseFontCharacter(__instance, character);
            }
        }
    }
}
