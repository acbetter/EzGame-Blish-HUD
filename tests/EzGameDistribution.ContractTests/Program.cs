using System;
using System.Collections.Generic;
using Blish_HUD;

internal static class Program {

    private static int Main() {
        var failures = new List<string>();

        ExpectEqual("package repository", "https://blish.ezgame.cc/", EzGameDistribution.PackageRepositoryUrl, failures);
        ExpectEqual("spoiled modules", "https://pkgs.blishhud.com/spoiled.json", EzGameDistribution.SpoiledModulesUrl, failures);
        ExpectEqual("core versions", "https://blish.ezgame.cc/versions.json?cv={0}", EzGameDistribution.CoreVersionsUrl, failures);

        ExpectArtifactUrl("https://blish.ezgame.cc/artifacts/module-key/module.bhm", true, failures);
        ExpectArtifactUrl("https://BLISH.EZGAME.CC/artifacts/core-key/Blish.HUD.zip?download=1", true, failures);
        ExpectArtifactUrl("https://blish.ezgame.cc/packages.gz", false, failures);
        ExpectArtifactUrl("https://blish.ezgame.cc/artifacts/module.bhm", false, failures);
        ExpectArtifactUrl("https://blish.ezgame.cc/artifacts/", false, failures);
        ExpectArtifactUrl("https://blish.ezgame.cc/artifacts/key/nested/module.bhm", false, failures);
        ExpectArtifactUrl("https://blish.ezgame.cc/artifacts/key%2Fother/module.bhm", false, failures);
        ExpectArtifactUrl("https://blish.ezgame.cc/artifacts/key/%2e%2e", false, failures);
        ExpectArtifactUrl("https://ezgame.cc/artifacts/module-key/module.bhm", false, failures);
        ExpectArtifactUrl("https://blish.ezgame.cc.evil.example/artifacts/module-key/module.bhm", false, failures);
        ExpectArtifactUrl("http://blish.ezgame.cc/artifacts/module-key/module.bhm", false, failures);
        ExpectArtifactUrl("https://blish.ezgame.cc:8443/artifacts/module-key/module.bhm", false, failures);
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
