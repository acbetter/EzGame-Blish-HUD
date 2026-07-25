using Blish_HUD.Content;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.BitmapFonts;
using SpriteFontPlus;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Resources;
using System.Text.RegularExpressions;

namespace Blish_HUD {

    public class ContentService : GameService {

        private static readonly Logger Logger = Logger.GetLogger<ContentService>();

        private const string REF_FILE = "ref.dat";

        #region Load Static

        private static readonly ConcurrentDictionary<string, BitmapFont>  _loadedBitmapFonts  = new ConcurrentDictionary<string, BitmapFont>();
        private static readonly ConcurrentDictionary<string, Texture2D>   _loadedTextures     = new ConcurrentDictionary<string, Texture2D>();
        
        #endregion

        public static class Colors {
            public static readonly Color ColonialWhite = Color.FromNonPremultiplied(255, 238, 187, 255);
            public static readonly Color Chardonnay = Color.FromNonPremultiplied(255, 204, 119, 255);
            public static readonly Color OldLace = Color.FromNonPremultiplied(253, 246, 225, 255);

            public static readonly Color DullColor = Color.FromNonPremultiplied(150, 150, 150, 255);

            public static Color Darkened(float amt) {
                return Color.FromNonPremultiplied((int)(amt * 255), (int)(amt * 255), (int)(amt * 255), 255);
            }

        }

        public static class Textures {
            public static Texture2D Error { get; private set; }
            public static Texture2D Pixel { get; private set; }

            public static Texture2D TransparentPixel { get; private set; }

            public static void Load() {
                using (var ctx = Graphics.LendGraphicsDeviceContext(true)) {
                    Error = Content.GetTexture(@"common\error");

                    Pixel = new Texture2D(ctx.GraphicsDevice, 1, 1);
                    Pixel.SetData(new[] { Color.White });

                    TransparentPixel = new Texture2D(ctx.GraphicsDevice, 1, 1);
                    TransparentPixel.SetData(new[] { Color.Transparent });
                }
            }
        }

        private IDataReader _audioDataReader;

        private const int CJK_BASE_TEXTURE_SIZE     = 2048;
        private const int CJK_EXTENDED_TEXTURE_SIZE = 4096;
        private const int CJK_EXTENDED_THRESHOLD    = 1600;
        private const int CJK_MAX_CACHE_CHARACTERS  = 4000;

        private const string CHINESE_FONT_CACHE_FILE = "zh-cn-glyph-cache.txt";

        private static readonly object             _chineseFontCharacterLock = new object();
        private static readonly Lazy<HashSet<char>> _chineseFontCharacters   = new Lazy<HashSet<char>>(LoadChineseFontCharacters);

        public BitmapFont DefaultFont12 => GetFont(FontFace.Menomonia, FontSize.Size12, FontStyle.Regular);

        public BitmapFont DefaultFont14 => GetFont(FontFace.Menomonia, FontSize.Size14, FontStyle.Regular);

        public BitmapFont DefaultFont16 => GetFont(FontFace.Menomonia, FontSize.Size16, FontStyle.Regular);

        public BitmapFont DefaultFont18 => GetFont(FontFace.Menomonia, FontSize.Size18, FontStyle.Regular);

        public BitmapFont DefaultFont32 => GetFont(FontFace.Menomonia, FontSize.Size32, FontStyle.Regular);

        public enum FontFace {
            Menomonia
        }

        public enum FontSize {
            Size8 = 8,
            Size11 = 11,
            Size12 = 12,
            Size14 = 14,
            Size16 = 16,
            Size18 = 18,
            Size20 = 20,
            Size22 = 22,
            Size24 = 24,
            Size32 = 32,
            Size34 = 34,
            Size36 = 36,
        }

        public enum FontStyle {
            Regular,
            Italic,
            Bold,
        }

        public ContentManager ContentManager => BlishHud.Instance.ActiveContentManager;

        public DatAssetCache DatAssetCache { get; private set; }

        internal ContentService() {
            SetServiceModules(this.DatAssetCache = new DatAssetCache(this));
        }

        protected override void Initialize() {
            // Typically occurs when Blish HUD is extracted without its dependencies.
            if (!File.Exists(ApplicationSettings.Instance.RefPath)) {
                Blish_HUD.Debug.Contingency.NotifyMissingRef();
            }
        }

