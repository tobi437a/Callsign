using System;
using System.Collections.Generic;
using System.Reflection;
using Callsign.API;
using Callsign.API.Callouts;
using Callsign.API.Events;
using Callsign.Core.Callouts;
using Callsign.Core.Duty;
using Callsign.Core.Events;
using Callsign.Core.Ports;
using Callsign.Core.World;
using Callsign.Engine;
using ApiEndReason = Callsign.API.Events.CalloutEndReason;
using CoreEndReason = Callsign.Core.Callouts.CalloutEndReason;

namespace Callsign.Plugins
{
    /// <summary>
    /// One plugin's view of the mod: a facade (anti-corruption layer) that translates internal
    /// Core events/services into the public Callsign.API types. Internals can change freely as
    /// long as this translation keeps the public contract. Disposing detaches everything the
    /// plugin subscribed to and removes every callout part it added.
    /// </summary>
    internal sealed class PluginApi : ICallsignApi, IDutyApi, ICalloutsApi, IDisposable
    {
        // AssemblyVersion is kept fixed (see Callsign.API.csproj); the real semver is the file version.
        public static readonly Version CurrentApiVersion = new Version(
            typeof(ICallsignApi).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version);

        readonly DutyService _duty;
        readonly CalloutParts _parts;
        readonly CalloutService _callouts;
        readonly List<IDisposable> _owned = new List<IDisposable>();   // bus subscriptions + added parts

        public PluginApi(string pluginName, DutyService duty, CalloutParts parts, CalloutService callouts, IEventBus bus, ILog log)
        {
            _duty = duty;
            _parts = parts;
            _callouts = callouts;
            Log = new PrefixedLog(pluginName, log);
            _owned.Add(bus.Subscribe<DutyChanged>(e => Raise(DutyChanged, new DutyChangedEventArgs(e.OnDuty))));
            _owned.Add(bus.Subscribe<Core.Callouts.CalloutOffered>(e => Raise(CalloutOffered, new CalloutEventArgs(ToInfo(e.Incident)))));
            _owned.Add(bus.Subscribe<Core.Callouts.CalloutAccepted>(e => Raise(CalloutAccepted, new CalloutEventArgs(ToInfo(e.Incident)))));
            _owned.Add(bus.Subscribe<Core.Callouts.CalloutEnded>(e => Raise(CalloutEnded, new CalloutEndedEventArgs(ToInfo(e.Incident), ToApi(e.Reason)))));
        }

        public Version ApiVersion => CurrentApiVersion;
        public IDutyApi Duty => this;
        public ICalloutsApi Callouts => this;
        public IPluginLog Log { get; }

        // IDutyApi
        public bool IsOnDuty => _duty.OnDuty;
        public void SetOnDuty(bool onDuty) => _duty.SetOnDuty(onDuty);
        public event EventHandler<DutyChangedEventArgs>? DutyChanged;

        // ICalloutsApi
        public IDisposable AddSituation(CalloutSituation s) => Own(_parts.Situations.Add(new SituationPart(
            s.Id, s.Title, s.Dispatch, s.LocationTags, s.SuspectTags, s.OutcomeTags, Array.Empty<string>(), s.Weight)
        {
            InVehicle = s.InVehicle,
            MinSuspects = s.MinSuspects,
            MaxSuspects = s.MaxSuspects == 0 ? s.MinSuspects : s.MaxSuspects,
        }));

        public IDisposable AddLocation(CalloutLocation l) => Own(_parts.Locations.Add(new LocationPart(
            l.Id, l.Name, new Position(l.Position.X, l.Position.Y, l.Position.Z), l.Tags, l.Weight)));

        public IDisposable AddSuspect(CalloutSuspect p) => Own(_parts.Suspects.Add(new SuspectPart(
            p.Id, p.Description, p.Models, string.IsNullOrWhiteSpace(p.Weapon) ? null : p.Weapon, p.Tags, p.Weight)
        {
            Vehicles = p.Vehicles,
        }));

        public IDisposable AddOutcome(CalloutOutcome o) => Own(_parts.Outcomes.Add(new OutcomePart(
            o.Id, ToCore(o.Reaction), o.Tags, o.Weight)));

        public CalloutInfo? Current => _callouts.Incident == null ? null : ToInfo(_callouts.Incident);

        public event EventHandler<CalloutEventArgs>? CalloutOffered;
        public event EventHandler<CalloutEventArgs>? CalloutAccepted;
        public event EventHandler<CalloutEndedEventArgs>? CalloutEnded;

        IDisposable Own(IDisposable handle)
        {
            _owned.Add(handle);
            return handle;
        }

        static CalloutInfo ToInfo(Incident i) => new CalloutInfo(i.Title, i.DispatchMessage, i.Situation.Id,
            i.Location.Id, i.Location.Name, i.Location.Position.ToVector(), i.Suspect.Id, i.Outcome.Id);

        static SuspectBehaviour ToCore(SuspectReaction reaction)
        {
            switch (reaction)
            {
                case SuspectReaction.Comply: return SuspectBehaviour.Comply;
                case SuspectReaction.Flee: return SuspectBehaviour.Flee;
                case SuspectReaction.Fight: return SuspectBehaviour.Fight;
                default: throw new ArgumentException("Unknown suspect reaction " + reaction, nameof(reaction));
            }
        }

        static ApiEndReason ToApi(CoreEndReason reason)
        {
            switch (reason)
            {
                case CoreEndReason.Declined: return ApiEndReason.Declined;
                case CoreEndReason.OfferExpired: return ApiEndReason.OfferExpired;
                case CoreEndReason.Resolved: return ApiEndReason.Resolved;
                case CoreEndReason.SuspectEscaped: return ApiEndReason.SuspectEscaped;
                case CoreEndReason.SuspectDied: return ApiEndReason.SuspectDied;
                case CoreEndReason.Cancelled: return ApiEndReason.Cancelled;
                case CoreEndReason.Arrested: return ApiEndReason.Arrested;
                default: return ApiEndReason.Failed;
            }
        }

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
            foreach (var d in _owned) d.Dispose();
            _owned.Clear();
            DutyChanged = null;
            CalloutOffered = null;
            CalloutAccepted = null;
            CalloutEnded = null;
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
