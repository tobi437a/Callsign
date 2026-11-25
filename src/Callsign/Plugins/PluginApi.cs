using System;
using System.Collections.Generic;
using System.Reflection;
using Callsign.API;
using Callsign.API.Events;
using Callsign.Core.Duty;
using Callsign.Core.Events;
using Callsign.Core.Ports;

namespace Callsign.Plugins
{
    /// <summary>
    /// One plugin's view of the mod: a facade (anti-corruption layer) that translates internal
    /// Core events/services into the public Callsign.API types. Internals can change freely as
    /// long as this translation keeps the public contract. Disposing detaches everything the
    /// plugin subscribed to.
    /// </summary>
    internal sealed class PluginApi : ICallsignApi, IDutyApi, IDisposable
    {
        // AssemblyVersion is kept fixed (see Callsign.API.csproj); the real semver is the file version.
        public static readonly Version CurrentApiVersion = new Version(
            typeof(ICallsignApi).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version);

        readonly DutyService _duty;
        readonly ILog _log;
        readonly List<IDisposable> _subscriptions = new List<IDisposable>();

        public PluginApi(string pluginName, DutyService duty, IEventBus bus, ILog log)
        {
            _duty = duty;
            _log = log;
            Log = new PrefixedLog(pluginName, log);
            _subscriptions.Add(bus.Subscribe<DutyChanged>(e => Raise(DutyChanged, new DutyChangedEventArgs(e.OnDuty))));
        }

        public Version ApiVersion => CurrentApiVersion;
        public IDutyApi Duty => this;
        public IPluginLog Log { get; }

        // IDutyApi
        public bool IsOnDuty => _duty.OnDuty;
        public void SetOnDuty(bool onDuty) => _duty.SetOnDuty(onDuty);
        public event EventHandler<DutyChangedEventArgs>? DutyChanged;

        void Raise<T>(EventHandler<T>? handler, T args) where T : EventArgs
        {
            if (handler == null) return;
            foreach (EventHandler<T> h in handler.GetInvocationList())
            {
                try { h(this, args); }
                catch (Exception ex) { Log.Error("event handler threw", ex); }
            }
        }

        public void Dispose()
        {
            foreach (var s in _subscriptions) s.Dispose();
            _subscriptions.Clear();
            DutyChanged = null;
        }

        sealed class PrefixedLog : IPluginLog
        {
            readonly string _prefix;
            readonly ILog _log;
            public PrefixedLog(string name, ILog log) { _prefix = "[" + name + "] "; _log = log; }
            public void Info(string message) => _log.Info(_prefix + message);
            public void Warn(string message) => _log.Warn(_prefix + message);
            public void Error(string message, Exception? exception = null) => _log.Error(_prefix + message, exception);
        }
    }
}
