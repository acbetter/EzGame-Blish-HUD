using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace Blish_HUD.Licensing {

    internal static class ExpirationGate {

        private const string WebsiteUrl = "https://ezgame.cc";

        // 2026-09-01 00:00:00 in China Standard Time.
        private static readonly DateTime ExpiresAtUtc =
            new DateTime(2026, 8, 31, 16, 0, 0, DateTimeKind.Utc);

        public static bool AllowStartup() {
            if (!NtpClient.TryGetUtcNow(out DateTime trustedUtcNow)) {
                ShowFailure(
                    "EzGame 时间校验失败",
                    "无法连接时间校验服务器。\r\n\r\n" +
                    "请检查网络连接，并确认防火墙没有拦截 UDP 123 端口。\r\n\r\n" +
                    "程序即将退出，详情请访问 https://ezgame.cc。");
                return false;
            }

            if (trustedUtcNow >= ExpiresAtUtc) {
                ShowFailure(
                    "EzGame 版本已到期",
                    "此版本的使用期限已于 2026 年 8 月 31 日结束。\r\n\r\n" +
                    "请前往 https://ezgame.cc 获取新版本。");
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
