using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Flurl.Http;
using Newtonsoft.Json.Linq;

namespace Blish_HUD.Modules {
    // Readers use an immutable snapshot; refreshing never waits on the UI thread.
    internal sealed class SpoiledModuleList {
        private static readonly Logger Logger = Logger.GetLogger<SpoiledModuleList>();
        private readonly string _cachePath;
        private volatile HashSet<string> _identifiers = new HashSet<string>();

        public SpoiledModuleList(string cachePath) {
            _cachePath = cachePath;
        }

        public bool Contains(string identifier) => _identifiers.Contains(identifier);

        public void LoadCached() {
            try {
                if (File.Exists(_cachePath)) _identifiers = Parse(File.ReadAllText(_cachePath));
            } catch (Exception ex) {
                Logger.Warn(ex, "Unable to read the cached spoiled modules list.");
            }
        }

        public async Task RefreshAsync(CancellationToken cancellationToken) {
            try {
                string json = await EzGameDistribution.SpoiledModulesUrl.WithTimeout(5)
                    .GetStringAsync(cancellationToken).ConfigureAwait(false);
                var identifiers = Parse(json);
                cancellationToken.ThrowIfCancellationRequested();
                _identifiers = identifiers;

                // Keep the last complete file if a write is interrupted.
                string temporaryPath = _cachePath + ".tmp";
                File.WriteAllText(temporaryPath, json);
                if (File.Exists(_cachePath)) File.Replace(temporaryPath, _cachePath, null);
                else File.Move(temporaryPath, _cachePath);
            } catch (Exception) when (cancellationToken.IsCancellationRequested) {
                // Shutdown cancels the request without delaying exit.
            } catch (Exception ex) {
                Logger.Warn(ex, "Unable to refresh the spoiled modules list; keeping the last available list.");
            }
        }

        private static HashSet<string> Parse(string json) {
            var array = JArray.Parse(json);
            if (array.Any(item => item.Type != JTokenType.String))
                throw new InvalidDataException("The spoiled modules list must be an array of strings.");
            return new HashSet<string>(array.Select(item => item.Value<string>()));
        }
    }
}
