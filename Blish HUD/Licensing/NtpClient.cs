using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Blish_HUD.Licensing {

    internal static class NtpClient {

        private const int NTP_PORT = 123;
        private const int NTP_PACKET_LENGTH = 48;
        private const int REQUEST_TIMEOUT_MS = 2000;
        private const int TOTAL_TIMEOUT_MS = 5000;

        private static readonly string[] Servers = {
            "ntp.tencent.com",
            "ntp.aliyun.com",
            "time.cloudflare.com"
        };

        private static readonly DateTime NtpEpoch =
            new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static bool TryGetUtcNow(out DateTime utcNow) {
            var requests = Servers.Select(server => Task.Run(() => QuerySafe(server))).ToArray();

            try {
                Task.WaitAll(requests, TOTAL_TIMEOUT_MS);
            } catch (AggregateException) {
                // QuerySafe handles individual failures. This only protects startup from
                // an unexpected task failure.
            }

            List<DateTime> results = requests
                                    .Where(request => request.Status == TaskStatus.RanToCompletion
                                                   && request.Result.HasValue)
                                    .Select(request => request.Result.Value)
                                    .OrderBy(result => result)
                                    .ToList();

            if (results.Count == 0) {
                utcNow = default;
                return false;
            }

            // Any one valid response is enough to allow startup.
            utcNow = results[0];
            return true;
        }

        private static DateTime? QuerySafe(string server) {
            try {
                return Query(server);
            } catch {
                return null;
            }
        }

        private static DateTime Query(string server) {
            byte[] request = new byte[NTP_PACKET_LENGTH];
            request[0] = 0x23; // Leap indicator 0, NTP v4, client mode.

            using (var client = new UdpClient()) {
                client.Client.ReceiveTimeout = REQUEST_TIMEOUT_MS;
                client.Connect(server, NTP_PORT);

                var stopwatch = Stopwatch.StartNew();
                client.Send(request, request.Length);
                IPEndPoint remoteEndpoint = null;
                byte[] response = client.Receive(ref remoteEndpoint);
                stopwatch.Stop();

                ValidateResponse(response);

                uint seconds = ReadUInt32BigEndian(response, 40);
                uint fraction = ReadUInt32BigEndian(response, 44);

                double fractionSeconds = fraction / 4294967296d;
                DateTime serverTransmitTime = NtpEpoch.AddSeconds(seconds + fractionSeconds);

                // The transmit timestamp is generated shortly before the packet is sent.
                return serverTransmitTime.AddTicks(stopwatch.Elapsed.Ticks / 2);
            }
        }

        private static void ValidateResponse(byte[] response) {
            if (response == null || response.Length < NTP_PACKET_LENGTH) {
                throw new InvalidOperationException("The NTP response was incomplete.");
            }

            int leapIndicator = (response[0] >> 6) & 0x03;
            int mode = response[0] & 0x07;
            int stratum = response[1];

            if (leapIndicator == 3 || mode != 4 || stratum < 1 || stratum > 15) {
                throw new InvalidOperationException("The NTP response was not synchronized.");
            }

            if (ReadUInt32BigEndian(response, 40) == 0) {
                throw new InvalidOperationException("The NTP response did not contain a timestamp.");
            }
        }

        private static uint ReadUInt32BigEndian(byte[] buffer, int offset) {
            return ((uint)buffer[offset] << 24)
                 | ((uint)buffer[offset + 1] << 16)
                 | ((uint)buffer[offset + 2] << 8)
                 | buffer[offset + 3];
        }

    }
}