        protected override void Load() {
            Textures.Load();
            var zipArchiveReader = new ZipArchiveReader(RefPath);
            _audioDataReader = zipArchiveReader.GetSubPath("audio");
        }

        // Temporary failsafe until all audio bugs are resolved
        private int _playRemainingAttempts = 3;

        public void PlaySoundEffectByName(string soundName) {
            if (_playRemainingAttempts <= 0) {
                // We keep failing to play sound effects - don't even bother
                return;
            }

            if (GameService.GameIntegration.Audio.AudioDevice == null) {
                // No device is set yet or there isn't one to use
                return;
            }

            try {
                const string SOUND_EFFECT_FILE_EXTENSION = ".wav";
                var          filePath                    = soundName + SOUND_EFFECT_FILE_EXTENSION;

                if (_audioDataReader.FileExists(filePath)) {
                    SoundEffect.FromStream(_audioDataReader.GetFileStream(filePath)).Play(GameService.GameIntegration.Audio.Volume, 0, 0);
                }

                _playRemainingAttempts = 3;
            } catch (Exception ex) {
                _playRemainingAttempts--;
                Logger.Warn(ex, "Failed to play sound effect.");
            }
        }

        private static string RefPath => ApplicationSettings.Instance.RefPath ?? REF_FILE;

