using System;

namespace NavigationSim.Core
{
    [Serializable]
    public class AutopilotParams
    {
        public double Kp = 3.0;            // deg rudder per deg heading error
        public double Ki = 0.05;           // deg rudder per deg·s
        public double Kd = 120.0;          // deg rudder per deg/s of yaw rate
        public double RudderLimitDeg = 20.0;
    }

    /// <summary>
    /// PID heading controller with anti-windup and rudder limit, following the
    /// structure of PVS shipClarke83.headingAutopilot (pole-placement PID).
    /// </summary>
    public class HeadingAutopilot
    {
        private double _integral;

        public double Update(AutopilotParams p, double headingDeg, double setpointDeg,
            double yawRateDegPerS, double dt)
        {
            double error = ShipState.WrapDeg(setpointDeg - headingDeg);

            double cmd = p.Kp * error - p.Kd * yawRateDegPerS + p.Ki * _integral;

            bool saturated = Math.Abs(cmd) > p.RudderLimitDeg;
            if (!saturated)
            {
                _integral += error * dt;
                _integral = Math.Max(-400.0, Math.Min(400.0, _integral));
            }

            return Math.Max(-p.RudderLimitDeg, Math.Min(p.RudderLimitDeg, cmd));
        }

        public void Reset()
        {
            _integral = 0.0;
        }
    }
}
