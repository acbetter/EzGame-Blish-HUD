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

        private const int  CJK_PAGE_TEXTURE_SIZE       = 1024;
        private const int  CJK_PAGE_CHARACTER_LIMIT    = 128;
        private const int  CJK_MAX_PAGES_PER_FONT      = 8;
        private const long CJK_GLOBAL_TEXTURE_BUDGET   = 96L * 1024 * 1024;
        private const long CJK_PAGE_TEXTURE_BYTES      = CJK_PAGE_TEXTURE_SIZE * CJK_PAGE_TEXTURE_SIZE * 4L;
        private const int  CJK_EVICTION_GRACE_SECONDS  = 10;

        private const string CHINESE_FONT_CACHE_FILE = "zh-cn-glyph-cache-v2.txt";

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
            // Version 2 intentionally starts empty on every launch. The cache file
            // is diagnostic only: preloading it would defeat demand-driven paging.
            return new HashSet<char>();
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

        private sealed class ChineseFontAtlas : IDisposable {

            private sealed class Page {
                public Texture2D Texture { get; }
                public IReadOnlyList<BitmapFontRegion> Regions { get; }
                public HashSet<char> Characters { get; }
                public bool IncludesBaseCharacters { get; }
                public long LastUsedTicks { get; set; }

                public Page(Texture2D texture, IReadOnlyList<BitmapFontRegion> regions, HashSet<char> characters, bool includesBaseCharacters) {
                    this.Texture                = texture;
                    this.Regions                = regions;
                    this.Characters             = characters;
                    this.IncludesBaseCharacters = includesBaseCharacters;
                    this.LastUsedTicks          = DateTime.UtcNow.Ticks;
                }
            }

            private sealed class BakedPage {
                public TtfFontBakerResult Result { get; }
                public Color[] Pixels { get; }
                public HashSet<char> RequestedCharacters { get; }
                public bool IncludesBaseCharacters { get; }

                public BakedPage(TtfFontBakerResult result, Color[] pixels, HashSet<char> requestedCharacters, bool includesBaseCharacters) {
                    this.Result                 = result;
                    this.Pixels                 = pixels;
                    this.RequestedCharacters    = requestedCharacters;
                    this.IncludesBaseCharacters = includesBaseCharacters;
                }
            }

            private readonly object          _sync = new object();
            private readonly string          _key;
            private readonly string          _fontPath;
            private readonly int             _fontSize;
            private readonly FontFace        _face;
            private readonly FontSize        _size;
            private readonly FontStyle       _style;
            private readonly List<Page>      _pages = new List<Page>();
            private readonly HashSet<char>   _pendingCharacters = new HashSet<char>();
            private readonly HashSet<char>   _bakedCharacters = new HashSet<char>();

            private byte[]       _fontData;
            private BitmapFont   _fallbackFont;
            private BitmapFont   _currentFont;
            private bool         _baseCharactersBaked;
            private bool         _buildInProgress;
            private bool         _disposed;
            private int          _generation;
            private long         _lastUsedTicks = DateTime.UtcNow.Ticks;

            public long LastUsedTicks => Interlocked.Read(ref _lastUsedTicks);

            public bool CanEvict {
                get {
                    lock (_sync) {
                        return !_buildInProgress && _pages.Count > 0;
                    }
                }
            }

            public BitmapFont CurrentFont {
                get {
                    Touch();
                    lock (_sync) {
                        return _currentFont ?? _fallbackFont;
                    }
                }
            }

            public ChineseFontAtlas(string key, string fontPath, FontFace face, FontSize size, FontStyle style) {
                _key      = key;
                _fontPath = fontPath;
                _fontSize = (int)size + 2;
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

            public void Touch() {
                Interlocked.Exchange(ref _lastUsedTicks, DateTime.UtcNow.Ticks);
            }

            public void RequestCharacters(IEnumerable<char> characters) {
                if (characters == null) return;

                lock (_sync) {
                    if (_disposed) return;

                    long now = DateTime.UtcNow.Ticks;

                    foreach (char character in characters.Where(IsChineseFontCharacter).Distinct()) {
                        var page = _pages.FirstOrDefault(candidate => candidate.Characters.Contains(character));

                        if (page != null) {
                            page.LastUsedTicks = now;
                        } else if (!_bakedCharacters.Contains(character)) {
                            _pendingCharacters.Add(character);
                        }
                    }
                }

                Touch();
                StartBuildIfNeeded();
            }

            public long EvictAllPages() {
                List<Texture2D> textures;
                long freedBytes;

                lock (_sync) {
                    if (_buildInProgress || _pages.Count == 0) return 0;

                    textures = _pages.Select(page => page.Texture).ToList();
                    freedBytes = textures.Count * CJK_PAGE_TEXTURE_BYTES;

                    _pages.Clear();
                    _bakedCharacters.Clear();
                    _baseCharactersBaked = false;
                    _currentFont = _fallbackFont;
                    _generation++;
                }

                DisposeTexturesAfterGracePeriod(textures);
                Logger.Debug("Evicted {pageCount} Chinese font pages for inactive atlas {fontName}.", textures.Count, _key);
                return freedBytes;
            }

            private void StartBuildIfNeeded() {
                HashSet<char> requestedCharacters;
                bool includeBaseCharacters;

                lock (_sync) {
                    if (_disposed || _buildInProgress || (_baseCharactersBaked && _pendingCharacters.Count == 0)) {
                        return;
                    }

                    includeBaseCharacters = !_baseCharactersBaked;
                    requestedCharacters = new HashSet<char>(_pendingCharacters.Take(CJK_PAGE_CHARACTER_LIMIT));
                    _pendingCharacters.ExceptWith(requestedCharacters);
                    _buildInProgress = true;
                }

                Task.Run(async () => {
                    // Coalesce labels created in the same UI update into one page.
                    await Task.Delay(50).ConfigureAwait(false);
                    await _chineseFontBakeLock.WaitAsync().ConfigureAwait(false);

                    try {
                        if (_fontData == null) {
                            int collectionFaceIndex = Path.GetExtension(_fontPath).Equals(".ttc", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                            _fontData = GetTtfBakerFontData(_fontPath, collectionFaceIndex);
                        }

                        var ranges = new List<CharacterRange>();

                        if (includeBaseCharacters) {
                            ranges.AddRange(FontUtil.GetRanges(Gw2FontRanges.Default));
                        }

                        ranges.AddRange(GetCharacterRanges(requestedCharacters));

                        var result = TtfFontBaker.Bake(_fontData,
                                                       _fontSize,
                                                       CJK_PAGE_TEXTURE_SIZE,
                                                       CJK_PAGE_TEXTURE_SIZE,
                                                       ranges);
                        var pixels = new Color[result.Width * result.Height];

                        for (int i = 0; i < result.Pixels.Length; i++) {
                            byte value = result.Pixels[i];
                            pixels[i] = new Color(value, value, value, value);
                        }

                        var bakedPage = new BakedPage(result, pixels, requestedCharacters, includeBaseCharacters);
                        GameService.Graphics.QueueMainThreadRender(graphicsDevice => CompleteBuild(graphicsDevice, bakedPage));
                    } catch (Exception ex) {
                        FailBuild(requestedCharacters, ex);
                    } finally {
                        _chineseFontBakeLock.Release();
                    }
                });
            }

            private void CompleteBuild(GraphicsDevice graphicsDevice, BakedPage bakedPage) {
                lock (_sync) {
                    if (_disposed) {
                        _buildInProgress = false;
                        return;
                    }
                }

                if (!TryReserveChineseFontPage(this)) {
                    lock (_sync) {
                        _pendingCharacters.UnionWith(bakedPage.RequestedCharacters);
                        _buildInProgress = false;
                    }

                    Logger.Warn("Chinese font texture budget reached. Delaying page for {fontName}.", _key);
                    return;
                }

                var texture = new Texture2D(graphicsDevice, bakedPage.Result.Width, bakedPage.Result.Height);
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
                List<Texture2D> evictedTextures = null;

                lock (_sync) {
                    if (_pages.Count >= CJK_MAX_PAGES_PER_FONT) {
                        Page evictedPage = _pages.Where(candidate => !candidate.IncludesBaseCharacters)
                                                 .OrderBy(candidate => candidate.LastUsedTicks)
                                                 .FirstOrDefault()
                                           ?? _pages.OrderBy(candidate => candidate.LastUsedTicks).First();
                        _pages.Remove(evictedPage);
                        _bakedCharacters.ExceptWith(evictedPage.Characters);
                        _baseCharactersBaked &= !evictedPage.IncludesBaseCharacters;
                        evictedTextures = new List<Texture2D> { evictedPage.Texture };
                        Interlocked.Add(ref _chineseFontTextureBytes, -CJK_PAGE_TEXTURE_BYTES);
                    }

                    _pages.Add(page);
                    _bakedCharacters.UnionWith(page.Characters);
                    _baseCharactersBaked |= bakedPage.IncludesBaseCharacters;
                    _buildInProgress = false;
                    _generation++;

                    var allRegions = _pages.SelectMany(candidate => candidate.Regions).ToList();
                    int lineHeight = (int)Math.Ceiling(bakedPage.Result.FontFontPixelHeight);
                    var font = new PagedBitmapFont($"{_key}-g{_generation}", allRegions, lineHeight, _generation) {
                        LetterSpacing = 0
                    };

                    _currentFont = font;
                    _chineseFontRequests.TryAdd(font, new ChineseFontRequest(_face, _size, _style, _key));
                }

                if (evictedTextures != null) {
                    DisposeTexturesAfterGracePeriod(evictedTextures);
                }

                Logger.Debug("Built Chinese font page for {fontName}: {glyphCount} glyphs, {pageCount} resident pages, {textureMegabytes} MB total.",
                             _key,
                             regions.Count,
                             _pages.Count,
                             Interlocked.Read(ref _chineseFontTextureBytes) / (1024 * 1024));

                StartBuildIfNeeded();
            }

            private void FailBuild(HashSet<char> requestedCharacters, Exception exception) {
                lock (_sync) {
                    _pendingCharacters.UnionWith(requestedCharacters);
                    _buildInProgress = false;
                }

                Logger.Warn(exception, "Unable to build Chinese font page for {fontName}.", _key);
            }

            public void Dispose() {
                List<Texture2D> textures;

                lock (_sync) {
                    if (_disposed) return;
                    _disposed = true;
                    textures = _pages.Select(page => page.Texture).ToList();
                    _pages.Clear();
                }

                Interlocked.Add(ref _chineseFontTextureBytes, -textures.Count * CJK_PAGE_TEXTURE_BYTES);

                foreach (var texture in textures) {
                    texture.Dispose();
                }
            }

        }

        private static void DisposeTexturesAfterGracePeriod(IEnumerable<Texture2D> textures) {
            var textureList = textures.ToList();

            Task.Run(async () => {
                await Task.Delay(TimeSpan.FromSeconds(CJK_EVICTION_GRACE_SECONDS)).ConfigureAwait(false);
                GameService.Graphics.QueueMainThreadRender(_ => {
                    foreach (var texture in textureList) {
                        texture.Dispose();
                    }
                });
            });
        }

        private static bool TryReserveChineseFontPage(ChineseFontAtlas requester) {
            while (Interlocked.Read(ref _chineseFontTextureBytes) + CJK_PAGE_TEXTURE_BYTES > CJK_GLOBAL_TEXTURE_BUDGET) {
                var evictionCandidate = _chineseFontAtlases.Values
                                                          .Where(atlas => atlas != requester && atlas.CanEvict)
                                                          .OrderBy(atlas => atlas.LastUsedTicks)
                                                          .FirstOrDefault();

                if (evictionCandidate == null) {
                    return false;
                }

                long freedBytes = evictionCandidate.EvictAllPages();
                if (freedBytes <= 0) return false;
                Interlocked.Add(ref _chineseFontTextureBytes, -freedBytes);
            }

            Interlocked.Add(ref _chineseFontTextureBytes, CJK_PAGE_TEXTURE_BYTES);
            return true;
        }

        private BitmapFont GetChineseFont(FontFace font, FontSize size, FontStyle style) {
            string fontPath = GetChineseFontFilePath(style);

            if (fontPath == null) {
                Logger.Warn("Unable to find a Windows CJK font. Falling back to {fontFace}.", font);
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
            string fontPath = GetChineseFontFilePath(style);
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
