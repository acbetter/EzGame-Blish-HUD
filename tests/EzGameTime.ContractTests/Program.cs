using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Blish_HUD.Licensing;
using Flurl.Http.Testing;

internal static class Program {
    private static void Main() {
        using (var http = new HttpTest()) {
            var serverTime = new DateTime(2026, 11, 30, 16, 0, 0, DateTimeKind.Utc);
            http.RespondWithJson(new { utcNow = serverTime.ToString("O") });
            if (EzGameTimeClient.GetUtcNow() != serverTime)
                throw new Exception("Server time must take precedence, including at expiry.");
            http.ShouldHaveCalled("https://ezgame.cc/api/blish/time").Times(1);
        }

        CheckLocalFallback(http => http.SimulateTimeout());
        CheckLocalFallback(http => http.RespondWith("unavailable", 503));
        CheckLocalFallback(http => http.RespondWithJson(new { utcNow = "invalid" }));
        CheckLocalFallback(http => http.RespondWithJson(new { }));

        using (var http = new HttpTest()) {
            var release = new TaskCompletionSource<bool>();
            http.Configure(settings => settings.BeforeCallAsync = _ => release.Task);
            var stopwatch = Stopwatch.StartNew();
            try {
                CheckLocalTime(EzGameTimeClient.GetUtcNow(), DateTime.UtcNow.AddSeconds(-4));
                if (stopwatch.Elapsed.TotalSeconds < 2.8 || stopwatch.Elapsed.TotalSeconds > 4.5)
                    throw new Exception("A hanging request must stop delaying startup after 3 seconds.");
            } finally {
                release.SetResult(true);
            }
        }
        Console.WriteLine("PASS: server time takes precedence, timeout, HTTP error, invalid/missing time and 3-second startup limit.");
    }

    private static void CheckLocalFallback(Action<HttpTest> setup) {
        using (var http = new HttpTest()) {
            setup(http);
            DateTime before = DateTime.UtcNow;
            CheckLocalTime(EzGameTimeClient.GetUtcNow(), before);
            http.ShouldHaveCalled("https://ezgame.cc/api/blish/time").Times(1);
        }
    }

    private static void CheckLocalTime(DateTime result, DateTime before) {
        if (result.Kind != DateTimeKind.Utc || result < before || result > DateTime.UtcNow)
            throw new Exception("Failed time requests must use the computer's current UTC time.");
    }
}
