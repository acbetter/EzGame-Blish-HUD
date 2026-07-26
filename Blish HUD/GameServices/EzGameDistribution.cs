using System;

namespace Blish_HUD {
    internal static class EzGameDistribution {

        public const string PackageRepositoryUrl = "https://ezgame.cc/packages/blish/";
        public const string SpoiledModulesUrl    = "https://pkgs.blishhud.com/spoiled.json";
        public const string CoreVersionsUrl      = PackageRepositoryUrl + "versions.json?cv={0}";

        private const string ARTIFACT_PATH_PREFIX = "/packages/blish/artifacts/";

        public static bool IsArtifactUrl(string value) {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(uri.Host, "ezgame.cc", StringComparison.OrdinalIgnoreCase)
                || !uri.IsDefaultPort
                || !string.IsNullOrEmpty(uri.UserInfo)) {
                return false;
            }

            string artifactPath;
            try {
                artifactPath = Uri.UnescapeDataString(uri.AbsolutePath);
            } catch (UriFormatException) {
                return false;
            }

            if (!artifactPath.StartsWith(ARTIFACT_PATH_PREFIX, StringComparison.Ordinal)) return false;

            string[] artifactPathParts = artifactPath.Substring(ARTIFACT_PATH_PREFIX.Length).Split('/');

            return artifactPathParts.Length == 2
                && IsSha256(artifactPathParts[0])
                && IsSafePathSegment(artifactPathParts[1]);
        }

        private static bool IsSha256(string value) {
            if (value == null || value.Length != 64) return false;

            foreach (char c in value) {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            }

            return true;
        }

        private static bool IsSafePathSegment(string value) {
            return !string.IsNullOrWhiteSpace(value)
                && !string.Equals(value, ".", StringComparison.Ordinal)
                && !string.Equals(value, "..", StringComparison.Ordinal)
                && value.IndexOf('\\') < 0;
        }

    }
}
