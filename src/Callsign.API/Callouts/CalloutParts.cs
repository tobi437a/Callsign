using System;
using System.Collections.Generic;
using System.Linq;
using GTA.Math;

namespace Callsign.API.Callouts
{
    /// <summary>
    /// What is going on in an incident: its title, the dispatch line, and which locations, suspects and
    /// outcomes fit it (by tag).
    /// </summary>
    public sealed class CalloutSituation
    {
        /// <summary>Creates a situation.</summary>
        /// <param name="id">Unique id, prefixed with your plugin's name.</param>
        /// <param name="title">Short call title, e.g. "Noise complaint".</param>
        /// <param name="dispatch">Dispatch text. <c>{location}</c> and <c>{suspect}</c> are filled in.</param>
        /// <param name="locationTags">A location fits if it has any of these tags. Null or empty = any location.</param>
        /// <param name="suspectTags">Same for suspects.</param>
        /// <param name="outcomeTags">Same for outcomes.</param>
        /// <param name="weight">Relative chance of being picked. Built-in situations use 0.5 to 2.</param>
        public CalloutSituation(string id, string title, string dispatch,
            IEnumerable<string>? locationTags = null, IEnumerable<string>? suspectTags = null,
            IEnumerable<string>? outcomeTags = null, double weight = 1)
        {
            Id = id;
            Title = title;
            Dispatch = dispatch;
            LocationTags = TagList.Copy(locationTags);
            SuspectTags = TagList.Copy(suspectTags);
            OutcomeTags = TagList.Copy(outcomeTags);
            Weight = weight;
        }

        /// <summary>Unique id.</summary>
        public string Id { get; }
        /// <summary>Call title.</summary>
        public string Title { get; }
        /// <summary>Dispatch text template.</summary>
        public string Dispatch { get; }
        /// <summary>Tags a location needs one of (empty = any).</summary>
        public IReadOnlyList<string> LocationTags { get; }
        /// <summary>Tags a suspect needs one of (empty = any).</summary>
        public IReadOnlyList<string> SuspectTags { get; }
        /// <summary>Tags an outcome needs one of (empty = any).</summary>
        public IReadOnlyList<string> OutcomeTags { get; }
        /// <summary>Relative chance of being picked.</summary>
        public double Weight { get; }
    }

    /// <summary>A place where incidents can happen.</summary>
    public sealed class CalloutLocation
    {
        /// <summary>Creates a location.</summary>
        /// <param name="id">Unique id, prefixed with your plugin's name.</param>
        /// <param name="name">How dispatch names it, e.g. "Pillbox Hill Medical Center".</param>
        /// <param name="position">Where. The suspect spawns on the nearest spot a ped can stand on, so it only needs to be close.</param>
        /// <param name="tags">Labels situations filter on. Built-in ones: "store", "public", "street", "city", "county".</param>
        /// <param name="weight">Relative chance of being picked among the fitting locations in range.</param>
        public CalloutLocation(string id, string name, Vector3 position, IEnumerable<string>? tags = null, double weight = 1)
        {
            Id = id;
            Name = name;
            Position = position;
            Tags = TagList.Copy(tags);
            Weight = weight;
        }

        /// <summary>Unique id.</summary>
        public string Id { get; }
        /// <summary>Name dispatch uses.</summary>
        public string Name { get; }
        /// <summary>World position.</summary>
        public Vector3 Position { get; }
        /// <summary>Labels situations filter on.</summary>
        public IReadOnlyList<string> Tags { get; }
        /// <summary>Relative chance of being picked.</summary>
        public double Weight { get; }
    }

    /// <summary>Who a suspect can be.</summary>
    public sealed class CalloutSuspect
    {
        /// <summary>Creates a suspect profile.</summary>
        /// <param name="id">Unique id, prefixed with your plugin's name.</param>
        /// <param name="description">How dispatch describes them, e.g. "man in a red jacket".</param>
        /// <param name="models">Ped model names to pick from, e.g. "a_m_y_skater_01". At least one.</param>
        /// <param name="weapon">Weapon they carry (e.g. "WEAPON_KNIFE"), or null.</param>
        /// <param name="tags">Labels situations filter on. Built-in ones: "rowdy", "armed".</param>
        /// <param name="weight">Relative chance of being picked.</param>
        public CalloutSuspect(string id, string description, IEnumerable<string> models, string? weapon = null,
            IEnumerable<string>? tags = null, double weight = 1)
        {
            Id = id;
            Description = description;
            Models = TagList.Copy(models);
            Weapon = weapon;
            Tags = TagList.Copy(tags);
            Weight = weight;
        }

        /// <summary>Unique id.</summary>
        public string Id { get; }
        /// <summary>Description dispatch uses.</summary>
        public string Description { get; }
        /// <summary>Ped models to pick from.</summary>
        public IReadOnlyList<string> Models { get; }
        /// <summary>Weapon they carry, or null.</summary>
        public string? Weapon { get; }
        /// <summary>Labels situations filter on.</summary>
        public IReadOnlyList<string> Tags { get; }
        /// <summary>Relative chance of being picked.</summary>
        public double Weight { get; }
    }

    /// <summary>How the suspect reacts when the officer arrives.</summary>
    public sealed class CalloutOutcome
    {
        /// <summary>Creates an outcome.</summary>
        /// <param name="id">Unique id, prefixed with your plugin's name.</param>
        /// <param name="reaction">What the suspect does.</param>
        /// <param name="tags">Labels situations filter on. Built-in ones: "calm", "runs", "violent".</param>
        /// <param name="weight">Relative chance of being picked.</param>
        public CalloutOutcome(string id, SuspectReaction reaction, IEnumerable<string>? tags = null, double weight = 1)
        {
            Id = id;
            Reaction = reaction;
            Tags = TagList.Copy(tags);
            Weight = weight;
        }

        /// <summary>Unique id.</summary>
        public string Id { get; }
        /// <summary>What the suspect does.</summary>
        public SuspectReaction Reaction { get; }
        /// <summary>Labels situations filter on.</summary>
        public IReadOnlyList<string> Tags { get; }
        /// <summary>Relative chance of being picked.</summary>
        public double Weight { get; }
    }

    /// <summary>A suspect's reaction when the officer arrives.</summary>
    public enum SuspectReaction
    {
        /// <summary>Stays and cooperates.</summary>
        Comply,
        /// <summary>Runs.</summary>
        Flee,
        /// <summary>Attacks the officer.</summary>
        Fight,
    }

    internal static class TagList
    {
        public static IReadOnlyList<string> Copy(IEnumerable<string>? items) =>
            items == null ? Array.Empty<string>() : items.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToArray();
    }
}
