using System;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using Callsign.Core.Ports;

namespace Callsign.Core.Settings
{
    /// <summary>
    /// Settings as an XML file (<c>Callsign\settings.xml</c>, outside <c>Data\</c> so updating the
    /// mod never overwrites it). A broken file is logged and replaced by defaults on the next save.
    /// </summary>
    public sealed class SettingsFile : ISettingsStore
    {
        static readonly XmlSerializer Serializer = new XmlSerializer(typeof(ModSettings));

        readonly string _path;
        readonly ILog _log;

        public SettingsFile(string path, ILog log)
        {
            _path = path;
            _log = log;
        }

        public ModSettings Load()
        {
            if (!File.Exists(_path)) return new ModSettings();
            try
            {
                using (var reader = File.OpenText(_path))
                    return (ModSettings?)Serializer.Deserialize(reader) ?? new ModSettings();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                _log.Error("Could not read " + _path + "; using default settings", ex);
                return new ModSettings();
            }
        }

        public void Save(ModSettings settings)
        {
            try
            {
                // Write next to it first, so a crash mid-write never leaves half a file.
                string temp = _path + ".tmp";
                using (var writer = XmlWriter.Create(temp, new XmlWriterSettings { Indent = true }))
                    Serializer.Serialize(writer, settings);
                if (File.Exists(_path)) File.Delete(_path);
                File.Move(temp, _path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _log.Error("Could not save " + _path, ex);
            }
        }
    }
}
