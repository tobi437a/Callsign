using System.Collections.Generic;
using System.Linq;
using Callsign.Core.Callouts;
using Callsign.Core.Duty;
using Callsign.Core.Entities;
using Callsign.Core.Events;
using Callsign.Core.Ports;
using Callsign.Core.Suspects;

namespace Callsign.Core.TrafficStops
{
    /// <summary>Distances in metres.</summary>
    public sealed class TrafficStopSettings
    {
        /// <summary>How far ahead the vehicle to pull over can be.</summary>
        public float MaxTargetDistance { get; set; } = 30;
        /// <summary>How long the "hold to pull over" hint shows after the lights go on.</summary>
        public long HintMs { get; set; } = 8_000;
        /// <summary>The stop is over once the player is this far from it (they drove off).</summary>
        public float AbandonDistance { get; set; } = 250;

        public StopReactionChances Reactions { get; set; } = new StopReactionChances();
    }

    /// <summary>What the player pressed this frame, for traffic stops.</summary>
    /// <param name="Initiate">The pull-over key has just been held long enough.</param>
    public readonly record struct TrafficStopInput(bool Initiate, CalloutInput Scene);

    /// <summary>
    /// Traffic stops: on duty, in a vehicle with the emergency lights on, the player picks the
    /// vehicle ahead. Its people get hidden traits like any suspect, and those decide whether the
    /// driver pulls over or runs. From there it plays out like a vehicle callout (same scene:
    /// approach, interaction menu with a plate check and citation, arrest, pursuit).
    /// </summary>
    public sealed class TrafficStopService
    {
        public const string Activity = "traffic stop";
        public const string LightsHelp = "Hold ~INPUT_SPRINT~ to pull over the vehicle in front.";

        readonly DutyService _duty;
        readonly IEventBus _bus;
        readonly ITraffic _traffic;
        readonly CalloutSceneServices _s;
        readonly TrafficStopSettings _settings;
        CalloutScene? _scene;
        string _vehicleName = "";
        long _lightsOnSince = -1;

        public TrafficStopService(DutyService duty, IEventBus bus, ITraffic traffic, CalloutSceneServices services,
            TrafficStopSettings? settings = null)
        {
            _duty = duty;
            _bus = bus;
            _traffic = traffic;
            _s = services;
            _settings = settings ?? new TrafficStopSettings();
        }

        public bool Active => _scene != null;

        /// <summary>The running stop's scene, for dev tools.</summary>
        internal CalloutScene? Scene => _scene;

        public void Tick(TrafficStopInput input)
        {
            if (_scene == null)
            {
                if (!_duty.OnDuty || _duty.Busy || !_s.World.PlayerInVehicle || !_s.World.PlayerLightsOn)
                {
                    _lightsOnSince = -1;
                    return;
                }
                if (_lightsOnSince < 0) _lightsOnSince = _s.Clock.NowMs;
                if (input.Initiate) TryStart();
                else if (_s.Clock.NowMs - _lightsOnSince < _settings.HintMs) _s.Notifier.Help(LightsHelp);
                return;
            }

            if (!_duty.OnDuty)
            {
                Finish(TrafficStopResult.Cancelled);
                return;
            }
            if (_s.World.PlayerPosition.DistanceTo(_scene.Focus) > _settings.AbandonDistance)
            {
                _s.Notifier.Notify("You left the traffic stop.");
                Finish(TrafficStopResult.Cancelled);
                return;
            }
            if (_scene.Tick(input.Scene) != null) Finish(Result(_scene));
        }

        /// <summary>Pulls over the vehicle ahead, if there is one. False if no stop started.</summary>
        public bool TryStart()
        {
            if (_scene != null || !_s.World.PlayerInVehicle || !_duty.TryBegin(Activity)) return false;

            EntityScope scope = _s.Scopes.Open("traffic stop");
            if (!_traffic.TryTakeVehicleAhead(scope, _settings.MaxTargetDistance, out IVehicle? vehicle, out IReadOnlyList<IPed> occupants)
                || occupants.Count == 0)
            {
                scope.Close(ReleaseMode.Dismiss);
                _duty.End(Activity);
                _s.Notifier.Help("No vehicle ahead to pull over.");
                return false;
            }

            var people = new List<CalloutScene.Occupant>();
            SuspectProfile? driver = null;
            VehicleRecord? record = null;
            foreach (IPed ped in occupants)
            {
                SuspectProfile profile = _s.Profiles.Generate(ped.IsMale, System.Array.Empty<string>(), visiblyArmed: false);
                if (profile.ConcealedWeapon != null) ped.GiveWeapon(profile.ConcealedWeapon, equip: false);
                if (driver == null)
                {
                    driver = profile;
                    record = new VehicleRecordGenerator(_s.Profiles, _s.Random, _s.VehicleRecords)
                        .Generate(vehicle!.Plate, vehicle.Model, profile, System.Array.Empty<string>());
                }
                people.Add(new CalloutScene.Occupant(ped, profile,
                    StopReaction.Pick(profile, record!.Stolen, _s.Random, _settings.Reactions)));
            }

            _vehicleName = vehicle!.Model;
            _s.Log.Info("Traffic stop: " + vehicle.Model + " " + vehicle.Plate + ", " + people.Count + " occupant(s), "
                + string.Join("/", people.Select(p => p.Behaviour + "[" + p.Profile.Traits + "]")) + (record!.Stolen ? ", stolen" : ""));
            _scene = CalloutScene.AroundVehicle(vehicle, people, record, scope, _s);
            _s.Notifier.Notify("~b~Traffic stop:~s~ " + vehicle.Model + ", plate ~y~" + vehicle.Plate + "~s~.");
            _bus.Publish(new TrafficStopStarted(vehicle.Model, vehicle.Plate, people.Count));
            return true;
        }

        /// <summary>The player calls off the stop. False if none is running.</summary>
        public bool Cancel()
        {
            if (_scene == null) return false;
            _s.Notifier.Notify("Traffic stop ~o~cancelled~s~.");
            Finish(TrafficStopResult.Cancelled);
            return true;
        }

        /// <summary>Script reload/unload: delete the scene at once. No events.</summary>
        public void Stop()
        {
            _scene?.Close(ReleaseMode.Delete);
            _scene = null;
            _duty.End(Activity);
        }

        /// <summary>One line on the stop, for dev tools.</summary>
        internal string Describe() => _scene == null ? "none" : _vehicleName + " | " + _scene.Describe();

        static TrafficStopResult Result(CalloutScene scene)
        {
            var results = scene.Results.ToList();
            if (results.Contains(SuspectResult.Arrested)) return TrafficStopResult.Arrested;
            if (results.Contains(SuspectResult.Cited)) return TrafficStopResult.Cited;
            if (results.Contains(SuspectResult.Released)) return TrafficStopResult.Warned;
            if (results.Contains(SuspectResult.Died)) return TrafficStopResult.SuspectDied;
            return TrafficStopResult.Escaped;
        }

        void Finish(TrafficStopResult result)
        {
            _scene!.Close(ReleaseMode.Dismiss);
            _scene = null;
            _duty.End(Activity);
            _bus.Publish(new TrafficStopEnded(_vehicleName, result));
        }
    }
}
