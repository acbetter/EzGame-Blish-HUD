using System;
using System.Collections.Generic;
using Blish_HUD;

internal static class Program {

    private static int Main() {
        var failures = new List<string>();

        const string sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        ExpectEqual("package repository", "https://ezgame.cc/packages/blish/", EzGameDistribution.PackageRepositoryUrl, failures);
        ExpectEqual("spoiled modules", "https://pkgs.blishhud.com/spoiled.json", EzGameDistribution.SpoiledModulesUrl, failures);
        ExpectEqual("core versions", "https://ezgame.cc/packages/blish/versions.json?cv={0}", EzGameDistribution.CoreVersionsUrl, failures);

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

        if (failures.Count == 0) {
            Console.WriteLine("EzGame distribution endpoint contract tests passed.");
            return 0;
        }

        foreach (string failure in failures) Console.Error.WriteLine(failure);
        return 1;
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
