using System;
using Callsign.Core.Callouts;
using Callsign.Core.Ports;

namespace Callsign.Core.Suspects
{
    /// <summary>Chances (0..1) behind how someone reacts to being pulled over or stopped.</summary>
    public sealed class StopReactionChances
    {
        /// <summary>Runs for no good reason.</summary>
        public double FleeAnyway { get; set; } = 0.02;
        public double FleeIfWanted { get; set; } = 0.3;
        public double FleeIfStolenVehicle { get; set; } = 0.5;
        public double FleeIfIntoxicated { get; set; } = 0.15;
        public double FleeIfCarryingDrugs { get; set; } = 0.1;
        /// <summary>Nervous people are this many times as likely to run (capped at <see cref="FleeMax"/>).</summary>
        public double NervousFactor { get; set; } = 1.5;
        public double FleeMax { get; set; } = 0.9;
        /// <summary>A wanted suspect with a hidden weapon fights instead of running.</summary>
        public double FightIfArmedAndWanted { get; set; } = 0.3;
    }

    /// <summary>
    /// Picks how a stopped person reacts from their hidden traits: the more they have to lose, the
    /// likelier they run (or, armed and wanted, fight). Used where nothing scripted the reaction,
    /// e.g. a driver the player picked out of traffic.
    /// </summary>
    public static class StopReaction
    {
        public static double FleeChance(SuspectProfile p, bool inStolenVehicle, StopReactionChances c)
        {
            double stay = 1 - c.FleeAnyway;
            if (p.Has(SuspectTraits.Warrant)) stay *= 1 - c.FleeIfWanted;
            if (inStolenVehicle) stay *= 1 - c.FleeIfStolenVehicle;
            if (p.OverAlcoholLimit || p.Has(SuspectTraits.OnDrugs)) stay *= 1 - c.FleeIfIntoxicated;
            if (p.Has(SuspectTraits.CarryingDrugs)) stay *= 1 - c.FleeIfCarryingDrugs;
            double flee = 1 - stay;
            if (p.Has(SuspectTraits.Nervous)) flee *= c.NervousFactor;
            return Math.Min(flee, c.FleeMax);
        }

        public static SuspectBehaviour Pick(SuspectProfile p, bool inStolenVehicle, IRandom random, StopReactionChances c)
        {
            if (p.ConcealedWeapon != null && p.Has(SuspectTraits.Warrant) && random.NextDouble() < c.FightIfArmedAndWanted)
                return SuspectBehaviour.Fight;
            return random.NextDouble() < FleeChance(p, inStolenVehicle, c) ? SuspectBehaviour.Flee : SuspectBehaviour.Comply;
        }
    }
}
