using System;
using System.Globalization;
using System.Linq;
using Callsign.Core.Callouts;
using Callsign.Core.Events;
using Callsign.Core.Ports;
using Callsign.Core.TrafficStops;

namespace Callsign.Core.Director
{
    /// <summary>Tuning for <see cref="PacingDirector"/>. Times in game milliseconds; levels 0..1.</summary>
    public sealed class DirectorSettings
    {
        /// <summary>Tension halves this often.</summary>
        public long TensionHalfLifeMs { get; set; } = 6 * 60_000;
        /// <summary>The intensity dispatch aims for with no tension and nothing quiet about it.</summary>
        public double BaseTarget { get; set; } = 0.35;
        /// <summary>How much a long quiet spell raises the target...</summary>
        public double QuietBonus { get; set; } = 0.5;
        /// <summary>... reached after this long without a call or stop.</summary>
        public long QuietRampMs { get; set; } = 12 * 60_000;
        /// <summary>Lowest pacing factor a situation can get (far from the target, but still possible).</summary>
        public double Floor { get; set; } = 0.15;
        /// <summary>At full tension the next call waits this much longer (0.8 = up to 1.8× the gap).</summary>
        public double GapStretch { get; set; } = 0.8;

        public int MemorySize { get; set; } = 4;
        public double SituationRepeat { get; set; } = 0.85;
        public double LocationRepeat { get; set; } = 0.9;
        public double SuspectRepeat { get; set; } = 0.6;
        public double OutcomeRepeat { get; set; } = 0.35;
        public double ComplicationRepeat { get; set; } = 0.8;

        /// <summary>
        /// Complication chance scale at target intensity 0 (right after chaos) and 1 (a long quiet spell):
        /// fewer twists on a tense shift, more when it has been quiet.
        /// </summary>
        public double ComplicationScaleMin { get; set; } = 0.4;
        public double ComplicationScaleMax { get; set; } = 1.6;

        /// <summary>Extra tension a call with a complication adds.</summary>
        public double ComplicationLoad { get; set; } = 0.15;
    }

    /// <summary>
    /// Pacing director: tracks how intense the shift has been (tension, from finished calls and stops,
    /// decaying over time) and aims the next call at a matching intensity: calm after something big,
    /// bigger after a long quiet spell. Also keeps the anti-repeat memory of offered calls.
    /// Learns everything from the event bus.
    /// </summary>
    public sealed class PacingDirector : IDispatchDirector
    {
        readonly IClock _clock;
        readonly DirectorSettings _settings;
        double _tension;
        long _tensionAt;
        long _lastActivityAt;

        public PacingDirector(IEventBus bus, IClock clock, DirectorSettings? settings = null)
        {
            _clock = clock;
            _settings = settings ?? new DirectorSettings();
            Situations = new CallMemory(_settings.MemorySize, _settings.SituationRepeat);
            Locations = new CallMemory(_settings.MemorySize, _settings.LocationRepeat);
            Suspects = new CallMemory(_settings.MemorySize, _settings.SuspectRepeat);
            Outcomes = new CallMemory(_settings.MemorySize, _settings.OutcomeRepeat);
            Complications = new CallMemory(_settings.MemorySize, _settings.ComplicationRepeat);
            _tensionAt = _lastActivityAt = clock.NowMs;

            bus.Subscribe<CalloutOffered>(e => Remember(e.Incident));
            bus.Subscribe<CalloutAccepted>(_ => Active());
            bus.Subscribe<CalloutEnded>(e => Add(Load(e)));
            bus.Subscribe<TrafficStopStarted>(_ => Active());
            bus.Subscribe<TrafficStopEnded>(e => Add(Load(e.Result)));
        }

        public CallMemory Situations { get; }
        public CallMemory Locations { get; }
        public CallMemory Suspects { get; }
        public CallMemory Outcomes { get; }
        public CallMemory Complications { get; }

        /// <summary>0..1: how intense the shift has been lately.</summary>
        public double Tension
        {
            get
            {
                double halfLives = (_clock.NowMs - _tensionAt) / (double)_settings.TensionHalfLifeMs;
                return _tension * Math.Pow(0.5, Math.Max(0, halfLives));
            }
        }

