using System.Linq;
using Callsign.Core.Callouts;
using Callsign.Core.Duty;
using Callsign.Core.Entities;
using Callsign.Core.Events;
using Callsign.Core.Ports;
using Callsign.Core.Suspects;

namespace Callsign.Core.PedStops
{
    /// <summary>Distances in metres.</summary>
    public sealed class PedStopSettings
    {
        /// <summary>How close (on foot, roughly in front) the pedestrian to stop must be.</summary>
        public float MaxTargetDistance { get; set; } = 4;
        /// <summary>The stop is over once the player is this far from it (they walked or drove off).</summary>
        public float AbandonDistance { get; set; } = 120;

        public StopReactionChances Reactions { get; set; } = new StopReactionChances();
    }

    /// <summary>
    /// Pedestrian stops: on duty and on foot, the player holds the interact key next to someone.
    /// They get hidden traits like any suspect, and those decide whether they stand and talk, run
    /// or (armed and wanted) fight. From there it is the same scene and interaction menu as a
    /// callout suspect (ID, questions, records check, tests, pat-down, arrest or let go).
    /// </summary>
    public sealed class PedStopService
    {
        public const string Activity = "ped stop";
        public const string StopHelp = "Hold ~INPUT_CONTEXT~ to stop the pedestrian";

        readonly DutyService _duty;
        readonly IEventBus _bus;
        readonly IPedestrians _pedestrians;
        readonly CalloutSceneServices _s;
        readonly PedStopSettings _settings;
        CalloutScene? _scene;

        public PedStopService(DutyService duty, IEventBus bus, IPedestrians pedestrians, CalloutSceneServices services,
            PedStopSettings? settings = null)
        {
            _duty = duty;
            _bus = bus;
            _pedestrians = pedestrians;
            _s = services;
            _settings = settings ?? new PedStopSettings();
        }

        public bool Active => _scene != null;

        /// <summary>The running stop's scene, for dev tools.</summary>
        internal CalloutScene? Scene => _scene;

        public void Tick(CalloutInput input)
        {
            if (_scene == null)
            {
                if (!_duty.OnDuty || _duty.Busy || !_s.World.PlayerOnFoot) return;
                if (input.StopHeld) TryStart();
                else if (_pedestrians.AnyoneToStop(_settings.MaxTargetDistance)) _s.Notifier.Help(StopHelp);
                return;
            }

            if (!_duty.OnDuty)
            {
                Finish(PedStopResult.Cancelled);
                return;
            }
            if (_s.World.PlayerPosition.DistanceTo(_scene.Focus) > _settings.AbandonDistance)
            {
                _s.Notifier.Notify("You left the pedestrian stop.");
                Finish(PedStopResult.Cancelled);
                return;
            }
            if (_scene.Tick(input) != null) Finish(Result(_scene));
        }

        /// <summary>Stops the pedestrian next to the player, if there is one. False if no stop started.</summary>
        public bool TryStart()
        {
            if (_scene != null || !_s.World.PlayerOnFoot || !_duty.TryBegin(Activity)) return false;

            EntityScope scope = _s.Scopes.Open("ped stop");
            if (!_pedestrians.TryTakePedNearby(scope, _settings.MaxTargetDistance, out IPed? ped))
            {
                scope.Close(ReleaseMode.Dismiss);
                _duty.End(Activity);
                return false;
            }

            SuspectProfile profile = _s.Profiles.Generate(ped!.IsMale, System.Array.Empty<string>(), visiblyArmed: false);
            if (profile.ConcealedWeapon != null) ped.GiveWeapon(profile.ConcealedWeapon, equip: false);
            SuspectBehaviour behaviour = StopReaction.Pick(profile, inStolenVehicle: false, _s.Random, _settings.Reactions);
            _s.Log.Info("Ped stop: " + behaviour + "[" + profile.Traits + "]");
            _scene = CalloutScene.AroundPed(ped, profile, behaviour, scope, _s);
            _bus.Publish(new PedStopStarted());
            return true;
        }

        /// <summary>The player calls off the stop. False if none is running.</summary>
        public bool Cancel()
        {
            if (_scene == null) return false;
            _s.Notifier.Notify("Pedestrian stop ~o~cancelled~s~.");
            Finish(PedStopResult.Cancelled);
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
        internal string Describe() => _scene == null ? "none" : _scene.Describe();

        static PedStopResult Result(CalloutScene scene)
        {
            var results = scene.Results.ToList();
            if (results.Contains(SuspectResult.Arrested)) return PedStopResult.Arrested;
            if (results.Contains(SuspectResult.Released) || results.Contains(SuspectResult.Cited)) return PedStopResult.Released;
            if (results.Contains(SuspectResult.Died)) return PedStopResult.SuspectDied;
            return PedStopResult.Escaped;
        }

        void Finish(PedStopResult result)
        {
            _scene!.Close(ReleaseMode.Dismiss);
            _scene = null;
            _duty.End(Activity);
            _bus.Publish(new PedStopEnded(result));
        }
    }
}
