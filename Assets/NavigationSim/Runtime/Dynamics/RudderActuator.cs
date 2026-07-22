using System;

namespace NavigationSim.Core
{
    [Serializable]
    public class RudderParams
    {
        public double MaxAngleDeg = 35.0;
        public double MaxRateDegPerS = 3.0;   // typical SOLAS steering gear ~2.3-3.5 °/s
        public double TimeConstantS = 1.5;
    }

    /// <summary>
    /// Rate-limited first-order rudder response (pattern from PVS frigate.py:
    /// deltaMax + DdeltaMax). The wheel commands an angle; the gear chases it.
    /// </summary>
    public class RudderActuator
    {
        public double AngleDeg;

        public void Update(double commandDeg, bool pumpsAvailable, RudderParams p, double dt)
        {
            if (!pumpsAvailable)
            {
                return; // no hydraulic pump: rudder frozen
            }

            double cmd = Clamp(commandDeg, -p.MaxAngleDeg, p.MaxAngleDeg);
            double rate = (cmd - AngleDeg) / Math.Max(0.05, p.TimeConstantS);
            rate = Clamp(rate, -p.MaxRateDegPerS, p.MaxRateDegPerS);
            AngleDeg = Clamp(AngleDeg + rate * dt, -p.MaxAngleDeg, p.MaxAngleDeg);
        }

        /// <summary>NFU: drive the rudder directly at full gear rate while held.</summary>
        public void UpdateNfu(int direction, bool pumpsAvailable, RudderParams p, double dt)
        {
            if (!pumpsAvailable || direction == 0)
            {
                return;
            }

            AngleDeg = Clamp(AngleDeg + Math.Sign(direction) * p.MaxRateDegPerS * dt,
                -p.MaxAngleDeg, p.MaxAngleDeg);
        }

        public void Reset()
        {
            AngleDeg = 0.0;
        }

        private static double Clamp(double v, double lo, double hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }
    }
}
