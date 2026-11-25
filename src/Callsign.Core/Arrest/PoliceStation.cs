using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Callsign.Core.Ports;
using Callsign.Core.World;

namespace Callsign.Core.Arrest
{
    /// <summary>A place where prisoners are booked. Driving them within range of it books them.</summary>
    public sealed record PoliceStation(string Id, string Name, Position Position)
    {
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("missing id");
            if (string.IsNullOrWhiteSpace(Name)) throw new ArgumentException("missing name");
        }

        /// <summary>The station closest to <paramref name="from"/>, or null if there are none.</summary>
        public static PoliceStation? Nearest(IEnumerable<PoliceStation> stations, Position from) =>
            stations.OrderBy(s => s.Position.DistanceTo(from)).FirstOrDefault();
    }

    /// <summary>
    /// Reads stations from XML (<c>Callsign\Data\Arrest\*.xml</c>). A bad entry is logged and skipped.
    /// </summary>
    /// <example>
    /// <code>
    /// &lt;Stations&gt;
    ///   &lt;Station id="mission-row" name="Mission Row Police Station" x="441" y="-985" z="30" /&gt;
    /// &lt;/Stations&gt;
    /// </code>
    /// </example>
    public static class StationLoader
    {
        static readonly XmlSerializer Serializer = new XmlSerializer(typeof(StationsXml));

        public static List<PoliceStation> LoadDirectory(string directory, ILog log)
        {
            var stations = new List<PoliceStation>();
            if (!Directory.Exists(directory)) return stations;
            foreach (string file in Directory.GetFiles(directory, "*.xml").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    using (var reader = File.OpenText(file))
                        stations.AddRange(Load(reader, Path.GetFileName(file), log));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    log.Error("Could not read " + file, ex);
                }
            }
            return stations;
        }

        public static List<PoliceStation> Load(TextReader xml, string sourceName, ILog log)
        {
            var stations = new List<PoliceStation>();
            StationsXml content;
            try { content = (StationsXml)Serializer.Deserialize(xml); }
            catch (InvalidOperationException ex)
            {
                log.Error(sourceName + ": not valid stations XML", ex.InnerException ?? ex);
                return stations;
            }

            foreach (var s in content.Stations)
            {
                var station = new PoliceStation(s.Id ?? "", s.Name ?? "", new Position(s.X, s.Y, s.Z));
                try
                {
                    station.Validate();
                    stations.Add(station);
                }
                catch (ArgumentException ex)
                {
                    log.Warn(sourceName + ": skipped station '" + (s.Id ?? "?") + "': " + ex.Message);
                }
            }
            return stations;
        }
    }

    // XmlSerializer needs public mutable classes; these stay a private detail of the file format.
    [XmlRoot("Stations")]
    public sealed class StationsXml
    {
        [XmlElement("Station")] public List<StationXml> Stations { get; set; } = new List<StationXml>();
    }

    public sealed class StationXml
    {
        [XmlAttribute("id")] public string? Id { get; set; }
        [XmlAttribute("name")] public string? Name { get; set; }
        [XmlAttribute("x")] public float X { get; set; }
        [XmlAttribute("y")] public float Y { get; set; }
        [XmlAttribute("z")] public float Z { get; set; }
    }
}
