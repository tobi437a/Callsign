using System;
using System.Collections.Generic;
using System.Linq;
using Callsign.Core.World;

namespace Callsign.Core.Callouts
{
    /// <summary>
    /// One building block of an incident. The generator combines one part of each kind
    /// (situation × location × suspect × outcome), so content multiplies instead of being scripted.
    /// </summary>
    public interface ICalloutPart
    {
        /// <summary>Unique within its registry, e.g. "sandy-liquor".</summary>
        string Id { get; }

        /// <summary>Relative chance of being picked among the parts that fit. 0 = never.</summary>
        double Weight { get; }

        /// <summary>Free-form labels situations filter on, e.g. "store", "county", "armed".</summary>
        IReadOnlyList<string> Tags { get; }

        /// <summary>Throws <see cref="ArgumentException"/> if the part can't work (missing name, no models, ...).</summary>
        void Validate();
    }

    /// <summary>Where an incident happens.</summary>
    public sealed record LocationPart(string Id, string Name, Position Position, IReadOnlyList<string> Tags, double Weight = 1)
        : ICalloutPart
    {
        public void Validate() => Require.Text(Name, "name");
    }

    /// <summary>
    /// What is going on: the call's title, the dispatch line, and which other parts fit it.
    /// </summary>
    /// <param name="Dispatch">Radio text. <c>{location}</c> and <c>{suspect}</c> are filled in.</param>
    /// <param name="LocationTags">A location fits if it has any of these tags. Empty = any location.</param>
    /// <param name="SuspectTags">Same for suspects.</param>
    /// <param name="OutcomeTags">Same for outcomes.</param>
    public sealed record SituationPart(
        string Id,
        string Title,
        string Dispatch,
        IReadOnlyList<string> LocationTags,
        IReadOnlyList<string> SuspectTags,
        IReadOnlyList<string> OutcomeTags,
        IReadOnlyList<string> Tags,
        double Weight = 1) : ICalloutPart
    {
        /// <summary>Most suspects one incident can have (a vehicle has four seats).</summary>
        public const int SuspectLimit = 4;

        /// <summary>The suspects are in a vehicle: only suspects that come with vehicles fit, and vice versa.</summary>
        public bool InVehicle { get; init; }

        /// <summary>Fewest suspects. Each one rolls their own outcome.</summary>
        public int MinSuspects { get; init; } = 1;

        /// <summary>Most suspects (up to <see cref="SuspectLimit"/>).</summary>
        public int MaxSuspects { get; init; } = 1;

        /// <summary>
        /// 0..1: how big a call this is (0.15 suspicious person, 0.9 shots fired). The director pairs calm
        /// calls with tense shifts and big ones with quiet spells.
        /// </summary>
        public double Intensity { get; init; } = DefaultIntensity;

        public const double DefaultIntensity = 0.3;

        /// <summary>A complication fits if it has any of these tags. Empty = none fit (no twists unless asked for).</summary>
        public IReadOnlyList<string> ComplicationTags { get; init; } = Array.Empty<string>();

        /// <summary>0..1: chance this call gets a complication (when one fits). The director tilts it with the shift's pace.</summary>
        public double ComplicationChance { get; init; } = DefaultComplicationChance;

        public const double DefaultComplicationChance = 0.35;

        public bool Fits(LocationPart location) => TagMatch.Any(LocationTags, location.Tags);
        public bool Fits(SuspectPart suspect) => suspect.InVehicle == InVehicle && TagMatch.Any(SuspectTags, suspect.Tags);
        public bool Fits(OutcomePart outcome) => TagMatch.Any(OutcomeTags, outcome.Tags);
        public bool Fits(ComplicationPart complication) =>
            ComplicationTags.Count > 0 && TagMatch.Any(ComplicationTags, complication.Tags);

        public void Validate()
        {
            Require.Text(Title, "title");
            Require.Text(Dispatch, "dispatch text");
            if (MinSuspects < 1 || MaxSuspects < MinSuspects || MaxSuspects > SuspectLimit)
                throw new ArgumentException("suspect count must be 1.." + SuspectLimit + " with min <= max");
            if (!(Intensity >= 0 && Intensity <= 1)) throw new ArgumentException("intensity must be 0..1");
            if (!(ComplicationChance >= 0 && ComplicationChance <= 1)) throw new ArgumentException("complication chance must be 0..1");
        }
    }

    /// <summary>Who the suspect is.</summary>
    /// <param name="Description">How dispatch describes them, e.g. "man in a grey hoodie".</param>
    /// <param name="Models">Ped models to pick from (at least one).</param>
    /// <param name="Weapon">Weapon they carry, or null for none.</param>
    public sealed record SuspectPart(
        string Id,
        string Description,
        IReadOnlyList<string> Models,
        string? Weapon,
        IReadOnlyList<string> Tags,
        double Weight = 1) : ICalloutPart
    {
        /// <summary>Vehicle models they drive (one is picked per incident). Empty = on foot.</summary>
        public IReadOnlyList<string> Vehicles { get; init; } = Array.Empty<string>();

        public bool InVehicle => Vehicles.Count > 0;

        public void Validate()
        {
            Require.Text(Description, "description");
            if (Models == null || Models.Count == 0) throw new ArgumentException("needs at least one model");
        }
    }

