using Callsign.Core.Ports;
using Callsign.Core.Suspects;

namespace Callsign.Core.Callouts
{
    /// <summary>
    /// Tuning for how fleeing drivers give up. Pressure builds per second from what the player does
    /// to them; a driver stops once it passes their resolve. Rates are per second.
    /// </summary>
    public sealed class PursuitSettings
    {
        // Resolve: most drivers are average, some fold quickly, some almost never stop.
        public double QuickToFoldChance { get; set; } = 0.3;
        public double DeterminedChance { get; set; } = 0.2;
        public double QuickToFoldMin { get; set; } = 0.4;
        public double QuickToFoldMax { get; set; } = 0.8;
        public double AverageMin { get; set; } = 0.9;
        public double AverageMax { get; set; } = 1.8;
        public double DeterminedMin { get; set; } = 3.0;
        public double DeterminedMax { get; set; } = 6.0;
        /// <summary>Extra resolve for each thing they have to hide (warrant, drugs, weapon).</summary>
        public double ResolvePerSecret { get; set; } = 0.4;
        /// <summary>Extra resolve for someone looking for a fight.</summary>
        public double ResolveIfViolent { get; set; } = 0.6;

        /// <summary>Chance that a driver who breaks jumps out and runs instead of giving up.</summary>
        public double RunnerChance { get; set; } = 0.3;
        public double RunnerChanceIfWanted { get; set; } = 0.6;

        public double BaseRate { get; set; } = 0.008;
        /// <summary>The player is right behind them (within <see cref="CloseDistance"/>).</summary>
        public double CloseRate { get; set; } = 0.012;
        public float CloseDistance { get; set; } = 30;
        public double RatePerBurstTyre { get; set; } = 0.05;
        public double DamagedEngineRate { get; set; } = 0.05;
        public double WreckedEngineRate { get; set; } = 0.15;
        /// <summary>
        /// The player is aiming a gun at them from close (within <see cref="AimDistance"/>) while the car is
        /// slow (under <see cref="AimSpeed"/>). A gun waved at a speeding car barely registers.
        /// </summary>
        public double AimedAtRate { get; set; } = 0.04;
        public float AimDistance { get; set; } = 20;
        public float AimSpeed { get; set; } = 12;
        /// <summary>The car can barely move (slower than <see cref="CrawlSpeed"/>) with the player close.</summary>
        public double CrawlingRate { get; set; } = 0.05;
        public float CrawlSpeed { get; set; } = 6;
    }

    /// <summary>What the fleeing driver is up against right now.</summary>
    /// <param name="EngineHealth">0 = dead, 1 = undamaged.</param>
    public readonly record struct PursuitPressure(int BurstTyres, float EngineHealth, float Speed, float PlayerDistance, bool PlayerAiming);

    public enum PursuitChoice
    {
        KeepGoing,
        /// <summary>Pull over and give up.</summary>
        GiveUp,
        /// <summary>Stop and run for it on foot (or fight, if that's who they are).</summary>
        Bail,
    }

    /// <summary>
    /// How badly one fleeing driver wants to get away. Rolled once per pursuit: their resolve
    /// (some fold at the first sign of trouble, some almost never stop) and whether, once they
    /// break, they give up or bail and run. Pressure only grows: burst tyres, a smoking engine, a
    /// gun pointed at them and a long chase all wear them down.
    /// </summary>
    public sealed class PursuitMind
    {
        readonly PursuitSettings _s;

        public PursuitMind(SuspectProfile driver, SuspectBehaviour behaviour, IRandom random, PursuitSettings settings)
        {
            _s = settings;
            double tier = random.NextDouble();
            double roll = random.NextDouble();
            Resolve = tier < _s.QuickToFoldChance ? Between(_s.QuickToFoldMin, _s.QuickToFoldMax, roll)
                : tier < 1 - _s.DeterminedChance ? Between(_s.AverageMin, _s.AverageMax, roll)
                : Between(_s.DeterminedMin, _s.DeterminedMax, roll);

            bool wanted = driver.Has(SuspectTraits.Warrant);
            foreach (SuspectTraits secret in new[] { SuspectTraits.Warrant, SuspectTraits.CarryingDrugs, SuspectTraits.ConcealedWeapon })
                if (driver.Has(secret)) Resolve += _s.ResolvePerSecret;
            if (behaviour == SuspectBehaviour.Fight) Resolve += _s.ResolveIfViolent;

            Runner = behaviour == SuspectBehaviour.Fight
                || random.NextDouble() < (wanted ? _s.RunnerChanceIfWanted : _s.RunnerChance);
        }

        /// <summary>How much pressure they take before they stop.</summary>
        public double Resolve { get; }

        /// <summary>Once they break, they bail and run (or fight) instead of giving up.</summary>
        public bool Runner { get; }

        public double Pressure { get; private set; }

        /// <summary>Adds <paramref name="seconds"/> worth of pressure and says what the driver does now.</summary>
        public PursuitChoice Update(PursuitPressure p, double seconds)
        {
            Pressure += Rate(p) * seconds;
            if (Pressure < Resolve) return PursuitChoice.KeepGoing;
            return Runner ? PursuitChoice.Bail : PursuitChoice.GiveUp;
        }

        double Rate(PursuitPressure p)
        {
            bool close = p.PlayerDistance <= _s.CloseDistance;
            double rate = _s.BaseRate;
            if (close) rate += _s.CloseRate;
            rate += p.BurstTyres * _s.RatePerBurstTyre;
            if (p.EngineHealth < 0.2f) rate += _s.WreckedEngineRate;
            else if (p.EngineHealth < 0.5f) rate += _s.DamagedEngineRate;
            if (p.PlayerAiming && p.PlayerDistance <= _s.AimDistance && p.Speed < _s.AimSpeed) rate += _s.AimedAtRate;
            if (close && p.Speed < _s.CrawlSpeed) rate += _s.CrawlingRate;
            return rate;
        }

        static double Between(double min, double max, double t) => min + (max - min) * t;
    }
}
