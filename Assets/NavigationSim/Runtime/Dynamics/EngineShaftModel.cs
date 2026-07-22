using System;

namespace NavigationSim.Core
{
    public enum FuelType
    {
        MarineDiesel = 0,
        HeavyFuelOil = 1,
        Lng = 2
    }

    [Serializable]
    public class EngineParams
    {
        public double RatedRps = 1.25;          // shaft revolutions at 100% telegraph
        public double TimeConstantS = 18.0;     // first-order engine response
        public double MaxRpsPerS = 0.06;        // rpm ramp limit (accelerating)
        public double MaxRpsPerSDecel = 0.12;   // rpm ramp limit (decelerating)
        public double ReversalDelayS = 8.0;     // dwell at zero before reversing
        public double McrKw = 36000.0;          // maximum continuous rating
        public FuelType Fuel = FuelType.MarineDiesel;

        // SFOC(load) = A·load² + B·load + C   [g/kWh] (Vessel.js FuelConsumption pattern)
        public double SfocA = 140.0;
        public double SfocB = -230.0;
        public double SfocC = 270.0;
    }

    /// <summary>
    /// The telegraph sets an order, never the rpm. First-order shaft response
    /// with separate ramp limits and a mandatory dwell at zero before the shaft
    /// reverses, so a crash stop develops ahead → stop → astern correctly.
    /// </summary>
    public class EngineShaftModel
    {
        private const double Eps = 1e-3;

        public double Rps;

        private double _reversalTimer;
        private int _lastRunSign;

        /// <summary>Seconds left before a commanded reversal engages (0 = none pending).</summary>
        public double ReversalWaitS { get; private set; }

        public void Update(double targetRps, EngineParams p, double dt)
        {
            double effectiveTarget = targetRps;
            ReversalWaitS = 0.0;

            if (Math.Abs(Rps) > Eps)
            {
                _lastRunSign = Math.Sign(Rps);
                _reversalTimer = 0.0;

                if (targetRps * Rps < 0.0)
                {
                    effectiveTarget = 0.0; // must come to rest before reversing
                }
            }
            else if (targetRps != 0.0 && _lastRunSign != 0 && Math.Sign(targetRps) != _lastRunSign)
            {
                // At rest with a reversal ordered: hold for the reversal delay.
                _reversalTimer += dt;
                if (_reversalTimer < p.ReversalDelayS)
                {
                    effectiveTarget = 0.0;
                    ReversalWaitS = p.ReversalDelayS - _reversalTimer;
                }
                else
                {
                    _lastRunSign = Math.Sign(targetRps);
                    _reversalTimer = 0.0;
                }
            }

            double rate = (effectiveTarget - Rps) / Math.Max(0.5, p.TimeConstantS);
            bool decelerating = Math.Abs(effectiveTarget) < Math.Abs(Rps) || effectiveTarget * Rps < 0.0;
            double limit = decelerating ? p.MaxRpsPerSDecel : p.MaxRpsPerS;
            rate = Clamp(rate, -limit, limit);
            Rps += rate * dt;

            if (effectiveTarget == 0.0 && Math.Abs(Rps) < 5e-4)
            {
                Rps = 0.0;
            }
        }

        public void Reset()
        {
            Rps = 0.0;
            _reversalTimer = 0.0;
            _lastRunSign = 0;
            ReversalWaitS = 0.0;
        }

        private static double Clamp(double v, double lo, double hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }
    }
}
