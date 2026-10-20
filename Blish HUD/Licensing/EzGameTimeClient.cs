using System;
using System.Threading;
using System.Threading.Tasks;
using Flurl.Http;

namespace Blish_HUD.Licensing {
    internal static class EzGameTimeClient {
        private const string TimeUrl = "https://ezgame.cc/api/blish/time";
        private const int TimeoutMilliseconds = 3000;

        public static DateTime GetUtcNow() {
            using (var cancellation = new CancellationTokenSource()) {
                // Bound startup even when DNS or an HTTP handler delays cancellation.
                CancellationToken token = cancellation.Token;
                var request = Task.Run(() => QueryAsync(token));
                var completed = Task.WhenAny(request, Task.Delay(TimeoutMilliseconds)).GetAwaiter().GetResult();
                if (completed == request) {
                    DateTime? serverTime = request.GetAwaiter().GetResult();
                    if (serverTime.HasValue) return serverTime.Value;
                }
                cancellation.Cancel();
                return DateTime.UtcNow;
            }
        }

        private static async Task<DateTime?> QueryAsync(CancellationToken cancellationToken) {
            try {
                var response = await TimeUrl.WithTimeout(TimeSpan.FromMilliseconds(TimeoutMilliseconds))
                    .GetJsonAsync<TimeResponse>(cancellationToken).ConfigureAwait(false);
                return response?.UtcNow?.UtcDateTime;
            } catch (Exception) {
                // An unavailable or invalid time response must not prevent startup.
                return null;
            }
        }

        private sealed class TimeResponse {
            public DateTimeOffset? UtcNow { get; set; }
        }
    }
}