        // Used while debugging since it's easier
        private static Texture2D TextureFromFile(string filepath) {
            if (File.Exists(filepath)) {
                using (var fileStream = new FileStream(filepath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                    return TextureUtil.FromStreamPremultiplied(BlishHud.Instance.GraphicsDevice, fileStream);
                }
            } else return null;
        }

        private static Texture2D TextureFromFileSystem(string filepath) {
            var refPath = RefPath;
            if (!File.Exists(refPath)) {
                Logger.Warn("{refFileName} is missing!  Lots of assets will be missing!", refPath);
                return null;
            }

            using (var refFs = new FileStream(refPath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                using (var refArchive = new ZipArchive(refFs, ZipArchiveMode.Read)) {
                    var refEntry = refArchive.GetEntry(filepath);

                    if (refEntry != null) {
                        using (var textureStream = refEntry.Open()) {
                            var textureCanSeek = new MemoryStream();
                            textureStream.CopyTo(textureCanSeek);

                            if (GameService.Graphics == null) {
                                return TextureUtil.FromStreamPremultiplied(BlishHud.Instance.GraphicsDevice, textureCanSeek);
                            } else {
                                return TextureUtil.FromStreamPremultiplied(textureCanSeek);
                            }
                        }
                    }

                    return null;
                }
            }
        }

        public MonoGame.Extended.TextureAtlases.TextureAtlas GetTextureAtlas(string textureAtlasName) {
            return GameService.Content.ContentManager.Load<MonoGame.Extended.TextureAtlases.TextureAtlas>(textureAtlasName);
        }

        public void PurgeTextureCache(string textureName) {
            _loadedTextures.TryRemove(textureName, out var _);
        }

        public Texture2D GetTexture(string textureName) {
            return GetTexture(textureName, Textures.Error);
        }

        public Texture2D GetTexture(string textureName, Texture2D defaultTexture) {
            if (textureName == null) return defaultTexture;

            if (_loadedTextures.TryGetValue(textureName, out var cachedTexture))
                return cachedTexture;

            if (File.Exists(textureName)) {
                return TextureFromFile(textureName);
            }

            cachedTexture = TextureFromFileSystem($"{textureName}.png");

            if (cachedTexture == null) {
                try {
                    cachedTexture = GameService.Content.ContentManager.Load<Texture2D>(textureName);
                } catch (ContentLoadException) {
                    Logger.Warn("Could not find {textureName} precompiled or in the ref archive.", textureName);
                }
            }

            cachedTexture = cachedTexture ?? defaultTexture;

            _loadedTextures.TryAdd(textureName, cachedTexture);

            return cachedTexture;
        }

        #region Render Service

        private const string RENDERSERVICE_REQUESTURL = "https://render.guildwars2.com/file/";

        private static readonly Regex _regexRenderServiceSignatureFileIdPair = new Regex(@"(.{40})\/(\d+)(?>\..*)?$", RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>
        /// Retreives a texture from the Guild Wars 2 Render Service.
        /// </summary>
        /// <param name="signature">The SHA1 signature of the requested texture.</param>
        /// <param name="fileId">The file id of the requested texture.</param>
        /// <returns>A transparent texture that is later overwritten by the texture downloaded from the Render Service.</returns>
        /// <seealso cref="https://wiki.guildwars2.com/wiki/API:Render_service"/>
        public AsyncTexture2D GetRenderServiceTexture(string signature, string fileId) {
            return this.DatAssetCache.GetTextureFromAssetId(int.Parse(fileId));
        }

        /// <summary>
        /// Retreives a texture from the Guild Wars 2 Render Service.
        /// </summary>
        /// <param name="uriOrSignatureFileIdPair">Either the full Render Service URL or the signature and file id URI (e.g. "7554DCAF5A1EA1BDF5297352A203AF2357BE2B5B/498983").</param>
        /// <returns>A transparent texture that is later overwritten by the texture downloaded from the Render Service.</returns>
        /// <seealso cref="https://wiki.guildwars2.com/wiki/API:Render_service"/>
        public AsyncTexture2D GetRenderServiceTexture(string uriOrSignatureFileIdPair) {
            var splitUri = _regexRenderServiceSignatureFileIdPair.Match(uriOrSignatureFileIdPair);

            if (!splitUri.Success) {
                throw new ArgumentException($"Could not find signature / file id pair in provided '{uriOrSignatureFileIdPair}'.", nameof(uriOrSignatureFileIdPair));
            }

            string signature = splitUri.Groups[1].Value;
            string fileId    = splitUri.Groups[2].Value;

            return GetRenderServiceTexture(signature, fileId);
        }

        #endregion

        private static string ChineseFontCachePath => Path.Combine(DirectoryUtil.CachePath, CHINESE_FONT_CACHE_FILE);

        private static IReadOnlyList<CharacterRange> GetChineseUiFontRanges(out int characterCount) {
            HashSet<char> characters;

            lock (_chineseFontCharacterLock) {
                characters = new HashSet<char>(_chineseFontCharacters.Value);
            }

            characterCount = characters.Count;

            var ranges = new List<CharacterRange>(FontUtil.GetRanges(Gw2FontRanges.Default));
            ranges.AddRange(GetCharacterRanges(characters));

            return ranges;
        }

        private static HashSet<char> LoadChineseFontCharacters() {
            var characters = new HashSet<char>();

            AddChineseFontCharacters(characters, GetChineseUiCharacters());
            AddChineseFontCharacters(characters, GetCommonChinesePunctuation());

            try {
                if (File.Exists(ChineseFontCachePath)) {
                    AddChineseFontCharacters(characters, File.ReadAllText(ChineseFontCachePath));
                }
            } catch (Exception ex) {
                Logger.Debug(ex, "Unable to read Chinese UI font cache.");
            }

            return characters;
        }

        private static string GetChineseUiCharacters() {
            var characters = new HashSet<char>();
            var assembly   = typeof(ContentService).Assembly;
            var culture    = CultureInfo.GetCultureInfo("zh-CN");

            foreach (string resourceName in assembly.GetManifestResourceNames().Where(IsStringResourceName)) {
                string baseName = resourceName.Substring(0, resourceName.Length - ".resources".Length);

                try {
                    ResourceSet resourceSet = new ResourceManager(baseName, assembly).GetResourceSet(culture, true, true);

                    if (resourceSet == null) {
                        continue;
                    }

                    foreach (DictionaryEntry entry in resourceSet) {
                        if (entry.Value is string value) {
                            AddChineseFontCharacters(characters, value);
                        }
                    }
                } catch (MissingManifestResourceException) {
                    Logger.Debug("Unable to inspect string resource {resourceName} for Chinese UI font characters.", resourceName);
                }
            }

            return new string(characters.OrderBy(c => c).ToArray());
        }

        private static string GetCommonChinesePunctuation() {
            return "\uFF0C\u3002\u3001\uFF1B\uFF1A\uFF1F\uFF01\u300A\u300B\u3008\u3009"
                 + "\uFF08\uFF09\u3010\u3011\u300C\u300D\u300E\u300F\u201C\u201D"
                 + "\u2018\u2019\u2014\u2026\u00B7\uFFE5";
        }

        private static bool IsStringResourceName(string resourceName) {
            return resourceName.StartsWith("Blish_HUD.Strings.", StringComparison.Ordinal)
                && resourceName.EndsWith(".resources", StringComparison.Ordinal);
        }

        private static void AddChineseFontCharacters(ISet<char> characters, string text) {
            if (string.IsNullOrEmpty(text)) {
                return;
            }

            foreach (char character in text) {
                if (IsChineseFontCharacter(character)) {
                    characters.Add(character);
                }
            }
        }

        private static bool IsChineseFontCharacter(char character) {
            return !char.IsSurrogate(character)
                && (character >= 0x2E80
                 || character >= 0x2010 && character <= 0x203B);
        }

        private static IEnumerable<CharacterRange> GetCharacterRanges(IEnumerable<char> characters) {
            var sortedCharacters = characters.Distinct().OrderBy(c => c).ToList();

            if (sortedCharacters.Count == 0) {
                yield break;
            }

            char rangeStart = sortedCharacters[0];
            char previous   = rangeStart;

            for (int i = 1; i < sortedCharacters.Count; i++) {
                char current = sortedCharacters[i];

                if (current == previous + 1) {
                    previous = current;
                    continue;
                }

                yield return new CharacterRange(rangeStart, previous);

                rangeStart = current;
                previous   = current;
            }

            yield return new CharacterRange(rangeStart, previous);
        }

        private static int GetChineseFontTextureSize(int characterCount) {
            return characterCount >= CJK_EXTENDED_THRESHOLD
                       ? CJK_EXTENDED_TEXTURE_SIZE
                       : CJK_BASE_TEXTURE_SIZE;
        }

        private static void PersistChineseFontCharacters() {
            try {
                Directory.CreateDirectory(DirectoryUtil.CachePath);
                File.WriteAllText(ChineseFontCachePath, new string(_chineseFontCharacters.Value.OrderBy(c => c).ToArray()));
            } catch (Exception ex) {
                Logger.Debug(ex, "Unable to write Chinese UI font cache.");
            }
        }

        private static void PurgeLoadedChineseFonts() {
            foreach (string key in _loadedBitmapFonts.Keys.Where(key => key.StartsWith("zh-cn-", StringComparison.OrdinalIgnoreCase)).ToList()) {
                _loadedBitmapFonts.TryRemove(key, out _);
            }
        }

        public void EnsureChineseFontCharacters(string text) {
            if (!IsChineseUiCulture() || string.IsNullOrEmpty(text)) {
                return;
            }

            bool added = false;

            lock (_chineseFontCharacterLock) {
                foreach (char character in text) {
                    if (!IsChineseFontCharacter(character) || _chineseFontCharacters.Value.Contains(character)) {
                        continue;
                    }

                    if (_chineseFontCharacters.Value.Count >= CJK_MAX_CACHE_CHARACTERS) {
                        Logger.Warn("Chinese UI font cache reached the character limit of {characterLimit}.", CJK_MAX_CACHE_CHARACTERS);
                        break;
                    }

                    _chineseFontCharacters.Value.Add(character);
                    added = true;
                }

                if (!added) {
                    return;
                }

                PersistChineseFontCharacters();
                PurgeLoadedChineseFonts();
            }
        }

        private static bool IsChineseUiCulture() {
            return CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetChineseFontFilePath(FontStyle style) {
            string fontFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            string[] candidates = style == FontStyle.Bold
                                      ? new[] { "msyhbd.ttc", "msyh.ttc", "Dengb.ttf", "simhei.ttf", "NotoSansSC-VF.ttf", "Deng.ttf", "simsunb.ttf" }
                                      : new[] { "msyhbd.ttc", "msyh.ttc", "Deng.ttf", "simhei.ttf", "NotoSansSC-VF.ttf", "simsunb.ttf" };

            return candidates.Select(candidate => Path.Combine(fontFolder, candidate))
                             .FirstOrDefault(File.Exists);
        }

        private static byte[] GetTtfBakerFontData(string fontPath, int collectionFaceIndex) {
            byte[] fontData = File.ReadAllBytes(fontPath);

            // SpriteFontPlus initializes fonts at byte offset 0 and therefore cannot
            // consume a TrueType Collection directly. Expose the selected face at
            // offset 0 while retaining its absolute table offsets.
            if (fontData.Length < 16 ||
                fontData[0] != (byte)'t' ||
                fontData[1] != (byte)'t' ||
                fontData[2] != (byte)'c' ||
                fontData[3] != (byte)'f') {
                return fontData;
            }

            int faceCount = ReadBigEndianInt32(fontData, 8);
            if (collectionFaceIndex < 0 || collectionFaceIndex >= faceCount) {
                throw new InvalidDataException($"TrueType Collection face {collectionFaceIndex} does not exist in '{fontPath}'.");
            }

            int faceOffset = ReadBigEndianInt32(fontData, 12 + collectionFaceIndex * 4);
            if (faceOffset < 0 || faceOffset > fontData.Length - 12) {
                throw new InvalidDataException($"Invalid TrueType Collection face offset in '{fontPath}'.");
            }

            int tableCount = (fontData[faceOffset + 4] << 8) | fontData[faceOffset + 5];
            int directoryLength = checked(12 + tableCount * 16);
            if (directoryLength > fontData.Length - faceOffset) {
                throw new InvalidDataException($"Invalid TrueType Collection table directory in '{fontPath}'.");
            }

            Buffer.BlockCopy(fontData, faceOffset, fontData, 0, directoryLength);
            return fontData;
        }

        private static int ReadBigEndianInt32(byte[] data, int offset) {
            return (data[offset] << 24) |
                   (data[offset + 1] << 16) |
                   (data[offset + 2] << 8) |
                   data[offset + 3];
        }

        private BitmapFont GetChineseFont(FontFace font, FontSize size, FontStyle style) {
            string fontPath = GetChineseFontFilePath(style);

            if (fontPath == null) {
                Logger.Warn("Unable to find a Windows CJK font. Falling back to {fontFace}.", font);
                return null;
            }

            string fullFontName = $"zh-cn-{Path.GetFileNameWithoutExtension(fontPath).ToLowerInvariant()}-{((int)size).ToString()}-{style.ToString().ToLowerInvariant()}";

            if (!_loadedBitmapFonts.ContainsKey(fullFontName)) {
                try {
                    var ranges      = GetChineseUiFontRanges(out int characterCount);
                    int textureSize = GetChineseFontTextureSize(characterCount);
                    int fontSize    = (int)size + 2;

                    using var ctx = GameService.Graphics.LendGraphicsDeviceContext();

                    // Microsoft YaHei UI is the second face in the Windows YaHei
                    // collections and is tuned for small interface text.
                    int collectionFaceIndex = Path.GetExtension(fontPath).Equals(".ttc", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                    var loadedFont = TtfFontBaker.Bake(GetTtfBakerFontData(fontPath, collectionFaceIndex),
                                                       fontSize,
                                                       textureSize,
                                                       textureSize,
                                                       ranges)
                                                .CreateSpriteFont(ctx.GraphicsDevice)
                                                .ToBitmapFont();

                    loadedFont.LetterSpacing = 0;
                    _loadedBitmapFonts.TryAdd(fullFontName, loadedFont);

                    return loadedFont;
                } catch (Exception ex) {
                    Logger.Warn(ex, "Unable to load Chinese UI font {fontPath}. Falling back to {fontFace}.", fontPath, font);
                    return null;
                }
            }

            return _loadedBitmapFonts[fullFontName];
        }

        public BitmapFont GetFont(FontFace font, FontSize size, FontStyle style) {
            if (font == FontFace.Menomonia && IsChineseUiCulture()) {
                var chineseFont = GetChineseFont(font, size, style);

                if (chineseFont != null) {
                    return chineseFont;
                }
            }

            string fullFontName = $"{font.ToString().ToLowerInvariant()}-{((int)size).ToString()}-{style.ToString().ToLowerInvariant()}";

            if (!_loadedBitmapFonts.ContainsKey(fullFontName)) {
                var loadedFont = this.ContentManager.Load<BitmapFont>($"fonts\\{font.ToString().ToLowerInvariant()}\\{fullFontName}");
                loadedFont.LetterSpacing = -1;
                _loadedBitmapFonts.TryAdd(fullFontName, loadedFont);

                return loadedFont;
            }

            return _loadedBitmapFonts[fullFontName];
        }

        protected override void Unload() {
            _loadedTextures.Clear();
            _loadedBitmapFonts.Clear();
        }

        protected override void Update(GameTime gameTime) { /* NOOP */ }

    }
}
