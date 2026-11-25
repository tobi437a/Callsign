using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Callsign.API;
using Callsign.Core.Ports;

namespace Callsign.Plugins
{
    /// <summary>
    /// Finds and instantiates <see cref="Plugin"/> subclasses in &lt;GTA V&gt;\Callsign\Plugins\*.dll.
    /// A broken DLL is logged and skipped; it never stops the other plugins or the mod.
    /// </summary>
    /// <remarks>
    /// DLLs are loaded from bytes, not from the file, so they are never locked: plugin authors can
    /// overwrite their DLL while the game runs and press Insert (SHVDN then recreates the whole
    /// AppDomain, so the fresh copy is loaded). Assemblies loaded from bytes have no load context,
    /// so we resolve their references ourselves: Callsign.API → the copy already loaded, anything
    /// else → a DLL of that name in the plugins folder.
    /// </remarks>
    internal sealed class PluginLoader : IDisposable
    {
        static readonly string ApiAssemblyName = typeof(Plugin).Assembly.GetName().Name;

        readonly ILog _log;
        readonly string _directory;
        readonly Dictionary<string, Assembly> _loaded = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        public PluginLoader(string directory, ILog log)
        {
            _directory = directory;
            _log = log;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        public List<Plugin> LoadAll()
        {
            var plugins = new List<Plugin>();
            foreach (string file in Directory.GetFiles(_directory, "*.dll"))
            {
                try
                {
                    Assembly assembly = LoadFromBytes(file);
                    foreach (Type type in PluginTypes(assembly))
                    {
                        plugins.Add((Plugin)Activator.CreateInstance(type));
                        _log.Info("Found plugin " + type.FullName + " in " + Path.GetFileName(file));
                    }
                }
                catch (Exception ex)
                {
                    _log.Error("Could not load plugin DLL " + Path.GetFileName(file), ex);
                }
            }
            return plugins;
        }

        Assembly LoadFromBytes(string file)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (_loaded.TryGetValue(name, out var existing)) return existing;   // already pulled in as a dependency

            string pdb = Path.ChangeExtension(file, ".pdb");
            Assembly assembly = File.Exists(pdb)
                ? Assembly.Load(File.ReadAllBytes(file), File.ReadAllBytes(pdb))   // line numbers in stack traces
                : Assembly.Load(File.ReadAllBytes(file));
            _loaded[name] = assembly;
            return assembly;
        }

        static IEnumerable<Type> PluginTypes(Assembly assembly)
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }

            return types.Where(t => typeof(Plugin).IsAssignableFrom(t)
                                    && t.IsClass && !t.IsAbstract && t.IsPublic
                                    && t.GetConstructor(Type.EmptyTypes) != null);
        }

        Assembly? Resolve(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            if (name == ApiAssemblyName) return typeof(Plugin).Assembly;

            string candidate = Path.Combine(_directory, name + ".dll");
            return File.Exists(candidate) ? LoadFromBytes(candidate) : null;
        }

        public void Dispose()
        {
            AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
        }
    }
}
