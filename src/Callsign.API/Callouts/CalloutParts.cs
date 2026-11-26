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
        /// <param name="dispatch">Dispatch text. <c>{location}</c>, <c>{suspect}</c> and <c>{count}</c> (number of suspects, as a word) are filled in.</param>
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

        /// <summary>
        /// The suspects are in a vehicle. Such a situation only uses suspects that have
        /// <see cref="CalloutSuspect.Vehicles"/>, and the others only use suspects on foot. Default false.
        /// Since API 0.4.
        /// </summary>
        public bool InVehicle { get; set; }

        /// <summary>Fewest suspects (1 to 4, default 1). Each one rolls their own outcome. Since API 0.4.</summary>
        public int MinSuspects { get; set; } = 1;

        /// <summary>Most suspects (<see cref="MinSuspects"/> to 4). 0 (default) = same as <see cref="MinSuspects"/>. Since API 0.4.</summary>
        public int MaxSuspects { get; set; }

        /// <summary>
        /// 0 to 1: how big a call this is, for pacing. Dispatch sends calm calls after intense ones and
        /// bigger ones after a quiet spell. Built-in: 0.15 suspicious person, 0.5 stolen vehicle,
        /// 0.9 shots fired from a vehicle. Default 0.3. Since API 0.5.
        /// </summary>
        public double Intensity { get; set; } = 0.3;

        /// <summary>
        /// Complications (twists) that can happen in this call: one fits if it has any of these tags.
        /// Empty (default) = no twists. Built-in tags: "foot", "store", "group", "robbery", "vehicle", "gang". Since API 0.6.
        /// </summary>
        public IReadOnlyList<string> ComplicationTags
        {
            get => _complicationTags;
            set => _complicationTags = TagList.Copy(value);
        }
        IReadOnlyList<string> _complicationTags = Array.Empty<string>();

        /// <summary>
        /// 0 to 1: chance the call gets a complication (when one fits). Dispatch lowers it on a tense shift
        /// and raises it after a quiet spell. Default 0.35. Since API 0.6.
        /// </summary>
        public double ComplicationChance { get; set; } = 0.35;
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

        /// <summary>
        /// Vehicle model names they drive, e.g. "sultan" (one is picked per call). Empty (default) = on foot.
        /// Suspects with vehicles only appear in situations with <see cref="CalloutSituation.InVehicle"/>.
        /// Since API 0.4.
        /// </summary>
        public IReadOnlyList<string> Vehicles
        {
            get => _vehicles;
            set => _vehicles = TagList.Copy(value);
        }
        IReadOnlyList<string> _vehicles = Array.Empty<string>();
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

    /// <summary>
    /// A twist that can happen partway through a call: more people show up, or a cooperating suspect
    /// suddenly runs or attacks. Dispatch doesn't mention it; the player finds out when it happens.
    /// Situations pick complications by tag (<see cref="CalloutSituation.ComplicationTags"/>). Since API 0.6.
    /// </summary>
    public sealed class CalloutComplication
    {
        /// <summary>Creates a complication.</summary>
        /// <param name="id">Unique id, prefixed with your plugin's name.</param>
        /// <param name="kind">What happens.</param>
        /// <param name="trigger">When it happens.</param>
        /// <param name="message">Notification shown when it happens (e.g. "~r~A second suspect is coming out of the store!~s~"), or null.</param>
        /// <param name="tags">Labels situations filter on.</param>
        /// <param name="weight">Relative chance of being picked among the fitting complications.</param>
        public CalloutComplication(string id, ComplicationKind kind, ComplicationTrigger trigger, string? message = null,
            IEnumerable<string>? tags = null, double weight = 1)
        {
            Id = id;
            Kind = kind;
            Trigger = trigger;
            Message = message;
            Tags = TagList.Copy(tags);
            Weight = weight;
        }

        /// <summary>Unique id.</summary>
        public string Id { get; }
        /// <summary>What happens.</summary>
        public ComplicationKind Kind { get; }
        /// <summary>When it happens.</summary>
        public ComplicationTrigger Trigger { get; }
        /// <summary>Notification shown when it happens, or null.</summary>
        public string? Message { get; }
        /// <summary>Labels situations filter on.</summary>
        public IReadOnlyList<string> Tags { get; }
        /// <summary>Relative chance of being picked.</summary>
        public double Weight { get; }

        /// <summary>Seconds after the player arrives, for <see cref="ComplicationTrigger.Delay"/>. Default 20.</summary>
        public double DelaySeconds { get; set; } = 20;

        /// <summary>
        /// <see cref="ComplicationKind.Accomplice"/>: how the newcomers react (null = rolled from the call's outcomes).
        /// <see cref="ComplicationKind.Panic"/>: <see cref="SuspectReaction.Flee"/> (default when null) or <see cref="SuspectReaction.Fight"/>.
        /// </summary>
        public SuspectReaction? Reaction { get; set; }

        /// <summary>Accomplices: how many show up (1 to 3, default 1).</summary>
        public int Count { get; set; } = 1;

        /// <summary>Accomplices: ped model names. Empty (default) = the same kind of person as the call's suspect.</summary>
        public IReadOnlyList<string> Models
        {
            get => _models;
            set => _models = TagList.Copy(value);
        }
        IReadOnlyList<string> _models = Array.Empty<string>();

        /// <summary>Accomplices: weapon they hold (e.g. "WEAPON_PISTOL"), or null for the call's suspect's weapon, if any.</summary>
        public string? Weapon { get; set; }
    }

    /// <summary>What a complication does. Since API 0.6.</summary>
    public enum ComplicationKind
    {
        /// <summary>More people involved show up near the suspect the player is dealing with.</summary>
        Accomplice,
        /// <summary>A suspect who was cooperating suddenly runs or attacks (a driver at the wheel drives off).</summary>
        Panic,
    }

    /// <summary>When a complication happens. Since API 0.6.</summary>
    public enum ComplicationTrigger
    {
        /// <summary>When the suspects first notice the officer.</summary>
        Arrival,
        /// <summary><see cref="CalloutComplication.DelaySeconds"/> after that.</summary>
        Delay,
        /// <summary>When the player stops a suspect (or a pulled-over vehicle comes to a stop).</summary>
        Stop,
        /// <summary>When the player arrests a suspect.</summary>
        Arrest,
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
