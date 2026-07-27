using System;

namespace NavigationSim.Core
{
    /// <summary>
    /// Local NED metres ↔ WGS84 lat/lon around a fixed origin.
    /// Flat-earth approximation is fine for harbour-scale scenarios (under ~50 Nm).
    /// </summary>
    public sealed class GeoDatum
    {
        private const double MetresPerDegLat = 111320.0;

        /// <summary>Default: Narragansett Bay approaches (matches OpenBridge GPS placeholders).</summary>
        public static readonly GeoDatum DefaultCoastal = new GeoDatum(41.05735, -71.27793);

        public double OriginLatDeg { get; }
        public double OriginLonDeg { get; }

        private readonly double _metresPerDegLon;

        public GeoDatum(double originLatDeg, double originLonDeg)
        {
            OriginLatDeg = originLatDeg;
            OriginLonDeg = originLonDeg;
            double latRad = originLatDeg * Math.PI / 180.0;
            _metresPerDegLon = MetresPerDegLat * Math.Cos(latRad);
            if (_metresPerDegLon < 1.0)
            {
                _metresPerDegLon = 1.0;
            }
        }

        public void ToLatLon(double northM, double eastM, out double latDeg, out double lonDeg)
        {
            latDeg = OriginLatDeg + northM / MetresPerDegLat;
            lonDeg = OriginLonDeg + eastM / _metresPerDegLon;
        }

        public void ToNorthEast(double latDeg, double lonDeg, out double northM, out double eastM)
        {
            northM = (latDeg - OriginLatDeg) * MetresPerDegLat;
            eastM = (lonDeg - OriginLonDeg) * _metresPerDegLon;
        }

        /// <summary>Formats as dd°mm.mmm N/S (maritime GPS style).</summary>
        public static string FormatLat(double latDeg)
        {
            char hemi = latDeg >= 0.0 ? 'N' : 'S';
            double a = Math.Abs(latDeg);
            int d = (int)a;
            double minutes = (a - d) * 60.0;
            return $"{d:00}°{minutes:00.000} {hemi}";
        }

        /// <summary>Formats as ddd°mm.mmm E/W.</summary>
        public static string FormatLon(double lonDeg)
        {
            char hemi = lonDeg >= 0.0 ? 'E' : 'W';
            double a = Math.Abs(lonDeg);
            int d = (int)a;
            double minutes = (a - d) * 60.0;
            return $"{d:000}°{minutes:00.000} {hemi}";
        }
    }
}
