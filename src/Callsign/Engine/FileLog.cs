using System;
using System.IO;
using Callsign.Core.Ports;

namespace Callsign.Engine
{
    /// <summary>Writes Callsign.log. Started fresh on every load (each Insert reload).</summary>
    internal sealed class FileLog : ILog
    {
        readonly string _path;

        public FileLog(string path)
        {
            _path = path;
            File.WriteAllText(_path, "Callsign log, started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine);
        }

        public void Info(string message) => Write("INFO ", message);
        public void Warn(string message) => Write("WARN ", message);

        public void Error(string message, Exception? exception = null) =>
            Write("ERROR", exception == null ? message : message + Environment.NewLine + exception);

        void Write(string level, string message)
        {
            try { File.AppendAllText(_path, "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + level + " " + message + Environment.NewLine); }
            catch (IOException) { /* logging must never break the mod */ }
        }
    }
}
