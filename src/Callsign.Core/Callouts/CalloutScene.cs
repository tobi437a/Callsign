using System;
using System.Collections.Generic;
using System.Linq;
using Callsign.Core.Arrest;
using Callsign.Core.Entities;
using Callsign.Core.Interaction;
using Callsign.Core.Ports;
using Callsign.Core.Suspects;
using Callsign.Core.Ui;
using Callsign.Core.World;

namespace Callsign.Core.Callouts
{
    /// <summary>What a running callout needs from the game.</summary>
    public sealed record CalloutSceneServices(
        EntityScopes Scopes,
        IPedFactory Peds,
        IVehicleFactory Vehicles,
        IBlips Blips,
        IWorld World,
        INotifier Notifier,
        IClock Clock,
        IRandom Random,
        ILog Log,
        CalloutSettings Settings,
        SuspectProfileGenerator Profiles,
        InteractionServices Interaction,
        CustodyServices Custody);

    /// <summary>What the player pressed this frame that a callout reacts to.</summary>
    /// <param name="StopHeld">The interact key has just been held long enough to stop someone.</param>
    /// <param name="Interact">The interact key was tapped.</param>
    /// <param name="Menu">Menu keys.</param>
    public readonly record struct CalloutInput(bool StopHeld = false, bool Interact = false, MenuInput Menu = default)
    {
        public InteractionInput ToInteraction() => new InteractionInput(StopHeld, Interact, Menu);
    }

    /// <summary>
    /// Plays out one accepted incident, one tick at a time: en route → suspects spawn when the
    /// player is close → each suspect reacts as rolled and is dealt with (stopped, chased, arrested,
    /// let go) → the call ends once every suspect is accounted for.
    /// Suspects in a vehicle drive around until the player catches up: a calm driver pulls over
    /// for the player's lights, anyone else floors it, and a pursuit ends with everyone bailing out.
    /// Everything it spawns lives in its own entity scope.
    /// </summary>
    public sealed class CalloutScene
    {
        public enum Stage { EnRoute, Spawning, Active, Ended }

        public enum VehicleStage
        {
            None,
            Cruising,
            /// <summary>A calm driver pulling over for the player's lights.</summary>
            PullingOver,
            /// <summary>Pulled over; the occupants wait for the officer.</summary>
            Stopped,
            Pursuit,
            /// <summary>The fleeing driver has had enough and is stopping, to give up or to run.</summary>
            Stopping,
            Abandoned,
        }

        public const string PullOverHelp = "Turn on your emergency lights to pull the ~r~vehicle~s~ over.";

        readonly Incident _incident;
        readonly CalloutSceneServices _s;
        readonly EntityScope _scope;
        readonly Position _spot;
        readonly float _heading;
        readonly List<SceneSuspect> _suspects = new List<SceneSuspect>();
        IBlip? _areaBlip;
        IVehicle? _vehicle;
        long _slowSince = -1;
        long _lastTickMs = -1;
        long _stoppingSince;
        PursuitMind? _mind;
        bool _givingUp;
        SceneSuspect? _focus;
        SceneSuspect? _lastFinished;

        public CalloutScene(Incident incident, CalloutSceneServices services)
        {
            _incident = incident;
            _s = services;
            _scope = services.Scopes.Open("callout " + incident.Situation.Id + "@" + incident.Location.Id);

            Position at = incident.Location.Position;
            if (incident.VehicleModel != null && services.World.TryFindStreetNear(at, out Position street, out float heading))
            {
                _spot = street;
                _heading = heading;
            }
            else
            {
                _spot = services.World.TryFindPedSpotNear(at, out Position spot) ? spot : at;
            }

            _areaBlip = services.Blips.At(_scope, _spot);
            _areaBlip.Name = incident.Title;
            _areaBlip.Color = MarkerColor.Yellow;
            _areaBlip.ShowRoute = true;
        }

        public Stage Current { get; private set; } = Stage.EnRoute;

        public VehicleStage Vehicle { get; private set; } = VehicleStage.None;

