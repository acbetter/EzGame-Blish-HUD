using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Modules;
using Flurl.Http.Testing;

internal static class Program {

    private static async Task<int> Main() {
        var failures = new List<string>();

        const string sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        ExpectEqual("package repository", "https://ezgame.cc/packages/blish/", EzGameDistribution.PackageRepositoryUrl, failures);
        ExpectEqual("spoiled modules", "https://ezgame.cc/packages/blish/spoiled.json", EzGameDistribution.SpoiledModulesUrl, failures);
        ExpectEqual("core versions", "https://ezgame.cc/packages/blish/versions.json?cv={0}", EzGameDistribution.CoreVersionsUrl, failures);
        ExpectEqual("festivals", "https://ezgame.cc/packages/blish/general/getactivefestivals", EzGameDistribution.ActiveFestivalsUrl, failures);
        ExpectEqual("build information", "https://ezgame.cc/packages/blish/general/getbuildinfo", EzGameDistribution.BuildInfoUrl, failures);

        ExpectArtifactUrl($"https://ezgame.cc/packages/blish/artifacts/{sha256}/module.bhm", true, failures);
        ExpectArtifactUrl($"https://EZGAME.CC/packages/blish/artifacts/{sha256}/Blish.HUD.zip?download=1", true, failures);
        ExpectArtifactUrl("https://ezgame.cc/packages/blish/packages.gz", false, failures);
        ExpectArtifactUrl("https://ezgame.cc/packages/blish/artifacts/module.bhm", false, failures);
        ExpectArtifactUrl("https://ezgame.cc/packages/blish/artifacts/", false, failures);
        ExpectArtifactUrl($"https://ezgame.cc/packages/blish/artifacts/{sha256}/nested/module.bhm", false, failures);
        ExpectArtifactUrl($"https://ezgame.cc/packages/blish/artifacts/{sha256}%2Fother/module.bhm", false, failures);
        ExpectArtifactUrl($"https://ezgame.cc/packages/blish/artifacts/{sha256}/%2e%2e", false, failures);
        ExpectArtifactUrl("https://ezgame.cc/packages/blish/artifacts/not-a-sha256/module.bhm", false, failures);
        ExpectArtifactUrl($"https://ezgame.cc/packages/pathing/artifacts/{sha256}/module.bhm", false, failures);
        ExpectArtifactUrl($"https://blish.ezgame.cc/artifacts/{sha256}/module.bhm", false, failures);
        ExpectArtifactUrl($"https://ezgame.cc.evil.example/packages/blish/artifacts/{sha256}/module.bhm", false, failures);
        ExpectArtifactUrl($"http://ezgame.cc/packages/blish/artifacts/{sha256}/module.bhm", false, failures);
        ExpectArtifactUrl($"https://ezgame.cc:8443/packages/blish/artifacts/{sha256}/module.bhm", false, failures);
        ExpectArtifactUrl("not a URL", false, failures);

        await CheckSpoiledModuleCache(failures);

        if (failures.Count == 0) {
            Console.WriteLine("EzGame distribution endpoint contract tests passed.");
            return 0;
        }

        foreach (string failure in failures) Console.Error.WriteLine(failure);
        return 1;
    }

    private static async Task CheckSpoiledModuleCache(ICollection<string> failures) {
        string directory = Path.Combine(Path.GetTempPath(), "blish-spoiled-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "spoiled.json");
        try {
            File.WriteAllText(path, "[\"cached.module_1.0.0\"]");
            var list = new SpoiledModuleList(path);
            list.LoadCached();
            if (!list.Contains("cached.module_1.0.0")) failures.Add("Cached list unavailable before network refresh.");

            using (var http = new HttpTest()) {
                var entered = new TaskCompletionSource<bool>();
                var release = new TaskCompletionSource<bool>();
                http.Configure(settings => settings.BeforeCallAsync = _ => {
                    entered.TrySetResult(true);
                    return release.Task;
                });
                http.RespondWithJson(new[] { "fresh.module_2.0.0" });
                var refresh = list.RefreshAsync(CancellationToken.None);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (refresh.IsCompleted || !list.Contains("cached.module_1.0.0"))
                    failures.Add("Pending refresh must leave the cached list available.");
                release.SetResult(true);
                await refresh;
                http.ShouldHaveCalled(EzGameDistribution.SpoiledModulesUrl).Times(1);
            }
            var restored = new SpoiledModuleList(path);
            restored.LoadCached();
            if (!list.Contains("fresh.module_2.0.0") || !restored.Contains("fresh.module_2.0.0") || list.Contains("cached.module_1.0.0"))
                failures.Add("Refresh must replace both the live snapshot and the next startup's cache.");

            foreach (bool timeout in new[] { true, false }) {
                using var http = new HttpTest();
                if (timeout) http.SimulateTimeout();
                else http.RespondWith("[1]");
                await list.RefreshAsync(CancellationToken.None);
                restored.LoadCached();
                if (!list.Contains("fresh.module_2.0.0") || !restored.Contains("fresh.module_2.0.0"))
                    failures.Add("Timeout or invalid JSON must preserve the last valid list.");
            }
            using (var http = new HttpTest()) {
                using var cancellation = new CancellationTokenSource();
                var entered = new TaskCompletionSource<bool>();
                http.Configure(settings => settings.BeforeCallAsync = _ => {
                    entered.TrySetResult(true);
                    return Task.Delay(Timeout.Infinite, cancellation.Token);
                });
                var refresh = list.RefreshAsync(cancellation.Token);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                cancellation.Cancel();
                await refresh.WaitAsync(TimeSpan.FromSeconds(5));
                if (!list.Contains("fresh.module_2.0.0")) failures.Add("Cancellation must preserve the cached list.");
            }
            File.WriteAllText(path, "invalid JSON");
            new SpoiledModuleList(path).LoadCached();
            var empty = new SpoiledModuleList(Path.Combine(directory, "missing.json"));
            empty.LoadCached();
            if (empty.Contains("fresh.module_2.0.0")) failures.Add("First startup without a cache must use an empty list.");
        } finally {
            Directory.Delete(directory, true);
        }
    }

    private static void ExpectEqual(string name, string expected, string actual, ICollection<string> failures) {
        if (!string.Equals(expected, actual, StringComparison.Ordinal)) {
            failures.Add($"{name}: expected '{expected}', got '{actual}'.");
        }
    }

    private static void ExpectArtifactUrl(string value, bool expected, ICollection<string> failures) {
        bool actual = EzGameDistribution.IsArtifactUrl(value);
        if (actual != expected) failures.Add($"artifact URL '{value}': expected {expected}, got {actual}.");
    }

}

namespace Blish_HUD {
    internal sealed class Logger {
        public static Logger GetLogger<T>() => new Logger();
        public void Warn(Exception exception, string message) { }
    }
}
