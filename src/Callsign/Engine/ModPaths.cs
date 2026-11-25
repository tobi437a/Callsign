using System.Diagnostics;
using System.IO;

namespace Callsign.Engine
{
    /// <summary>
    /// Where the mod keeps its files: &lt;GTA V&gt;\Callsign\. Deliberately outside scripts\,
    /// so SHVDN never tries to load plugin DLLs as scripts of its own.
    /// </summary>
    internal sealed class ModPaths
    {
        public string Home { get; }
        public string Plugins => Path.Combine(Home, "Plugins");
        public string Data => Path.Combine(Home, "Data");
        public string LogFile => Path.Combine(Home, "Callsign.log");

        ModPaths(string home) { Home = home; }

        public static ModPaths Detect()
        {
            string gameDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName)!;
            var paths = new ModPaths(Path.Combine(gameDir, "Callsign"));
            Directory.CreateDirectory(paths.Plugins);
            Directory.CreateDirectory(paths.Data);
            return paths;
        }
    }
}