        /// <summary>0..1: the intensity the next call aims for.</summary>
        public double Target
        {
            get
            {
                double quiet = Math.Min(1, (_clock.NowMs - _lastActivityAt) / (double)_settings.QuietRampMs);
                return Clamp01(_settings.BaseTarget + _settings.QuietBonus * quiet - Tension);
            }
        }

        public double GapScale => 1 + _settings.GapStretch * Tension;

        public double Factor(SituationPart situation)
        {
            double closeness = 1 - Math.Abs(situation.Intensity - Target);
            double pacing = _settings.Floor + (1 - _settings.Floor) * closeness * closeness;
            return pacing * Situations.Factor(situation.Id);
        }

        public double Factor(LocationPart location) => Locations.Factor(LocationKey(location));
        public double Factor(SuspectPart suspect) => Suspects.Factor(suspect.Id);
        public double Factor(OutcomePart outcome) => Outcomes.Factor(outcome.Id);
        public double Factor(ComplicationPart complication) => Complications.Factor(complication.Id);

        public double ComplicationScale =>
            _settings.ComplicationScaleMin + (_settings.ComplicationScaleMax - _settings.ComplicationScaleMin) * Target;

        /// <summary>One line for dev tools.</summary>
        public string Describe() => "tension=" + F(Tension) + " target=" + F(Target) + " gap=x" + F(GapScale)
            + " twists=x" + F(ComplicationScale)
            + " recent=" + (Situations.Recent.Count == 0 ? "-" : string.Join(",", Situations.Recent));

        void Remember(Incident incident)
        {
            Situations.Remember(incident.Situation.Id);
            Locations.Remember(LocationKey(incident.Location));
            Suspects.Remember(incident.Suspect.Id);
            Outcomes.Remember(incident.Outcome.Id);
            if (incident.Twist != null) Complications.Remember(incident.Twist.Part.Id);
        }

        void Active() => _lastActivityAt = _clock.NowMs;

        void Add(double load)
        {
            if (load <= 0) return;
            _tension = Math.Min(1, Tension + load);
            _tensionAt = _lastActivityAt = _clock.NowMs;
        }

        /// <summary>How much a finished call adds: its intensity, or more if it turned into a chase or a fight.</summary>
        double Load(CalloutEnded e)
        {
            switch (e.Reason)
            {
                case CalloutEndReason.Declined:
                case CalloutEndReason.OfferExpired:
                case CalloutEndReason.Failed:
                    return 0;
            }
            double load = e.Incident.Situation.Intensity;
            if (e.Incident.Suspects.Any(s => s.Outcome.Behaviour == SuspectBehaviour.Flee)) load = Math.Max(load, 0.6);
            if (e.Incident.Suspects.Any(s => s.Outcome.Behaviour == SuspectBehaviour.Fight)) load = Math.Max(load, 0.8);
            Twist? twist = e.Incident.Twist;
            if (twist != null)
            {
                if (twist.Part.Behaviour == SuspectBehaviour.Fight || twist.Accomplices.Any(a => a.Outcome.Behaviour == SuspectBehaviour.Fight))
                    load = Math.Max(load, 0.8);
                load += _settings.ComplicationLoad;
            }
            if (e.Reason == CalloutEndReason.SuspectEscaped || e.Reason == CalloutEndReason.SuspectDied) load += 0.1;
            if (e.Reason == CalloutEndReason.Cancelled) load *= 0.5;   // usually called off before it got going
            return Math.Min(1, load);
        }

        static double Load(TrafficStopResult result)
        {
            switch (result)
            {
                case TrafficStopResult.Escaped: return 0.6;
                case TrafficStopResult.SuspectDied: return 0.7;
                case TrafficStopResult.Arrested: return 0.35;
                case TrafficStopResult.Cancelled: return 0;
                default: return 0.1;
            }
        }

        /// <summary>The street fallback always has the id "street"; what repeats for the player is the name.</summary>
        static string LocationKey(LocationPart location) => location.Name;

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
        static string F(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
