using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Callsign.Core.Ports;

namespace Callsign.Core.Suspects
{
    /// <summary>Things a suspect can say when asked something.</summary>
    public enum DialogueTopic
    {
        /// <summary>First words when stopped.</summary>
        Stopped,
        /// <summary>"What are you doing here?"</summary>
        Purpose,
        /// <summary>"Have you been drinking or taken anything?"</summary>
        Drinking,
        /// <summary>Asked for ID and has one.</summary>
        HandsId,
        /// <summary>Asked for ID and has none. <c>{name}</c> is the name they give.</summary>
        NoId,
        /// <summary>Told to step out of their vehicle.</summary>
        StepOut,
    }

    /// <summary>Where the suspect was when the officer stopped them.</summary>
    public enum DialogueContext
    {
        /// <summary>A line anyone may say.</summary>
        Any,
        /// <summary>Stopped on foot.</summary>
        OnFoot,
        /// <summary>Stopped at the wheel (or in a vehicle).</summary>
        InVehicle,
    }

    /// <summary>
    /// A line of dialogue. <paramref name="Trait"/> = None means anyone may say it;
    /// <paramref name="Context"/> = Any means wherever they were stopped.
    /// </summary>
    public sealed record DialogueLine(DialogueTopic Topic, SuspectTraits Trait, string Text,
        DialogueContext Context = DialogueContext.Any);

    /// <summary>A weapon a suspect can hide: game name and how a search describes it.</summary>
    public sealed record HiddenWeapon(string Weapon, string Name);

    /// <summary>The word lists suspects are built from. Loaded from <c>Data\Suspects\*.xml</c>; files add up.</summary>
    public sealed class SuspectContent
    {
        public List<string> MaleNames { get; } = new List<string>();
        public List<string> FemaleNames { get; } = new List<string>();
        public List<string> LastNames { get; } = new List<string>();
        public List<string> WarrantOffences { get; } = new List<string>();
        public List<string> PocketItems { get; } = new List<string>();
        public List<string> DrugItems { get; } = new List<string>();
        public List<HiddenWeapon> HiddenWeapons { get; } = new List<HiddenWeapon>();
        public List<DialogueLine> Lines { get; } = new List<DialogueLine>();

        /// <summary>Lists the generator can't do without that are empty. Empty result = usable.</summary>
        public IEnumerable<string> Missing()
        {
            if (MaleNames.Count == 0) yield return "male names";
            if (FemaleNames.Count == 0) yield return "female names";
            if (LastNames.Count == 0) yield return "last names";
            if (WarrantOffences.Count == 0) yield return "warrant offences";
            if (DrugItems.Count == 0) yield return "drug items";
            if (HiddenWeapons.Count == 0) yield return "hidden weapons";
        }
    }

    /// <summary>
    /// Reads <see cref="SuspectContent"/> from XML. Lists are comma-separated; a bad line is logged and skipped.
    /// </summary>
    /// <example>
    /// <code>
    /// &lt;SuspectContent&gt;
    ///   &lt;MaleNames&gt;James, Carlos&lt;/MaleNames&gt;
    ///   &lt;Warrants&gt;failure to appear, unpaid fines&lt;/Warrants&gt;
    ///   &lt;HiddenWeapon weapon="WEAPON_SWITCHBLADE" name="a switchblade" /&gt;
    ///   &lt;Line topic="Purpose" trait="Nervous"&gt;N-nothing. Why?&lt;/Line&gt;
    /// &lt;/SuspectContent&gt;
    /// </code>
    /// </example>
    public static class SuspectContentLoader
    {
        static readonly XmlSerializer Serializer = new XmlSerializer(typeof(SuspectContentXml));

        public static void LoadDirectory(string directory, SuspectContent content, ILog log)
        {
            if (!Directory.Exists(directory)) return;
            foreach (string file in Directory.GetFiles(directory, "*.xml").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    using (var reader = File.OpenText(file))
                        Load(reader, Path.GetFileName(file), content, log);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    log.Error("Could not read " + file, ex);
                }
            }
        }

