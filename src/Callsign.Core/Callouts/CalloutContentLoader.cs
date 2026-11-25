using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Callsign.Core.Ports;
using Callsign.Core.World;

namespace Callsign.Core.Callouts
{
    /// <summary>
    /// Reads callout parts from XML (<c>Callsign\Data\Callouts\*.xml</c>) into the registries.
    /// A bad entry is logged and skipped; the rest of the file still loads.
    /// </summary>
    /// <example>
    /// <code>
    /// &lt;CalloutContent&gt;
    ///   &lt;Situation id="suspicious-person" title="Suspicious person" locationTags="store"&gt;
    ///     &lt;Dispatch&gt;Caller reports a {suspect} hanging around {location}.&lt;/Dispatch&gt;
    ///   &lt;/Situation&gt;
    ///   &lt;Location id="sandy-liquor" name="Scoops Liquor, Sandy Shores" x="1392" y="3604" z="34" tags="store,county" /&gt;
    ///   &lt;Suspect id="hoodie" description="man in a grey hoodie" models="a_m_y_genstreet_01" /&gt;
    ///   &lt;Outcome id="runs" behaviour="Flee" weight="2" /&gt;
    /// &lt;/CalloutContent&gt;
    /// </code>
    /// </example>
    public static class CalloutContentLoader
    {
        static readonly XmlSerializer Serializer = new XmlSerializer(typeof(CalloutContentXml));

        /// <summary>Loads every <c>*.xml</c> in <paramref name="directory"/> (if it exists). Returns the number of parts added.</summary>
        public static int LoadDirectory(string directory, CalloutParts parts, ILog log)
        {
            if (!Directory.Exists(directory)) return 0;
            int added = 0;
            foreach (string file in Directory.GetFiles(directory, "*.xml").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    using (var reader = File.OpenText(file))
                        added += Load(reader, Path.GetFileName(file), parts, log);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    log.Error("Could not read " + file, ex);
                }
            }
            return added;
        }

        /// <summary>Loads one XML document. Returns the number of parts added.</summary>
        public static int Load(TextReader xml, string sourceName, CalloutParts parts, ILog log)
        {
            CalloutContentXml content;
            try { content = (CalloutContentXml)Serializer.Deserialize(xml); }
            catch (InvalidOperationException ex)
            {
                log.Error(sourceName + ": not valid callout content XML", ex.InnerException ?? ex);
                return 0;
            }

            int added = 0;
            foreach (var s in content.Situations)
                added += TryAdd(sourceName, "situation", s.Id, log, () =>
                {
                    Require(s.Title, "title");
                    Require(s.Dispatch, "Dispatch");
                    parts.Situations.Add(new SituationPart(s.Id!, s.Title!, s.Dispatch!.Trim(),
                        Tags(s.LocationTags), Tags(s.SuspectTags), Tags(s.OutcomeTags), Tags(s.Tags), s.Weight));
                });
            foreach (var l in content.Locations)
                added += TryAdd(sourceName, "location", l.Id, log, () =>
                {
                    Require(l.Name, "name");
                    parts.Locations.Add(new LocationPart(l.Id!, l.Name!, new Position(l.X, l.Y, l.Z), Tags(l.Tags), l.Weight));
                });
            foreach (var p in content.Suspects)
                added += TryAdd(sourceName, "suspect", p.Id, log, () =>
                {
                    Require(p.Description, "description");
                    var models = Tags(p.Models);
                    if (models.Count == 0) throw new FormatException("needs at least one model");
                    parts.Suspects.Add(new SuspectPart(p.Id!, p.Description!, models,
                        string.IsNullOrWhiteSpace(p.Weapon) ? null : p.Weapon!.Trim(), Tags(p.Tags), p.Weight));
                });
            foreach (var o in content.Outcomes)
                added += TryAdd(sourceName, "outcome", o.Id, log, () =>
                {
                    if (!Enum.TryParse(o.Behaviour, true, out SuspectBehaviour behaviour) || !Enum.IsDefined(typeof(SuspectBehaviour), behaviour))
                        throw new FormatException("unknown behaviour '" + o.Behaviour + "' (use " + string.Join(", ", Enum.GetNames(typeof(SuspectBehaviour))) + ")");
                    parts.Outcomes.Add(new OutcomePart(o.Id!, behaviour, Tags(o.Tags), o.Weight));
                });
            return added;
        }

        static int TryAdd(string source, string kind, string? id, ILog log, Action add)
        {
            try
            {
                add();
                return 1;
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException)
            {
                log.Warn(source + ": skipped " + kind + " '" + (id ?? "?") + "': " + ex.Message);
                return 0;
            }
        }

        static void Require(string? value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new FormatException("missing " + name);
        }

        static IReadOnlyList<string> Tags(string? csv) =>
            (csv ?? "").Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToArray();
    }

    // XmlSerializer needs public mutable classes; these stay a private detail of the file format.
    [XmlRoot("CalloutContent")]
    public sealed class CalloutContentXml
    {
        [XmlElement("Situation")] public List<SituationXml> Situations { get; set; } = new List<SituationXml>();
        [XmlElement("Location")] public List<LocationXml> Locations { get; set; } = new List<LocationXml>();
        [XmlElement("Suspect")] public List<SuspectXml> Suspects { get; set; } = new List<SuspectXml>();
        [XmlElement("Outcome")] public List<OutcomeXml> Outcomes { get; set; } = new List<OutcomeXml>();
    }

    public abstract class PartXml
    {
        [XmlAttribute("id")] public string? Id { get; set; }
        [XmlAttribute("weight")] public double Weight { get; set; } = 1;
        [XmlAttribute("tags")] public string? Tags { get; set; }
    }

    public sealed class SituationXml : PartXml
    {
        [XmlAttribute("title")] public string? Title { get; set; }
        [XmlAttribute("locationTags")] public string? LocationTags { get; set; }
        [XmlAttribute("suspectTags")] public string? SuspectTags { get; set; }
        [XmlAttribute("outcomeTags")] public string? OutcomeTags { get; set; }
        [XmlElement("Dispatch")] public string? Dispatch { get; set; }
    }

    public sealed class LocationXml : PartXml
    {
        [XmlAttribute("name")] public string? Name { get; set; }
        [XmlAttribute("x")] public float X { get; set; }
        [XmlAttribute("y")] public float Y { get; set; }
        [XmlAttribute("z")] public float Z { get; set; }
    }

    public sealed class SuspectXml : PartXml
    {
        [XmlAttribute("description")] public string? Description { get; set; }
        [XmlAttribute("models")] public string? Models { get; set; }
        [XmlAttribute("weapon")] public string? Weapon { get; set; }
    }

    public sealed class OutcomeXml : PartXml
    {
        [XmlAttribute("behaviour")] public string? Behaviour { get; set; }
    }
}
