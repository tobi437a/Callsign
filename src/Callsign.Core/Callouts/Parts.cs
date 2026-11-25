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
        public bool Fits(LocationPart location) => TagMatch.Any(LocationTags, location.Tags);
        public bool Fits(SuspectPart suspect) => TagMatch.Any(SuspectTags, suspect.Tags);
        public bool Fits(OutcomePart outcome) => TagMatch.Any(OutcomeTags, outcome.Tags);

        public void Validate()
        {
            Require.Text(Title, "title");
            Require.Text(Dispatch, "dispatch text");
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