        /// <summary>Advances the scene. Returns the end reason once it is over, otherwise null.</summary>
        public CalloutEndReason? Tick(CalloutInput input)
        {
            if (Current == Stage.Ended) return null;
            if (_s.World.PlayerDead) return End(CalloutEndReason.Cancelled);

            switch (Current)
            {
                case Stage.EnRoute:
                    if (_s.World.PlayerPosition.DistanceTo(_spot) <= _s.Settings.SpawnDistance) Current = Stage.Spawning;
                    return null;
                case Stage.Spawning:
                    return TrySpawn();
            }

            TickVehicle();
            TickSuspects(input);
            return _suspects.All(x => x.IsDone) ? Finish() : null;
        }

        // --- spawning: one entity per tick, vehicle first ---

        CalloutEndReason? TrySpawn()
        {
            try
            {
                if (_incident.VehicleModel != null && _vehicle == null)
                {
                    if (_s.Vehicles.TrySpawn(_scope, _incident.VehicleModel, _spot, _heading, out IVehicle? vehicle)) _vehicle = vehicle;
                    return null;
                }

                int i = _suspects.Count;
                SuspectRoll roll = _incident.Suspects[i];
                Position at = _vehicle?.Position ?? new Position(_spot.X + i * 1.5f, _spot.Y, _spot.Z);
                if (!_s.Peds.TrySpawn(_scope, roll.Model, at, _heading, out IPed? ped)) return null;
                AddSuspect(ped!, roll, i);
            }
            catch (ArgumentException ex)
            {
                _s.Log.Error("Callout '" + _incident.Situation.Id + "': could not spawn '" + _incident.Suspect.Id + "'", ex);
                return End(CalloutEndReason.Failed, "~r~Dispatch:~s~ call cancelled.");
            }

            if (_suspects.Count == _incident.Suspects.Count) StartScene();
            return null;
        }

        void AddSuspect(IPed ped, SuspectRoll roll, int index)
        {
            SuspectPart part = _incident.Suspect;
            SuspectProfile profile = _s.Profiles.Generate(ped.IsMale, part.Tags, part.Weapon != null);
            int seat = _vehicle != null ? index : -1;
            if (_vehicle != null) ped.WarpInto(_vehicle, seat);
            if (part.Weapon != null) ped.GiveWeapon(part.Weapon, equip: _vehicle == null);
            if (profile.ConcealedWeapon != null) ped.GiveWeapon(profile.ConcealedWeapon, equip: false);
            _suspects.Add(new SceneSuspect(ped, profile, roll.Outcome.Behaviour, seat, _scope, _s));
        }

        void StartScene()
        {
            Current = Stage.Active;
            if (_vehicle == null)
            {
                foreach (SceneSuspect x in _suspects) x.Ped.Wander();
                return;
            }

            // Dispatch keeps eyes on the vehicle, so the player can find it.
            ReleaseAreaBlip();
            Driver.Ped.DriveAround();
            Driver.ShowBlip("Suspect vehicle", MarkerColor.Yellow, route: true);
            Vehicle = VehicleStage.Cruising;
        }

        // --- the suspects' vehicle ---

        SceneSuspect Driver => _suspects[0];

        IEnumerable<SceneSuspect> Seated => _suspects.Where(x => x.Current == SceneSuspect.State.Seated);

        void TickVehicle()
        {
            if (_vehicle == null || Vehicle == VehicleStage.Abandoned) return;
            float distance = _s.World.PlayerPosition.DistanceTo(_vehicle.Position);
            CalloutSettings settings = _s.Settings;

            switch (Vehicle)
            {
                case VehicleStage.Cruising:
                    if (distance > settings.VehicleEngageDistance) return;
                    if (Driver.Behaviour != SuspectBehaviour.Comply)
                    {
                        StartPursuit();
                    }
                    else if (_s.World.PlayerLightsOn)
                    {
                        Driver.Ped.PullOver();
                        Driver.ShowBlip("Suspect vehicle", MarkerColor.Red);
                        Vehicle = VehicleStage.PullingOver;
                    }
                    else if (_s.World.PlayerInVehicle)
                    {
                        _s.Notifier.Help(PullOverHelp);
                    }
                    return;

                case VehicleStage.PullingOver:
                    if (_vehicle.Speed > settings.StoppedSpeed && !_vehicle.IsWrecked) return;
                    Vehicle = VehicleStage.Stopped;
                    _s.Notifier.Notify("The vehicle has ~g~stopped~s~. Approach the driver on foot.");
                    foreach (SceneSuspect x in Seated.ToList()) x.Engage();
                    return;

                case VehicleStage.Pursuit:
                    if (Seated.All(x => x.IsDone)) return;
                    if (distance > settings.VehicleEscapeDistance)
                    {
                        foreach (SceneSuspect x in Seated.ToList()) Finished(x, () => x.EscapedInVehicle());
                        Vehicle = VehicleStage.Abandoned;
                        return;
                    }
                    if (ShouldBail())
                    {
                        BailOut();
                        return;
                    }
                    PursuitChoice choice = _mind!.Update(Pressure(distance), SecondsSinceLastTick());
                    if (choice != PursuitChoice.KeepGoing) StartStopping(choice == PursuitChoice.GiveUp);
                    return;

                case VehicleStage.Stopping:
                    bool slow = _vehicle.Speed <= settings.GetOutSpeed || _vehicle.IsWrecked || !Driver.Ped.InVehicle;
                    if (!slow && _s.Clock.NowMs - _stoppingSince < settings.StopTimeoutMs) return;
                    if (_givingUp) GiveUp();
                    else BailOut();
                    return;
            }
        }

