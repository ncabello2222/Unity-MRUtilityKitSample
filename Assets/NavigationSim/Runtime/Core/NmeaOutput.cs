using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NavigationSim.Core
{
    /// <summary>
    /// Optional NMEA-0183 UDP emitter (RMC/GGA/HDT) for external plotters such as OpenCPN.
    /// Disabled by default; enable from the instructor panel.
    /// </summary>
    public sealed class NmeaOutput : IDisposable
    {
        private UdpClient _udp;
        private IPEndPoint _endpoint;
        private double _accum;
        private bool _enabled;

        public bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                if (!_enabled)
                {
                    DisposeSocket();
                }
            }
        }

        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 10110;
        public double IntervalS { get; set; } = 1.0;

        /// <summary>Fixes emitted since start. Lets the panel show whether it is alive.</summary>
        public int SentCount { get; private set; }

        public void Update(double dt, ShipState state, GeoDatum geo)
        {
            if (!_enabled || state == null || geo == null)
            {
                return;
            }

            _accum += dt;
            if (_accum < IntervalS)
            {
                return;
            }

            // Subtract the interval instead of zeroing: dropping the remainder loses up to
            // one frame per fix, which drifts the rate a percent or so below the nominal
            // 1 Hz. Guard against a long hitch turning into a burst.
            _accum -= IntervalS;
            if (_accum > IntervalS)
            {
                _accum = 0.0;
            }

            SentCount++;

            EnsureSocket();
            if (_udp == null)
            {
                return;
            }

            foreach (string s in BuildSentences(state, geo))
            {
                byte[] bytes = Encoding.ASCII.GetBytes(s + "\r\n");
                try
                {
                    _udp.Send(bytes, bytes.Length, _endpoint);
                }
                catch
                {
                    // Best-effort training aid — never break the sim loop.
                }
            }
        }

        /// <summary>
        /// The RMC/GGA/HDT trio for the current fix. Public so the sentences can be
        /// checked without opening a socket.
        /// </summary>
        public List<string> BuildSentences(ShipState state, GeoDatum geo)
        {
            geo.ToLatLon(state.North, state.East, out double lat, out double lon);
            return new List<string>
            {
                BuildRmc(state, lat, lon),
                BuildGga(state, lat, lon),
                BuildHdt(state.HeadingDeg)
            };
        }

        private void EnsureSocket()
        {
            if (_udp != null)
            {
                return;
            }

            try
            {
                _udp = new UdpClient();
                _endpoint = new IPEndPoint(IPAddress.Parse(Host), Port);
            }
            catch
            {
                DisposeSocket();
            }
        }

        private void DisposeSocket()
        {
            _udp?.Dispose();
            _udp = null;
        }

        public void Dispose()
        {
            DisposeSocket();
        }

        /// <summary>
        /// UTC field from the exercise clock. A frozen 000000.00 made every fix look
        /// simultaneous, which plotters treat as a stuck receiver.
        /// </summary>
        private static string TimeField(ShipState s)
        {
            double secondsOfDay = s.TimeS % 86400.0;
            if (secondsOfDay < 0.0)
            {
                secondsOfDay += 86400.0;
            }

            int h = (int)(secondsOfDay / 3600.0);
            int m = (int)(secondsOfDay % 3600.0 / 60.0);
            double sec = secondsOfDay % 60.0;
            return $"{h:00}{m:00}{sec.ToString("00.00", CultureInfo.InvariantCulture)}";
        }

        private static string BuildRmc(ShipState s, double lat, double lon)
        {
            FormatLatLon(lat, lon, out string latStr, out char ns, out string lonStr, out char ew);
            double sogKn = s.SogMs * 1.94384449244;
            string body =
                $"GPRMC,{TimeField(s)},A,{latStr},{ns},{lonStr},{ew},{sogKn.ToString("0.0", CultureInfo.InvariantCulture)},{s.CogDeg.ToString("0.0", CultureInfo.InvariantCulture)},010101,,,A";
            return "$" + body + "*" + Checksum(body);
        }

        private static string BuildGga(ShipState s, double lat, double lon)
        {
            FormatLatLon(lat, lon, out string latStr, out char ns, out string lonStr, out char ew);
            string body =
                $"GPGGA,{TimeField(s)},{latStr},{ns},{lonStr},{ew},1,08,1.0,0.0,M,0.0,M,,";
            return "$" + body + "*" + Checksum(body);
        }

        private static string BuildHdt(double headingDeg)
        {
            string body = $"GPHDT,{headingDeg.ToString("0.0", CultureInfo.InvariantCulture)},T";
            return "$" + body + "*" + Checksum(body);
        }

        private static void FormatLatLon(double lat, double lon,
            out string latStr, out char ns, out string lonStr, out char ew)
        {
            ns = lat >= 0 ? 'N' : 'S';
            ew = lon >= 0 ? 'E' : 'W';
            double alat = Math.Abs(lat);
            double alon = Math.Abs(lon);
            int latD = (int)alat;
            int lonD = (int)alon;
            double latM = (alat - latD) * 60.0;
            double lonM = (alon - lonD) * 60.0;
            latStr = $"{latD:00}{latM.ToString("00.000", CultureInfo.InvariantCulture)}";
            lonStr = $"{lonD:000}{lonM.ToString("00.000", CultureInfo.InvariantCulture)}";
        }

        private static string Checksum(string body)
        {
            int cs = 0;
            for (int i = 0; i < body.Length; i++)
            {
                cs ^= body[i];
            }

            return cs.ToString("X2");
        }
    }
}
