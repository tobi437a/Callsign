using System;
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
        IBlips Blips,
        IWorld World,
        INotifier Notifier,
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
    /// Plays out one accepted incident, one tick at a time:
    /// en route → (suspect spawns when close) → on scene → suspect reacts → stopped and dealt with
    /// → arrested or let go → ended. The suspect can change their mind on the way (run, fight).
    /// Everything it spawns lives in its own entity scope.
    /// </summary>
    public sealed class CalloutScene
    {
        public enum Stage { EnRoute, Spawning, Approaching, Engaged, Surrendered, InCustody, Ended }

        readonly Incident _incident;
        readonly CalloutSceneServices _s;
        readonly EntityScope _scope;
        readonly Position _spot;
        SuspectBehaviour _behaviour;
        IBlip? _areaBlip;
        IPed? _suspect;
        IBlip? _suspectBlip;
        SuspectInteraction? _interaction;
        Custody? _custody;

        public CalloutScene(Incident incident, CalloutSceneServices services)
        {
            _incident = incident;
            _s = services;
            _behaviour = incident.Outcome.Behaviour;
            _scope = services.Scopes.Open("callout " + incident.Situation.Id + "@" + incident.Location.Id);
            _spot = services.World.TryFindPedSpotNear(incident.Location.Position, out Position spot) ? spot : incident.Location.Position;

            _areaBlip = services.Blips.At(_scope, _spot);
            _areaBlip.Name = incident.Title;
            _areaBlip.Color = MarkerColor.Yellow;
            _areaBlip.ShowRoute = true;
        }

        public Stage Current { get; private set; } = Stage.EnRoute;

        /// <summary>Who the suspect really is. Null until they spawn.</summary>
        public SuspectProfile? Suspect => _interaction?.Profile;

        /// <summary>Advances the scene. Returns the end reason once it is over, otherwise null.</summary>
        public CalloutEndReason? Tick(CalloutInput input)
        {
            if (Current == Stage.Ended) return null;
            if (_s.World.PlayerDead) return End(CalloutEndReason.Cancelled);

            Position player = _s.World.PlayerPosition;
            switch (Current)
            {
                case Stage.EnRoute:
                    if (player.DistanceTo(_spot) <= _s.Settings.SpawnDistance) Current = Stage.Spawning;
                    return null;

                case Stage.Spawning:
                    return TrySpawn();

                case Stage.InCustody:
                    return TickCustody();
            }

            // From here on the suspect exists (or did).
            IPed suspect = _suspect!;
            if (suspect.IsDead) return End(CalloutEndReason.SuspectDied, "~r~Suspect down.~s~ Code 4.");
            if (!suspect.Exists) return End(CalloutEndReason.SuspectEscaped, "~o~The suspect is gone.~s~");
            float distance = player.DistanceTo(suspect.Position);

            switch (Current)
            {
                case Stage.Approaching:
                    if (distance <= _s.Settings.OnSceneDistance) Engage(suspect);
                    return null;

                case Stage.Engaged when _behaviour == SuspectBehaviour.Comply:
                case Stage.Surrendered:
                    return TickInteraction(suspect, input);

                case Stage.Engaged:
                    if (distance > _s.Settings.EscapeDistance)
                        return End(CalloutEndReason.SuspectEscaped, "~o~The suspect got away.~s~");
                    bool caught = _behaviour == SuspectBehaviour.Flee
                        && distance <= _s.Settings.CatchDistance && _s.World.PlayerOnFoot;
                    if (caught || suspect.IsSubdued)
                    {
                        suspect.HandsUp();
                        _s.Notifier.Notify("The suspect ~g~gave up~s~.");
                        _interaction!.Surrendered();
                        Current = Stage.Surrendered;
                    }
                    return null;
            }
            return null;
        }

        CalloutEndReason? TrySpawn()
        {
            IPed suspect;
            try
            {
                if (!_s.Peds.TrySpawn(_scope, _incident.SuspectModel, _spot, 0, out IPed? ped)) return null;
                suspect = ped!;
            }
            catch (ArgumentException ex)
            {
                _s.Log.Error("Callout '" + _incident.Situation.Id + "': could not spawn suspect '" + _incident.Suspect.Id + "'", ex);
                return End(CalloutEndReason.Failed, "~r~Dispatch:~s~ call cancelled.");
            }

            _suspect = suspect;
            SuspectProfile profile = _s.Profiles.Generate(suspect.IsMale, _incident.Suspect.Tags, _incident.Suspect.Weapon != null);
            _interaction = new SuspectInteraction(suspect, profile, _s.Interaction);
            if (_incident.Suspect.Weapon != null) suspect.GiveWeapon(_incident.Suspect.Weapon, equip: true);
            if (profile.ConcealedWeapon != null) suspect.GiveWeapon(profile.ConcealedWeapon, equip: false);
            suspect.Wander();
            Current = Stage.Approaching;
            return null;
        }

        void Engage(IPed suspect)
        {
            if (_areaBlip != null) _scope.Release(_areaBlip, ReleaseMode.Dismiss);
            _areaBlip = null;
            _suspectBlip = _s.Blips.On(_scope, suspect);
            _suspectBlip.Name = "Suspect";
            _suspectBlip.Color = MarkerColor.Red;

            switch (_behaviour)
            {
                case SuspectBehaviour.Comply:
                    _s.Notifier.Subtitle("Find the ~r~suspect~s~ and stop them.", 5000);
                    break;
                default:
                    ChangeBehaviour(suspect, _behaviour);
                    break;
            }
            Current = Stage.Engaged;
        }

        void ChangeBehaviour(IPed suspect, SuspectBehaviour behaviour)
        {
            _behaviour = behaviour;
            Current = Stage.Engaged;
            if (behaviour == SuspectBehaviour.Flee)
            {
                suspect.FleeFromPlayer();
                _s.Notifier.Notify("~r~The suspect is fleeing!~s~");
            }
            else if (behaviour == SuspectBehaviour.Fight)
            {
                suspect.FightPlayer();
                _s.Notifier.Notify("~r~The suspect is attacking!~s~");
            }
        }

        CalloutEndReason? TickInteraction(IPed suspect, CalloutInput input)
        {
            switch (_interaction!.Tick(input.ToInteraction()))
            {
                case InteractionOutcome.Arrest:
                    _custody = new Custody(suspect, _scope, _s.Custody);
                    if (_suspectBlip != null)
                    {
                        _suspectBlip.Name = "Prisoner";
                        _suspectBlip.Color = MarkerColor.Blue;
                    }
                    Current = Stage.InCustody;
                    return null;
                case InteractionOutcome.Release:
                    return End(CalloutEndReason.Resolved, "Suspect ~g~let go~s~. Code 4.");
                case InteractionOutcome.Fled:
                    ChangeBehaviour(suspect, SuspectBehaviour.Flee);
                    return null;
                case InteractionOutcome.Attacked:
                    ChangeBehaviour(suspect, SuspectBehaviour.Fight);
                    return null;
                default:
                    return null;
            }
        }

        CalloutEndReason? TickCustody()
        {
            switch (_custody!.Tick())
            {
                case CustodyOutcome.Booked:
                    string where = _custody.Station != null ? " at " + _custody.Station.Name : "";
                    return End(CalloutEndReason.Arrested, "~g~Suspect booked~s~" + where + ". Code 4.");
                case CustodyOutcome.PrisonerDied:
                    return End(CalloutEndReason.SuspectDied, "~r~The prisoner died.~s~");
                case CustodyOutcome.PrisonerLost:
                    return End(CalloutEndReason.SuspectEscaped, "~o~The prisoner is gone.~s~");
                default:
                    return null;
            }
        }

        CalloutEndReason End(CalloutEndReason reason, string? message = null)
        {
            Current = Stage.Ended;
            if (message != null) _s.Notifier.Notify(message);
            Close(ReleaseMode.Dismiss);
            return reason;
        }

        /// <summary>Releases everything the scene spawned. Safe to call more than once.</summary>
        public void Close(ReleaseMode mode)
        {
            Current = Stage.Ended;
            _scope.Close(mode);
        }
    }
}