        PursuitPressure Pressure(float distance) => new PursuitPressure(
            _vehicle!.BurstTyres, _vehicle.EngineHealth, _vehicle.Speed, distance, Driver.Ped.PlayerAimingAt);

        double SecondsSinceLastTick()
        {
            long now = _s.Clock.NowMs;
            double seconds = _lastTickMs < 0 ? 0 : (now - _lastTickMs) / 1000.0;
            _lastTickMs = now;
            return seconds;
        }

        void StartStopping(bool giveUp)
        {
            Vehicle = VehicleStage.Stopping;
            _givingUp = giveUp;
            _stoppingSince = _s.Clock.NowMs;
            Driver.Ped.PullOver();
            _s.Log.Info("Pursuit: driver " + (giveUp ? "gives up" : "bails") + " after pressure " + _mind!.Pressure.ToString("0.00"));
            if (giveUp) _s.Notifier.Notify("The vehicle is ~g~pulling over~s~.");
        }

        /// <summary>The fleeing driver gives up; passengers still decide for themselves.</summary>
        void GiveUp()
        {
            Vehicle = VehicleStage.Abandoned;
            _s.Notifier.Notify("The driver ~g~gives up~s~.");
            foreach (SceneSuspect x in Seated.ToList())
            {
                if (x.IsDriver) x.GiveUp();
                else x.BailOut();
            }
        }

        void StartPursuit()
        {
            Vehicle = VehicleStage.Pursuit;
            _slowSince = -1;
            _lastTickMs = -1;
            _mind = new PursuitMind(Driver.Interaction.Profile, Driver.Behaviour, _s.Random, _s.Settings.Pursuit);
            _s.Log.Info("Pursuit: driver resolve " + _mind.Resolve.ToString("0.00") + (_mind.Runner ? ", runs" : ", gives up") + " when broken");
            foreach (SceneSuspect x in _suspects) x.Reseat();   // a stopped car driving off takes its passengers along
            Driver.Ped.DriveAway();
            Driver.ShowBlip("Suspect vehicle", MarkerColor.Red, route: true);
            foreach (SceneSuspect x in Seated.Where(x => !x.IsDriver))
            {
                x.ShowBlip("Suspect", MarkerColor.Red);
                if (x.Behaviour == SuspectBehaviour.Fight) x.Ped.ShootFromVehicle();
            }
            _s.Notifier.Notify("~r~The vehicle is fleeing!~s~ Pursue it.");
        }

        /// <summary>A pursuit ends when the vehicle is wrecked, the driver is out, or it has stood still a moment.</summary>
        bool ShouldBail()
        {
            SceneSuspect driver = Driver;
            if (_vehicle!.IsWrecked || driver.IsDone || !driver.Ped.InVehicle) return true;
            if (_vehicle.Speed > _s.Settings.StoppedSpeed)
            {
                _slowSince = -1;
                return false;
            }
            if (_slowSince < 0) _slowSince = _s.Clock.NowMs;
            return _s.Clock.NowMs - _slowSince >= _s.Settings.BailAfterStoppedMs;
        }

