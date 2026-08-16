using Blish_HUD.Content;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.BitmapFonts;
using MonoGame.Extended.TextureAtlases;
using SpriteFontPlus;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Blish_HUD {

    public class ContentService : GameService {

        private static readonly Logger Logger = Logger.GetLogger<ContentService>();

        private const string REF_FILE = "ref.dat";

        #region Load Static

        private static readonly ConcurrentDictionary<string, BitmapFont>  _loadedBitmapFonts  = new ConcurrentDictionary<string, BitmapFont>();
        private static readonly ConcurrentDictionary<string, Texture2D>   _loadedTextures     = new ConcurrentDictionary<string, Texture2D>();
        private static readonly ConcurrentDictionary<BitmapFont, ChineseFontRequest> _chineseFontRequests = new ConcurrentDictionary<BitmapFont, ChineseFontRequest>();
        private static readonly ConcurrentDictionary<string, ChineseFontAtlas> _chineseFontAtlases = new ConcurrentDictionary<string, ChineseFontAtlas>();
        
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

        private const int  CJK_PAGE_CHARACTER_LIMIT    = 512;
        private const long CJK_GLOBAL_TEXTURE_BUDGET   = 128L * 1024 * 1024;

        private const string CHINESE_FONT_CACHE_FILE = "zh-cn-glyph-cache-v2.txt";
        private const string CHINESE_FONT_ATLAS_CACHE_FOLDER = "zh-cn-atlas-v3";
        private const string CHINESE_FONT_PREWARM_RESOURCE = "Blish_HUD.Fonts.zh-cn-prewarm.txt";
        private const int MICROSOFT_YAHEI_UI_FACE_INDEX = 1;

        private static readonly object             _chineseFontCharacterLock = new object();
        private static readonly Lazy<HashSet<char>> _chineseFontCharacters   = new Lazy<HashSet<char>>(LoadChineseFontCharacters);
        private static readonly SemaphoreSlim       _chineseFontBakeLock      = new SemaphoreSlim(1, 1);
        private static long                         _chineseFontTextureBytes;
        private static int                          _chineseFontPersistScheduled;

        private sealed class ChineseFontRequest {

            public FontFace  Face  { get; }
            public FontSize  Size  { get; }
            public FontStyle Style { get; }
            public string    AtlasKey { get; }

            public ChineseFontRequest(FontFace face, FontSize size, FontStyle style, string atlasKey) {
                this.Face  = face;
                this.Size  = size;
                this.Style = style;
                this.AtlasKey = atlasKey;
            }

        }

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
            ScanInstalledModuleChineseCharacters();
            PrewarmChineseUiFonts();
            BitmapFontFallbackPatches.Install();
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

        private static HashSet<char> LoadChineseFontCharacters() {
            var characters = new HashSet<char>();

            try {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(CHINESE_FONT_PREWARM_RESOURCE)) {
                    if (stream != null) {
                        using (var reader = new StreamReader(stream)) {
                            AddChineseFontCharacters(characters, reader.ReadToEnd());
                        }
                    }
                }
            } catch (Exception ex) {
                Logger.Warn(ex, "Unable to load the bundled Chinese font prewarm character set.");
            }

            try {
                if (File.Exists(ChineseFontCachePath)) {
                    AddChineseFontCharacters(characters, File.ReadAllText(ChineseFontCachePath));
                }
            } catch (Exception ex) {
                Logger.Debug(ex, "Unable to load the learned Chinese UI font cache.");
            }

            return characters;
        }

        private static void AddChineseFontCharacters(HashSet<char> destination, string text) {
            if (destination == null || string.IsNullOrEmpty(text)) return;

            foreach (char character in text) {
                if (IsChineseFontCharacter(character)) {
                    destination.Add(character);
                }
            }
        }

        private static void ScanInstalledModuleChineseCharacters() {
            if (!IsChineseUiCulture()) return;

            string modulesPath = Path.Combine(DirectoryUtil.BasePath, "modules");
            if (!Directory.Exists(modulesPath)) return;

            var textExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                ".json", ".xml", ".txt", ".csv", ".tsv", ".yaml", ".yml", ".properties", ".resx", ".md"
            };
            int discovered = 0;

            foreach (string modulePath in Directory.GetFiles(modulesPath, "*.bhm", SearchOption.TopDirectoryOnly)) {
                try {
                    using (var stream = new FileStream(modulePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var archive = new ZipArchive(stream, ZipArchiveMode.Read)) {
                        foreach (ZipArchiveEntry entry in archive.Entries) {
                            if (entry.Length <= 0 || entry.Length > 4 * 1024 * 1024 ||
                                !textExtensions.Contains(Path.GetExtension(entry.FullName))) {
                                continue;
                            }

                            using (Stream entryStream = entry.Open())
                            using (var reader = new StreamReader(entryStream, Encoding.UTF8, true)) {
                                lock (_chineseFontCharacterLock) {
                                    int before = _chineseFontCharacters.Value.Count;
                                    AddChineseFontCharacters(_chineseFontCharacters.Value, reader.ReadToEnd());
                                    discovered += _chineseFontCharacters.Value.Count - before;
                                }
                            }
                        }
                    }
                } catch (Exception ex) {
                    Logger.Debug(ex, "Unable to scan module font resources in {modulePath}.", modulePath);
                }
            }

            if (discovered > 0) {
                Logger.Info("Discovered {characterCount} additional Chinese characters in installed module resources.", discovered);
                SchedulePersistChineseFontCharacters();
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

        private static void PersistChineseFontCharacters() {
            try {
                Directory.CreateDirectory(DirectoryUtil.CachePath);

                char[] snapshot;
                lock (_chineseFontCharacterLock) {
                    snapshot = _chineseFontCharacters.Value.OrderBy(c => c).ToArray();
                }

                File.WriteAllText(ChineseFontCachePath, new string(snapshot));
            } catch (Exception ex) {
                Logger.Debug(ex, "Unable to write Chinese UI font cache.");
            }
        }

        private static void SchedulePersistChineseFontCharacters() {
            if (Interlocked.Exchange(ref _chineseFontPersistScheduled, 1) != 0) {
                return;
            }

            Task.Run(async () => {
                await Task.Delay(500).ConfigureAwait(false);
                PersistChineseFontCharacters();
                Interlocked.Exchange(ref _chineseFontPersistScheduled, 0);
            });
        }

        public void EnsureChineseFontCharacters(string text) {
            var requestedCharacters = RecordChineseFontCharacters(text);
            if (requestedCharacters.Count == 0) return;

            foreach (var atlas in _chineseFontAtlases.Values) {
                atlas.RequestCharacters(requestedCharacters);
            }
        }

        internal void EnsureChineseFontCharacters(string text, BitmapFont font) {
            var requestedCharacters = RecordChineseFontCharacters(text);
            if (requestedCharacters.Count == 0) return;

            if (font != null &&
                _chineseFontRequests.TryGetValue(font, out var request) &&
                _chineseFontAtlases.TryGetValue(request.AtlasKey, out var atlas)) {
                atlas.RequestCharacters(requestedCharacters);
            }
        }

        /// <summary>
        /// Called by the global BitmapFont hook before a string is measured or
        /// drawn. Main-thread callers synchronously prepare the whole string so
        /// layout and rendering observe the same glyph set on the first frame.
        /// </summary>
        internal void PrepareChineseFontText(BitmapFont sourceFont, string text) {
            var requestedCharacters = RecordChineseFontCharacters(text);
            if (requestedCharacters.Count == 0) return;

            var atlas = GetFallbackAtlas(sourceFont);
            if (atlas == null) return;

            if (Program.IsMainThread) {
                atlas.EnsureCharacters(BlishHud.Instance.GraphicsDevice, requestedCharacters);
            } else {
                // GPU textures are created on the render thread. Static module
                // resources are prewarmed at startup; truly dynamic worker text
                // is queued and becomes available on the next render update.
                atlas.RequestCharacters(requestedCharacters);
            }
        }

        internal BitmapFontRegion ResolveChineseFontCharacter(BitmapFont sourceFont, int character) {
            if (!IsChineseUiCulture() || character < char.MinValue || character > char.MaxValue) return null;

            char requestedCharacter = (char)character;
            if (!IsChineseFontCharacter(requestedCharacter)) return null;

            var atlas = GetFallbackAtlas(sourceFont);
            if (atlas == null) return null;

            if (Program.IsMainThread) {
                atlas.EnsureCharacters(BlishHud.Instance.GraphicsDevice, new[] { requestedCharacter });
            } else {
                atlas.RequestCharacters(new[] { requestedCharacter });
            }

            return atlas.TryGetCharacterRegion(character);
        }

        private ChineseFontAtlas GetFallbackAtlas(BitmapFont sourceFont) {
            if (sourceFont != null &&
                _chineseFontRequests.TryGetValue(sourceFont, out var registeredRequest) &&
                _chineseFontAtlases.TryGetValue(registeredRequest.AtlasKey, out var registeredAtlas)) {
                return registeredAtlas;
            }

            // SpriteFontPlus bakes the UI face two pixels above the requested
            // logical size; reverse that relation when matching a third-party
            // font by its line height.
            int lineHeight = Math.Max(8, Math.Min(36, (sourceFont?.LineHeight ?? 18) - 2));
            FontSize size = Enum.GetValues(typeof(FontSize))
                                .Cast<FontSize>()
                                .OrderBy(candidate => Math.Abs((int)candidate - lineHeight))
                                .First();
            FontStyle style = GetUiFontWeightStyle(FontStyle.Regular);
            string fontPath = GetUiFontPath(style);
            if (fontPath == null) return null;

            string key = $"zh-cn-{Path.GetFileNameWithoutExtension(fontPath).ToLowerInvariant()}-{(int)size}-{style.ToString().ToLowerInvariant()}";
            return _chineseFontAtlases.GetOrAdd(key,
                                                _ => new ChineseFontAtlas(key,
                                                                          fontPath,
                                                                          FontFace.Menomonia,
                                                                          size,
                                                                          style));
        }

        private static HashSet<char> RecordChineseFontCharacters(string text) {
            var requestedCharacters = new HashSet<char>();

            if (!IsChineseUiCulture() || string.IsNullOrEmpty(text)) {
                return requestedCharacters;
            }

            bool added = false;

            lock (_chineseFontCharacterLock) {
                foreach (char character in text) {
                    if (!IsChineseFontCharacter(character)) continue;

                    requestedCharacters.Add(character);
                    added |= _chineseFontCharacters.Value.Add(character);
                }
            }

            if (added) {
                SchedulePersistChineseFontCharacters();
            }

            return requestedCharacters;
        }

        private static bool IsChineseUiCulture() {
            return CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetUiFontPath(FontStyle style) {
            string fontFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            string fontPath = Path.Combine(fontFolder, "msyhbd.ttc");

            return File.Exists(fontPath) ? fontPath : null;
        }

        private static int GetUiFontBakeSize(FontSize size) {
            return (int)size + 2;
        }

        private static byte[] GetTrueTypeCollectionFace(string fontPath, int faceIndex) {
            byte[] fontData = File.ReadAllBytes(fontPath);

            if (fontData.Length < 16 ||
                fontData[0] != (byte)'t' ||
                fontData[1] != (byte)'t' ||
                fontData[2] != (byte)'c' ||
                fontData[3] != (byte)'f') {
                throw new InvalidDataException($"'{fontPath}' is not a TrueType Collection.");
            }

            int faceCount = ReadBigEndianInt32(fontData, 8);
            if (faceIndex < 0 || faceIndex >= faceCount) {
                throw new InvalidDataException($"TrueType Collection face {faceIndex} does not exist in '{fontPath}'.");
            }

            int faceOffset = ReadBigEndianInt32(fontData, 12 + faceIndex * 4);
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

        private static int GetUiFontPageTextureSize(int bakeSize) {
            if (bakeSize <= 20) return 640;
            if (bakeSize <= 24) return 768;

            return 1024;
        }

        private static FontStyle GetUiFontWeightStyle(FontStyle style) {
            // Chinese UI intentionally uses one Microsoft YaHei UI Bold face for
            // every requested style. Sharing one atlas avoids duplicating the
            // same glyph textures for regular, italic, and bold requests.
            return FontStyle.Regular;
        }

        private void PrewarmChineseUiFonts() {
            if (!IsChineseUiCulture()) return;

            HashSet<char> characters;
            lock (_chineseFontCharacterLock) {
                characters = new HashSet<char>(_chineseFontCharacters.Value);
            }

            if (characters.Count == 0) return;

            var commonSizes = new[] {
                FontSize.Size12,
                FontSize.Size14,
                FontSize.Size16,
                FontSize.Size18,
                FontSize.Size32,
            };

            foreach (FontSize size in commonSizes) {
                PrewarmChineseFont(FontFace.Menomonia, size, FontStyle.Regular, characters);
            }

            Logger.Info("Pre-rendered {characterCount} Chinese characters for {fontCount} common UI fonts.",
                        characters.Count,
                        commonSizes.Length);
        }

        private void PrewarmChineseFont(FontFace font, FontSize size, FontStyle style, HashSet<char> characters) {
            style = GetUiFontWeightStyle(style);
            string fontPath = GetUiFontPath(style);
            if (fontPath == null) return;

            string key = $"zh-cn-{Path.GetFileNameWithoutExtension(fontPath).ToLowerInvariant()}-{((int)size).ToString()}-{style.ToString().ToLowerInvariant()}";
            var atlas = _chineseFontAtlases.GetOrAdd(key, _ => new ChineseFontAtlas(key, fontPath, font, size, style));
            atlas.Prewarm(BlishHud.Instance.GraphicsDevice, characters);
        }

        private sealed class ChineseFontAtlas : IDisposable {

            private sealed class Page {
                public Texture2D Texture { get; }
                public IReadOnlyList<BitmapFontRegion> Regions { get; }
                public HashSet<char> Characters { get; }
                public bool IncludesBaseCharacters { get; }
                public long TextureBytes { get; }
                public long LastUsedTicks { get; set; }

                public Page(Texture2D texture, IReadOnlyList<BitmapFontRegion> regions, HashSet<char> characters, bool includesBaseCharacters) {
                    this.Texture                = texture;
                    this.Regions                = regions;
                    this.Characters             = characters;
                    this.IncludesBaseCharacters = includesBaseCharacters;
                    this.TextureBytes           = texture.Width * texture.Height * 4L;
                    this.LastUsedTicks           = DateTime.UtcNow.Ticks;
                }
            }

            private sealed class BakedPage {
                public TtfFontBakerResult Result { get; }
                public Color[] Pixels { get; }
                public int TextureWidth { get; }
                public int TextureHeight { get; }
                public HashSet<char> RequestedCharacters { get; }
                public bool IncludesBaseCharacters { get; }

                public BakedPage(TtfFontBakerResult result, Color[] pixels, int textureWidth, int textureHeight, HashSet<char> requestedCharacters, bool includesBaseCharacters) {
                    this.Result                 = result;
                    this.Pixels                 = pixels;
                    this.TextureWidth           = textureWidth;
                    this.TextureHeight          = textureHeight;
                    this.RequestedCharacters    = requestedCharacters;
                    this.IncludesBaseCharacters = includesBaseCharacters;
                }
            }

            private sealed class CachedGlyph {
                public int Character;
                public int X;
                public int Y;
                public int Width;
                public int Height;
                public int XOffset;
                public int YOffset;
                public int XAdvance;
            }

            private readonly object          _sync = new object();
            private readonly string          _key;
            private readonly string          _fontPath;
            private readonly int             _fontSize;
            private readonly int             _textureSize;
            private readonly FontFace        _face;
            private readonly FontSize        _size;
            private readonly FontStyle       _style;
            private readonly List<Page>      _pages = new List<Page>();
            private readonly HashSet<char>   _pendingCharacters = new HashSet<char>();
            private readonly HashSet<char>   _bakedCharacters = new HashSet<char>();
            private readonly HashSet<char>   _rejectedCharacters = new HashSet<char>();

            private byte[]       _fontData;
            private BitmapFont   _fallbackFont;
            private BitmapFont   _currentFont;
            private bool         _baseCharactersBaked;
            private bool         _buildInProgress;
            private bool         _disposed;
            private bool         _diskCacheLoaded;
            private int          _generation;
            public BitmapFont CurrentFont {
                get {
                    lock (_sync) {
                        return _currentFont ?? _fallbackFont;
                    }
                }
            }

            public ChineseFontAtlas(string key, string fontPath, FontFace face, FontSize size, FontStyle style) {
                _key      = key;
                _fontPath = fontPath;
                _fontSize = GetUiFontBakeSize(size);
                _textureSize = GetUiFontPageTextureSize(_fontSize);
                _face     = face;
                _size     = size;
                _style    = style;
            }

            public void SetFallback(BitmapFont fallbackFont) {
                if (fallbackFont == null) return;

                lock (_sync) {
                    _fallbackFont = fallbackFont;
                }
            }

            public void RequestCharacters(IEnumerable<char> characters) {
                if (characters == null) return;

                lock (_sync) {
                    if (_disposed) return;

                    foreach (char character in characters.Where(IsChineseFontCharacter).Distinct()) {
                        var page = _pages.FirstOrDefault(candidate => candidate.Characters.Contains(character));

                        if (page != null) {
                            page.LastUsedTicks = DateTime.UtcNow.Ticks;
                        }

                        if (page == null &&
                            !_bakedCharacters.Contains(character) &&
                            !_rejectedCharacters.Contains(character)) {
                            _pendingCharacters.Add(character);
                        }
                    }
                }

                StartBuildIfNeeded();
            }

            /// <summary>
            /// Prepares all requested glyphs before returning. This is used by
            /// BitmapFont measurement/drawing on the render thread so the first
            /// frame cannot be laid out with missing Chinese glyphs.
            /// </summary>
            public void EnsureCharacters(GraphicsDevice graphicsDevice, IEnumerable<char> characters) {
                if (graphicsDevice == null || characters == null) return;

                var requestedCharacters = new HashSet<char>(characters.Where(IsChineseFontCharacter));
                if (requestedCharacters.Count == 0) return;

                _chineseFontBakeLock.Wait();
                try {
                    LoadDiskCache(graphicsDevice);

                    if (_fontData == null) {
                        _fontData = GetTrueTypeCollectionFace(_fontPath, MICROSOFT_YAHEI_UI_FACE_INDEX);
                    }

                    bool includeBaseCharacters;
                    HashSet<char> missingCharacters;
                    lock (_sync) {
                        if (_disposed) return;

                        includeBaseCharacters = !_baseCharactersBaked;
                        missingCharacters = new HashSet<char>(requestedCharacters.Where(character => !_bakedCharacters.Contains(character)));
                        foreach (var page in _pages.Where(page => page.Characters.Overlaps(requestedCharacters))) {
                            page.LastUsedTicks = DateTime.UtcNow.Ticks;
                        }
                        _pendingCharacters.ExceptWith(missingCharacters);
                    }

                    if (!includeBaseCharacters && missingCharacters.Count == 0) return;

                    var batches = missingCharacters.OrderBy(character => character)
                                                   .Select((character, index) => new { character, index })
                                                   .GroupBy(item => item.index / CJK_PAGE_CHARACTER_LIMIT)
                                                   .Select(group => new HashSet<char>(group.Select(item => item.character)))
                                                   .ToList();

                    if (batches.Count == 0) {
                        batches.Add(new HashSet<char>());
                    }

                    foreach (var batch in batches) {
                        var bakedPage = BakePage(batch, includeBaseCharacters);
                        CompleteBuild(graphicsDevice, bakedPage, false);
                        includeBaseCharacters = false;
                    }
                } catch (Exception ex) {
                    FailBuild(requestedCharacters, ex);
                } finally {
                    _chineseFontBakeLock.Release();
                }
            }

            public BitmapFontRegion TryGetCharacterRegion(int character) {
                lock (_sync) {
                    var page = _pages.FirstOrDefault(candidate => candidate.Characters.Contains((char)character));
                    if (page != null) page.LastUsedTicks = DateTime.UtcNow.Ticks;
                    return (_currentFont as PagedBitmapFont)?.TryGetCharacterRegionRaw(character);
                }
            }

            public void Prewarm(GraphicsDevice graphicsDevice, IEnumerable<char> characters) {
                EnsureCharacters(graphicsDevice, characters);
            }

            private BakedPage BakePage(HashSet<char> requestedCharacters, bool includeBaseCharacters) {
                var ranges = new List<CharacterRange>();

                if (includeBaseCharacters) {
                    ranges.AddRange(FontUtil.GetRanges(Gw2FontRanges.Default));
                }

                ranges.AddRange(GetCharacterRanges(requestedCharacters));

                var result = TtfFontBaker.Bake(_fontData,
                                               _fontSize,
                                               _textureSize,
                                               _textureSize,
                                               ranges);
                int usedWidth = Math.Max(1, result.Glyphs.Values.Max(glyph => glyph.X + glyph.Width));
                int usedHeight = Math.Max(1, result.Glyphs.Values.Max(glyph => glyph.Y + glyph.Height));
                var pixels = new Color[usedWidth * usedHeight];

                for (int y = 0; y < usedHeight; y++) {
                    for (int x = 0; x < usedWidth; x++) {
                        byte value = result.Pixels[y * result.Width + x];
                        pixels[y * usedWidth + x] = new Color(value, value, value, value);
                    }
                }

                return new BakedPage(result, pixels, usedWidth, usedHeight, requestedCharacters, includeBaseCharacters);
            }

            private void StartBuildIfNeeded() {
                HashSet<char> queuedCharacters;

                lock (_sync) {
                    if (_disposed || _buildInProgress || (_baseCharactersBaked && _pendingCharacters.Count == 0)) {
                        return;
                    }

                    _buildInProgress = true;
                    queuedCharacters = new HashSet<char>(_pendingCharacters);
                    _pendingCharacters.Clear();
                }

                // GPU resource creation and BitmapFont map mutation are both
                // serialized on the render thread. This removes the former
                // delayed/background race that could rebuild the same atlas
                // while a module was drawing it.
                GameService.Graphics.QueueMainThreadRender(graphicsDevice => {
                    lock (_sync) {
                        if (_disposed) {
                            _buildInProgress = false;
                            return;
                        }
                    }

                    try {
                        EnsureCharacters(graphicsDevice, queuedCharacters);
                    } catch (Exception ex) {
                        FailBuild(queuedCharacters, ex);
                    } finally {
                        lock (_sync) {
                            _buildInProgress = false;
                        }
                        StartBuildIfNeeded();
                    }
                });
            }

            private void CompleteBuild(GraphicsDevice graphicsDevice, BakedPage bakedPage, bool continueBuilding = true) {
                lock (_sync) {
                    if (_disposed) {
                        _buildInProgress = false;
                        return;
                    }
                }

                long pageTextureBytes = bakedPage.TextureWidth * bakedPage.TextureHeight * 4L;

                if (!TryReserveChineseFontPage(this, pageTextureBytes)) {
                    lock (_sync) {
                        _rejectedCharacters.UnionWith(bakedPage.RequestedCharacters);
                        _buildInProgress = false;
                    }

                    Logger.Warn("Chinese font texture budget reached. Rejected {characterCount} new characters for {fontName}.",
                                bakedPage.RequestedCharacters.Count,
                                _key);
                    return;
                }

                var texture = new Texture2D(graphicsDevice, bakedPage.TextureWidth, bakedPage.TextureHeight);
                texture.SetData(bakedPage.Pixels);

                var regions = bakedPage.Result.Glyphs.OrderBy(pair => pair.Key)
                                       .Select(pair => {
                                           GlyphInfo glyph = pair.Value;
                                           var textureRegion = new TextureRegion2D(texture, glyph.X, glyph.Y, glyph.Width, glyph.Height);
                                           return new BitmapFontRegion(textureRegion,
                                                                       pair.Key,
                                                                       glyph.XOffset,
                                                                       glyph.YOffset,
                                                                       glyph.XAdvance);
                                       })
                                       .ToList();
                var pageCharacters = new HashSet<char>(bakedPage.Result.Glyphs.Keys.Select(key => (char)key));
                var page = new Page(texture, regions, pageCharacters, bakedPage.IncludesBaseCharacters);

                lock (_sync) {
                    _pages.Add(page);
                    _bakedCharacters.UnionWith(page.Characters);
                    // Requests received while this page was baking may contain the
                    // same characters. Do not bake them again on the next page.
                    _pendingCharacters.ExceptWith(page.Characters);
                    _baseCharactersBaked |= bakedPage.IncludesBaseCharacters;
                    _buildInProgress = false;
                    _generation++;

                    int lineHeight = (int)Math.Ceiling(bakedPage.Result.FontFontPixelHeight);
                    if (_currentFont is PagedBitmapFont font) {
                        font.AppendRegions(regions, _generation);
                    } else {
                        font = new PagedBitmapFont(_key, regions, lineHeight, _generation) {
                            LetterSpacing = 0
                        };

                        _currentFont = font;
                        _chineseFontRequests.TryAdd(font, new ChineseFontRequest(_face, _size, _style, _key));
                    }
                }

                Logger.Debug("Built Chinese font page for {fontName}: {glyphCount} glyphs, {pageCount} resident pages, {textureMegabytes} MB total.",
                             _key,
                             regions.Count,
                             _pages.Count,
                             Interlocked.Read(ref _chineseFontTextureBytes) / (1024 * 1024));

                SaveDiskCache(bakedPage);

                if (continueBuilding) {
                    StartBuildIfNeeded();
                }
            }

            private void FailBuild(HashSet<char> requestedCharacters, Exception exception) {
                lock (_sync) {
                    _pendingCharacters.UnionWith(requestedCharacters);
                    _buildInProgress = false;
                }

                Logger.Warn(exception, "Unable to build Chinese font page for {fontName}.", _key);
            }

            private string DiskCacheDirectory => Path.Combine(DirectoryUtil.CachePath,
                                                               CHINESE_FONT_ATLAS_CACHE_FOLDER,
                                                               _key);

            private void SaveDiskCache(BakedPage bakedPage) {
                try {
                    Directory.CreateDirectory(DiskCacheDirectory);
                    string glyphKey = string.Join("-", bakedPage.Result.Glyphs.Keys.OrderBy(key => key).Select(key => key.ToString("x")));
                    unchecked {
                        uint hash = 2166136261;
                        foreach (char character in glyphKey) hash = (hash ^ character) * 16777619;
                        string path = Path.Combine(DiskCacheDirectory, $"{hash:x8}.bin");

                        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                        using (var writer = new BinaryWriter(stream)) {
                            var fontInfo = new FileInfo(_fontPath);
                            writer.Write("EZCJK3");
                            writer.Write(fontInfo.Length);
                            writer.Write(fontInfo.LastWriteTimeUtc.Ticks);
                            writer.Write(bakedPage.IncludesBaseCharacters);
                            writer.Write(bakedPage.TextureWidth);
                            writer.Write(bakedPage.TextureHeight);
                            writer.Write((int)Math.Ceiling(bakedPage.Result.FontFontPixelHeight));
                            writer.Write(bakedPage.Result.Glyphs.Count);

                            foreach (var pair in bakedPage.Result.Glyphs.OrderBy(pair => pair.Key)) {
                                GlyphInfo glyph = pair.Value;
                                writer.Write(pair.Key);
                                writer.Write(glyph.X);
                                writer.Write(glyph.Y);
                                writer.Write(glyph.Width);
                                writer.Write(glyph.Height);
                                writer.Write(glyph.XOffset);
                                writer.Write(glyph.YOffset);
                                writer.Write(glyph.XAdvance);
                            }

                            writer.Write(bakedPage.Pixels.Length);
                            foreach (Color pixel in bakedPage.Pixels) writer.Write(pixel.PackedValue);
                        }
                    }
                } catch (Exception ex) {
                    Logger.Debug(ex, "Unable to persist Chinese font atlas page for {fontName}.", _key);
                }
            }

            private void LoadDiskCache(GraphicsDevice graphicsDevice) {
                lock (_sync) {
                    if (_diskCacheLoaded || _disposed) return;
                    _diskCacheLoaded = true;
                }

                if (!Directory.Exists(DiskCacheDirectory)) return;

                foreach (string path in Directory.GetFiles(DiskCacheDirectory, "*.bin")) {
                    try {
                        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                        using (var reader = new BinaryReader(stream)) {
                            var fontInfo = new FileInfo(_fontPath);
                            if (reader.ReadString() != "EZCJK3" ||
                                reader.ReadInt64() != fontInfo.Length ||
                                reader.ReadInt64() != fontInfo.LastWriteTimeUtc.Ticks) {
                                continue;
                            }

                            bool includesBase = reader.ReadBoolean();
                            int width = reader.ReadInt32();
                            int height = reader.ReadInt32();
                            int lineHeight = reader.ReadInt32();
                            int glyphCount = reader.ReadInt32();
                            var glyphs = new List<CachedGlyph>(glyphCount);

                            for (int i = 0; i < glyphCount; i++) {
                                glyphs.Add(new CachedGlyph {
                                    Character = reader.ReadInt32(),
                                    X = reader.ReadInt32(),
                                    Y = reader.ReadInt32(),
                                    Width = reader.ReadInt32(),
                                    Height = reader.ReadInt32(),
                                    XOffset = reader.ReadInt32(),
                                    YOffset = reader.ReadInt32(),
                                    XAdvance = reader.ReadInt32(),
                                });
                            }

                            int pixelCount = reader.ReadInt32();
                            if (width <= 0 || height <= 0 || pixelCount != width * height) continue;
                            var pixels = new Color[pixelCount];
                            for (int i = 0; i < pixelCount; i++) pixels[i] = new Color(reader.ReadUInt32());

                            long textureBytes = width * height * 4L;
                            if (!TryReserveChineseFontPage(this, textureBytes)) break;

                            var texture = new Texture2D(graphicsDevice, width, height);
                            texture.SetData(pixels);
                            var regions = glyphs.Select(glyph => new BitmapFontRegion(new TextureRegion2D(texture,
                                                                                                           glyph.X,
                                                                                                           glyph.Y,
                                                                                                           glyph.Width,
                                                                                                           glyph.Height),
                                                                                     glyph.Character,
                                                                                     glyph.XOffset,
                                                                                     glyph.YOffset,
                                                                                     glyph.XAdvance))
                                                .ToList();
                            var page = new Page(texture,
                                                regions,
                                                new HashSet<char>(glyphs.Select(glyph => (char)glyph.Character)),
                                                includesBase);

                            lock (_sync) {
                                _pages.Add(page);
                                _bakedCharacters.UnionWith(page.Characters);
                                _baseCharactersBaked |= includesBase;
                                _generation++;

                                if (_currentFont is PagedBitmapFont currentFont) {
                                    currentFont.AppendRegions(regions, _generation);
                                } else {
                                    currentFont = new PagedBitmapFont(_key, regions, lineHeight, _generation) { LetterSpacing = 0 };
                                    _currentFont = currentFont;
                                    _chineseFontRequests.TryAdd(currentFont, new ChineseFontRequest(_face, _size, _style, _key));
                                }
                            }
                        }
                    } catch (Exception ex) {
                        Logger.Debug(ex, "Unable to restore Chinese font atlas cache page {cachePath}.", path);
                    }
                }
            }

            public bool TryEvictOldestPage(out long lastUsedTicks) {
                Page page;
                lock (_sync) {
                    page = _pages.Where(candidate => !candidate.IncludesBaseCharacters)
                                 .OrderBy(candidate => candidate.LastUsedTicks)
                                 .FirstOrDefault();
                    lastUsedTicks = page?.LastUsedTicks ?? long.MaxValue;
                    if (page == null) return false;

                    _pages.Remove(page);
                    _bakedCharacters.ExceptWith(page.Characters);
                    _generation++;
                    (_currentFont as PagedBitmapFont)?.RemoveRegions(page.Characters, _generation);
                }

                Interlocked.Add(ref _chineseFontTextureBytes, -page.TextureBytes);
                page.Texture.Dispose();
                return true;
            }

            public bool TryPeekOldestPage(out long lastUsedTicks) {
                lock (_sync) {
                    Page page = _pages.Where(candidate => !candidate.IncludesBaseCharacters)
                                      .OrderBy(candidate => candidate.LastUsedTicks)
                                      .FirstOrDefault();
                    lastUsedTicks = page?.LastUsedTicks ?? long.MaxValue;
                    return page != null;
                }
            }

            public void Dispose() {
                List<Texture2D> textures;

                lock (_sync) {
                    if (_disposed) return;
                    _disposed = true;
                    textures = _pages.Select(page => page.Texture).ToList();
                    _pages.Clear();
                }

                Interlocked.Add(ref _chineseFontTextureBytes, -textures.Sum(texture => texture.Width * texture.Height * 4L));

                foreach (var texture in textures) {
                    texture.Dispose();
                }
            }

        }

        private static bool TryReserveChineseFontPage(ChineseFontAtlas requester, long pageTextureBytes) {
            while (true) {
                long currentBytes = Interlocked.Read(ref _chineseFontTextureBytes);
                long reservedBytes = currentBytes + pageTextureBytes;

                if (reservedBytes > CJK_GLOBAL_TEXTURE_BUDGET) {
                    var oldestAtlas = _chineseFontAtlases.Values
                                                         .Select(atlas => new { Atlas = atlas, HasPage = atlas.TryPeekOldestPage(out long ticks), Ticks = ticks })
                                                         .Where(candidate => candidate.HasPage)
                                                         .OrderBy(candidate => candidate.Ticks)
                                                         .Select(candidate => candidate.Atlas)
                                                         .FirstOrDefault();
                    if (oldestAtlas == null || !oldestAtlas.TryEvictOldestPage(out _)) return false;
                    continue;
                }
                if (Interlocked.CompareExchange(ref _chineseFontTextureBytes, reservedBytes, currentBytes) == currentBytes) {
                    return true;
                }
            }
        }

        private BitmapFont GetChineseFont(FontFace font, FontSize size, FontStyle style) {
            style = GetUiFontWeightStyle(style);
            string fontPath = GetUiFontPath(style);

            if (fontPath == null) {
                Logger.Error("Microsoft YaHei UI for style {fontStyle} is missing. Falling back to {fontFace}.", style, font);
                return null;
            }

            string fullFontName = $"zh-cn-{Path.GetFileNameWithoutExtension(fontPath).ToLowerInvariant()}-{((int)size).ToString()}-{style.ToString().ToLowerInvariant()}";
            bool created = false;
            var atlas = _chineseFontAtlases.GetOrAdd(fullFontName, _ => {
                created = true;
                return new ChineseFontAtlas(fullFontName, fontPath, font, size, style);
            });

            if (created) {
                HashSet<char> requestedCharacters;
                lock (_chineseFontCharacterLock) {
                    requestedCharacters = new HashSet<char>(_chineseFontCharacters.Value);
                }

                atlas.RequestCharacters(requestedCharacters);
            }

            return atlas.CurrentFont;
        }

        internal BitmapFont RefreshChineseFont(BitmapFont font) {
            if (font == null || !_chineseFontRequests.TryGetValue(font, out var request)) {
                return font;
            }

            return _chineseFontAtlases.TryGetValue(request.AtlasKey, out var atlas)
                       ? atlas.CurrentFont ?? font
                       : font;
        }

        public BitmapFont GetFont(FontFace font, FontSize size, FontStyle style) {
            if (font == FontFace.Menomonia && IsChineseUiCulture()) {
                var chineseFont = GetChineseFont(font, size, style);

                if (chineseFont != null) {
                    return chineseFont;
                }

                var fallbackFont = GetBuiltInFont(font, size, style);
                RegisterChineseFallback(font, size, style, fallbackFont);
                return fallbackFont;
            }

            return GetBuiltInFont(font, size, style);
        }

        private static void RegisterChineseFallback(FontFace font, FontSize size, FontStyle style, BitmapFont fallbackFont) {
            style = GetUiFontWeightStyle(style);
            string fontPath = GetUiFontPath(style);
            if (fontPath == null || fallbackFont == null) return;

            string atlasKey = $"zh-cn-{Path.GetFileNameWithoutExtension(fontPath).ToLowerInvariant()}-{((int)size).ToString()}-{style.ToString().ToLowerInvariant()}";

            if (_chineseFontAtlases.TryGetValue(atlasKey, out var atlas)) {
                atlas.SetFallback(fallbackFont);
                _chineseFontRequests.AddOrUpdate(fallbackFont,
                                                 new ChineseFontRequest(font, size, style, atlasKey),
                                                 (_, existing) => existing.AtlasKey == atlasKey
                                                                      ? existing
                                                                      : new ChineseFontRequest(font, size, style, atlasKey));
            }
        }

        private BitmapFont GetBuiltInFont(FontFace font, FontSize size, FontStyle style) {
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
            foreach (var atlas in _chineseFontAtlases.Values) {
                atlas.Dispose();
            }

            _chineseFontAtlases.Clear();
            _chineseFontRequests.Clear();
            _loadedTextures.Clear();
            _loadedBitmapFonts.Clear();
        }

        protected override void Update(GameTime gameTime) { /* NOOP */ }

    }
}