        public static void Load(TextReader xml, string sourceName, SuspectContent content, ILog log)
        {
            SuspectContentXml x;
            try { x = (SuspectContentXml)Serializer.Deserialize(xml); }
            catch (InvalidOperationException ex)
            {
                log.Error(sourceName + ": not valid suspect content XML", ex.InnerException ?? ex);
                return;
            }

            content.MaleNames.AddRange(x.MaleNames.SelectMany(List));
            content.FemaleNames.AddRange(x.FemaleNames.SelectMany(List));
            content.LastNames.AddRange(x.LastNames.SelectMany(List));
            content.WarrantOffences.AddRange(x.Warrants.SelectMany(List));
            content.PocketItems.AddRange(x.PocketItems.SelectMany(List));
            content.DrugItems.AddRange(x.DrugItems.SelectMany(List));

            foreach (var w in x.HiddenWeapons)
            {
                if (string.IsNullOrWhiteSpace(w.Weapon) || string.IsNullOrWhiteSpace(w.Name))
                    log.Warn(sourceName + ": skipped hidden weapon without weapon or name");
                else
                    content.HiddenWeapons.Add(new HiddenWeapon(w.Weapon!.Trim(), w.Name!.Trim()));
            }

            foreach (var l in x.Lines)
            {
                SuspectTraits trait = SuspectTraits.None;
                DialogueContext context = DialogueContext.Any;
                string text = (l.Text ?? "").Trim();
                if (!Enum.TryParse(l.Topic, true, out DialogueTopic topic) || !Enum.IsDefined(typeof(DialogueTopic), topic))
                    log.Warn(sourceName + ": skipped line with unknown topic '" + l.Topic + "'");
                else if (!string.IsNullOrWhiteSpace(l.Trait) && !Enum.TryParse(l.Trait, true, out trait))
                    log.Warn(sourceName + ": skipped line with unknown trait '" + l.Trait + "'");
                else if (!string.IsNullOrWhiteSpace(l.Context) && !TryParseContext(l.Context!, out context))
                    log.Warn(sourceName + ": skipped line with unknown context '" + l.Context + "' (use foot or vehicle)");
                else if (text.Length == 0)
                    log.Warn(sourceName + ": skipped empty " + topic + " line");
                else
                    content.Lines.Add(new DialogueLine(topic, trait, text, context));
            }
        }

        static bool TryParseContext(string value, out DialogueContext context)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "foot": context = DialogueContext.OnFoot; return true;
                case "vehicle": context = DialogueContext.InVehicle; return true;
                default: context = DialogueContext.Any; return false;
            }
        }

        static IEnumerable<string> List(string? csv) =>
            (csv ?? "").Split(',').Select(t => t.Trim()).Where(t => t.Length > 0);
    }

    // XmlSerializer needs public mutable classes; these stay a private detail of the file format.
    [XmlRoot("SuspectContent")]
    public sealed class SuspectContentXml
    {
        [XmlElement("MaleNames")] public List<string> MaleNames { get; set; } = new List<string>();
        [XmlElement("FemaleNames")] public List<string> FemaleNames { get; set; } = new List<string>();
        [XmlElement("LastNames")] public List<string> LastNames { get; set; } = new List<string>();
        [XmlElement("Warrants")] public List<string> Warrants { get; set; } = new List<string>();
        [XmlElement("PocketItems")] public List<string> PocketItems { get; set; } = new List<string>();
        [XmlElement("DrugItems")] public List<string> DrugItems { get; set; } = new List<string>();
        [XmlElement("HiddenWeapon")] public List<HiddenWeaponXml> HiddenWeapons { get; set; } = new List<HiddenWeaponXml>();
        [XmlElement("Line")] public List<LineXml> Lines { get; set; } = new List<LineXml>();
    }

    public sealed class HiddenWeaponXml
    {
        [XmlAttribute("weapon")] public string? Weapon { get; set; }
        [XmlAttribute("name")] public string? Name { get; set; }
    }

    public sealed class LineXml
    {
        [XmlAttribute("topic")] public string? Topic { get; set; }
        [XmlAttribute("trait")] public string? Trait { get; set; }
        [XmlAttribute("context")] public string? Context { get; set; }
        [XmlText] public string? Text { get; set; }
    }
}
