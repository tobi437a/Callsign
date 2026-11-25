using System.Collections.Generic;
using Callsign.Core.Entities;
using Callsign.Core.Ports;

namespace Callsign.Core.Arrest
{
    /// <summary>Distances in metres, times in game milliseconds.</summary>
    public sealed class CustodySettings
    {
        /// <summary>The prisoner gets into the player's vehicle if they are this close when the player gets in.</summary>
        public float BoardDistance { get; set; } = 15;
        /// <summary>If the prisoner isn't seated after this long, they are put in the seat directly.</summary>
        public long BoardTimeoutMs { get; set; } = 20_000;
        /// <summary>The prisoner is booked once the player is this close to a station.</summary>
        public float BookDistance { get; set; } = 30;
    }

    /// <summary>What a custody needs from the game.</summary>
    public sealed record CustodyServices(
        IWorld World,
        IBlips Blips,
        INotifier Notifier,
        IClock Clock,
        IReadOnlyList<PoliceStation> Stations,
        CustodySettings Settings);

    public enum CustodyOutcome
    {
        /// <summary>Delivered to a station and booked. The prisoner has been removed.</summary>
        Booked,
        /// <summary>The prisoner died.</summary>
        PrisonerDied,
        /// <summary>The prisoner no longer exists (e.g. the game despawned them).</summary>
        PrisonerLost,
    }

    /// <summary>
    /// The arrest pipeline for one prisoner, one tick at a time:
    /// cuffing → escorting on foot → boarding the player's vehicle → transport → booked.
    /// Used by anything that arrests someone (callouts now, traffic stops later).
    /// </summary>
    public sealed class Custody
    {
        public enum Stage { Cuffing, Escorting, Boarding, Transport, Done }

        public const string EscortHelp = "Walk the prisoner to your vehicle and get in.";
        public const string NoSeatHelp = "No free back seat in this vehicle.";
        public const string LeftBehindHelp = "Your ~b~prisoner~s~ is not with you.";

        readonly IPed _prisoner;
        readonly EntityScope _scope;
        readonly CustodyServices _s;
        long _boardingSince;
        IBlip? _stationBlip;

        /// <param name="scope">Scope that owns the prisoner; the station marker goes in it too.</param>
        public Custody(IPed prisoner, EntityScope scope, CustodyServices services)
        {
            _prisoner = prisoner;
            _scope = scope;
            _s = services;
        }

        public Stage Current { get; private set; } = Stage.Cuffing;

        /// <summary>Where the prisoner is being taken (set once transport starts).</summary>
        public PoliceStation? Station { get; private set; }

        /// <summary>Advances the arrest. Returns the outcome once it is over, otherwise null.</summary>
        public CustodyOutcome? Tick()
        {
            if (Current == Stage.Done) return null;
            if (_prisoner.IsDead) return Finish(CustodyOutcome.PrisonerDied);
            if (!_prisoner.Exists) return Finish(CustodyOutcome.PrisonerLost);

            IWorld world = _s.World;
            switch (Current)
            {
                case Stage.Cuffing:
                    if (!_prisoner.TryCuff()) return null;
                    _prisoner.FollowPlayer();
                    _s.Notifier.Subtitle("Suspect ~b~cuffed~s~. " + EscortHelp, 6000);
                    Current = Stage.Escorting;
                    return null;

                case Stage.Escorting:
                    if (!world.PlayerInVehicle) return null;
                    if (_prisoner.Position.DistanceTo(world.PlayerPosition) > _s.Settings.BoardDistance)
                    {
                        _s.Notifier.Help(LeftBehindHelp);
                        return null;
                    }
                    if (!_prisoner.EnterPlayerVehicle(warp: false))
                    {
                        _s.Notifier.Help(NoSeatHelp);
                        return null;
                    }
                    _boardingSince = _s.Clock.NowMs;
                    Current = Stage.Boarding;
                    return null;

                case Stage.Boarding:
                    if (_prisoner.InPlayerVehicle)
                    {
                        StartTransport();
                        return null;
                    }
                    if (!world.PlayerInVehicle)
                    {
                        // Player got out before the prisoner was seated: back to following.
                        _prisoner.FollowPlayer();
                        Current = Stage.Escorting;
                    }
                    else if (_s.Clock.NowMs - _boardingSince >= _s.Settings.BoardTimeoutMs)
                    {
                        _prisoner.EnterPlayerVehicle(warp: true);   // stuck on something: seat them
                        _boardingSince = _s.Clock.NowMs;
                    }
                    return null;

                case Stage.Transport:
                    if (!_prisoner.InPlayerVehicle)
                    {
                        // Player switched vehicles: the prisoner gets out and follows again.
                        ReleaseStationBlip();
                        _prisoner.FollowPlayer();
                        Current = Stage.Escorting;
                        return null;
                    }
                    if (Station == null || world.PlayerPosition.DistanceTo(Station.Position) <= _s.Settings.BookDistance)
                    {
                        _scope.Release(_prisoner, ReleaseMode.Delete);   // handed over to the station
                        return Finish(CustodyOutcome.Booked);
                    }
                    return null;
            }
            return null;
        }

        void StartTransport()
        {
            Current = Stage.Transport;
            Station = PoliceStation.Nearest(_s.Stations, _s.World.PlayerPosition);
            if (Station == null) return;   // no stations loaded: booked on the spot next tick

            _stationBlip = _s.Blips.At(_scope, Station.Position);
            _stationBlip.Name = Station.Name;
            _stationBlip.Color = MarkerColor.Blue;
            _stationBlip.ShowRoute = true;
            _s.Notifier.Notify("Take the prisoner to ~b~" + Station.Name + "~s~.");
        }

        CustodyOutcome Finish(CustodyOutcome outcome)
        {
            Current = Stage.Done;
            ReleaseStationBlip();
            return outcome;
        }

        void ReleaseStationBlip()
        {
            if (_stationBlip != null) _scope.Release(_stationBlip, ReleaseMode.Dismiss);
            _stationBlip = null;
        }
    }
}
