using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Forms;

namespace Blish_HUD.Licensing {

    internal static class ExpirationGate {

        private const string WebsiteUrl = "https://ezgame.cc/guides/ezgame-blish-hud-pathing-guide";

        // Valid through 2026-11-30; expires 2026-12-01 00:00:00 in China Standard Time.
        private static readonly DateTime ExpiresAtUtc =
            new DateTime(2026, 11, 30, 16, 0, 0, DateTimeKind.Utc);

        internal static string ValidThroughDateText => ExpiresAtUtc.AddHours(8).AddTicks(-1)
            .ToString("yyyy 年 M 月 d 日", CultureInfo.InvariantCulture);

        public static bool AllowStartup() {
            DateTime trustedUtcNow = EzGameTimeClient.GetUtcNow();

            if (trustedUtcNow >= ExpiresAtUtc) {
                ShowFailure(
                    "EzGame 版本已到期",
                    $"此版本已于北京时间 {ExpiresAtUtc.AddHours(8).ToString("yyyy 年 M 月 d 日 HH:mm", CultureInfo.InvariantCulture)} 到期（有效期至 {ValidThroughDateText}）。\r\n\r\n" +
                    $"点击“确定”将在浏览器中打开更新页面：\r\n{WebsiteUrl}");
                return false;
            }

            return true;
        }

        private static void ShowFailure(string title, string message) {
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);

            try {
                Process.Start(new ProcessStartInfo {
                    FileName = WebsiteUrl,
                    UseShellExecute = true
                });
            } catch {
                // Failure to launch the browser must not prevent the application from exiting.
            }
        }
    }
}