        void BailOut()
        {
            Vehicle = VehicleStage.Abandoned;
            var seated = Seated.ToList();
            if (seated.Count == 0) return;
            _s.Notifier.Notify(seated.Count == 1 ? "~r~The suspect is bailing out!~s~" : "~r~The suspects are bailing out!~s~");
            foreach (SceneSuspect x in seated) x.BailOut();
        }

        // --- suspects ---

        void TickSuspects(CalloutInput input)
        {
            if (_areaBlip != null && _suspects.Any(x => x.Current != SceneSuspect.State.Unaware)) ReleaseAreaBlip();

            _focus = PickFocus();
            foreach (SceneSuspect x in _suspects)
                if (x != _focus) Tick(x, default);
            if (_focus != null) Tick(_focus, input.ToInteraction());
        }

        void Tick(SceneSuspect suspect, InteractionInput input)
        {
            bool drivesOff = false;
            Finished(suspect, () => drivesOff = suspect.Tick(input));
            if (drivesOff && _vehicle != null) StartPursuit();
        }

        /// <summary>Runs <paramref name="step"/>; if it finished the suspect, says how (the last one is said by <see cref="Finish"/>).</summary>
        void Finished(SceneSuspect suspect, Action step)
        {
            bool wasDone = suspect.IsDone;
            step();
            if (wasDone || !suspect.IsDone) return;
            _lastFinished = suspect;
            if (_suspects.Any(x => !x.IsDone)) _s.Notifier.Notify(suspect.ResultMessage!);
        }

        /// <summary>The suspect the player's keys go to: the one whose menu is open, else the nearest one they can talk to.</summary>
        SceneSuspect? PickFocus()
        {
            if (_focus != null && _focus.Talkable && _focus.Interaction.MenuOpen) return _focus;
            Position player = _s.World.PlayerPosition;
            return _suspects.Where(x => x.Talkable)
                .OrderBy(x => x.Ped.Position.DistanceTo(player))
                .FirstOrDefault();
        }

        CalloutEndReason Finish()
        {
            CalloutEndReason reason =
                _suspects.Any(x => x.Result == SuspectResult.Arrested) ? CalloutEndReason.Arrested
                : _suspects.Any(x => x.Result == SuspectResult.Released) ? CalloutEndReason.Resolved
                : _suspects.Any(x => x.Result == SuspectResult.Died) ? CalloutEndReason.SuspectDied
                : CalloutEndReason.SuspectEscaped;
            return End(reason, _lastFinished!.ResultMessage + " Code 4.");
        }

        void ReleaseAreaBlip()
        {
            if (_areaBlip != null) _scope.Release(_areaBlip, ReleaseMode.Dismiss);
            _areaBlip = null;
        }

        CalloutEndReason End(CalloutEndReason reason, string? message = null)
        {
            Current = Stage.Ended;
            if (message != null) _s.Notifier.Notify(message);
            Close(ReleaseMode.Dismiss);
            return reason;
        }

        /// <summary>Where the action is: the first suspect still in play, else the vehicle or the call's spot. For dev tools.</summary>
        internal Position Focus =>
            _suspects.FirstOrDefault(x => !x.IsDone && x.Ped.Exists)?.Ped.Position ?? _vehicle?.Position ?? _spot;

        /// <summary>One line on the scene, for dev tools.</summary>
        internal string Describe()
        {
            Position player = _s.World.PlayerPosition;
            string suspects = string.Join(", ", _suspects.Select((x, i) =>
                "#" + i + " " + x.Current + "/" + x.Behaviour + (x.Result != null ? "=" + x.Result : "")
                + (x.Interaction.Stopped ? " stopped" : "") + (x.Interaction.MenuOpen ? " menu" : "")
                + (x.Ped.Exists ? " " + x.Ped.Position.DistanceTo(player).ToString("0") + "m" : " gone")));
            return Current + (Vehicle != VehicleStage.None ? " vehicle=" + Vehicle : "")
                + " spot=" + _spot.DistanceTo(player).ToString("0") + "m"
                + (suspects.Length > 0 ? " suspects: " + suspects : "");
        }

        /// <summary>Releases everything the scene spawned. Safe to call more than once.</summary>
        public void Close(ReleaseMode mode)
        {
            Current = Stage.Ended;
            _scope.Close(mode);
        }
    }
}