    /// <summary>How the suspect reacts once the officer shows up.</summary>
    public sealed record OutcomePart(string Id, SuspectBehaviour Behaviour, IReadOnlyList<string> Tags, double Weight = 1)
        : ICalloutPart
    {
        public void Validate()
        {
            if (!Enum.IsDefined(typeof(SuspectBehaviour), Behaviour)) throw new ArgumentException("unknown behaviour " + Behaviour);
        }
    }

    /// <summary>
    /// A twist rolled with the call and kept secret from dispatch: something that changes the call
    /// partway through (a second suspect shows up, a calm suspect bolts, friends attack during the arrest).
    /// </summary>
    /// <param name="Message">Shown to the player when it happens, or null for no notice.</param>
    public sealed record ComplicationPart(
        string Id,
        ComplicationKind Kind,
        ComplicationTrigger Trigger,
        string? Message,
        IReadOnlyList<string> Tags,
        double Weight = 1) : ICalloutPart
    {
        /// <summary>Seconds after the player arrives, for <see cref="ComplicationTrigger.Delay"/>.</summary>
        public double DelaySeconds { get; init; } = 20;

        /// <summary>
        /// <see cref="ComplicationKind.Accomplice"/>: how they react (null = rolled like the call's suspects).
        /// <see cref="ComplicationKind.Panic"/>: what the suspect does instead (null = runs).
        /// </summary>
        public SuspectBehaviour? Behaviour { get; init; }

        /// <summary>Accomplices: how many show up (1..3).</summary>
        public int Count { get; init; } = 1;

        /// <summary>Accomplices: ped models. Empty = the same kind of person as the call's suspect.</summary>
        public IReadOnlyList<string> Models { get; init; } = Array.Empty<string>();

        /// <summary>Accomplices: weapon in hand, or null for the call's suspect's weapon (if any).</summary>
        public string? Weapon { get; init; }

        public const int AccompliceLimit = 3;

        public void Validate()
        {
            if (!Enum.IsDefined(typeof(ComplicationKind), Kind)) throw new ArgumentException("unknown kind " + Kind);
            if (!Enum.IsDefined(typeof(ComplicationTrigger), Trigger)) throw new ArgumentException("unknown trigger " + Trigger);
            if (Behaviour != null && !Enum.IsDefined(typeof(SuspectBehaviour), Behaviour.Value)) throw new ArgumentException("unknown behaviour " + Behaviour);
            if (Kind == ComplicationKind.Accomplice && (Count < 1 || Count > AccompliceLimit))
                throw new ArgumentException("count must be 1.." + AccompliceLimit);
            if (Kind == ComplicationKind.Panic && Behaviour == SuspectBehaviour.Comply)
                throw new ArgumentException("a panic needs Flee or Fight");
            if (!(DelaySeconds >= 0)) throw new ArgumentException("delay must be 0 or more");
        }
    }

    /// <summary>What a complication does.</summary>
    public enum ComplicationKind
    {
        /// <summary>More people involved show up near the suspect.</summary>
        Accomplice,
        /// <summary>A suspect who was cooperating suddenly runs or attacks (a driver floors it).</summary>
        Panic,
    }

    /// <summary>When a complication happens.</summary>
    public enum ComplicationTrigger
    {
        /// <summary>When the suspects first notice the officer.</summary>
        Arrival,
        /// <summary>A while (<see cref="ComplicationPart.DelaySeconds"/>) after that.</summary>
        Delay,
        /// <summary>When the player stops a suspect (or a pulled-over vehicle stops).</summary>
        Stop,
        /// <summary>When the player arrests a suspect.</summary>
        Arrest,
    }

    /// <summary>The suspect's reaction when the officer arrives.</summary>
    public enum SuspectBehaviour
    {
        /// <summary>Stays and cooperates.</summary>
        Comply,
        /// <summary>Runs.</summary>
        Flee,
        /// <summary>Attacks the officer.</summary>
        Fight,
    }

    internal static class Require
    {
        public static void Text(string? value, string what)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("missing " + what);
        }
    }

    internal static class TagMatch
    {
        /// <summary>True if <paramref name="wanted"/> is empty or shares a tag with <paramref name="has"/>.</summary>
        public static bool Any(IReadOnlyList<string> wanted, IReadOnlyList<string> has) =>
            wanted.Count == 0 || wanted.Any(w => has.Contains(w, StringComparer.OrdinalIgnoreCase));
    }
}
