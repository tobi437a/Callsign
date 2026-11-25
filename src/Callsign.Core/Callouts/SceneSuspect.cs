using Callsign.Core.Arrest;
using Callsign.Core.Entities;
using Callsign.Core.Interaction;
using Callsign.Core.Ports;
using Callsign.Core.Suspects;

namespace Callsign.Core.Callouts
{
    /// <summary>How one suspect's part in a call ended.</summary>
    internal enum SuspectResult { Arrested, Released, Died, Escaped }

    /// <summary>
    /// One suspect in a running callout: their reaction, the interaction with them and, once
    /// arrested, their custody. The scene owns the vehicle and tells seated suspects when to act.
    /// </summary>
    internal sealed class SceneSuspect
    {
        public enum State
        {
            /// <summary>Spawned, hasn't noticed the player yet.</summary>
            Unaware,
            /// <summary>In the vehicle, which the scene drives (cruising, pulling over, pursuit).</summary>
            Seated,
            /// <summary>Cooperating: the player can stop and deal with them.</summary>
            Calm,
            Running,
            Fighting,
            /// <summary>Gave up: hands up, waiting for the player.</summary>
            Surrendered,
            InCustody,
            Done,
        }

        readonly EntityScope _scope;
        readonly CalloutSceneServices _s;
        IBlip? _blip;
        Custody? _custody;

        public SceneSuspect(IPed ped, SuspectProfile profile, SuspectBehaviour behaviour, int seat,
            EntityScope scope, CalloutSceneServices services)
        {
            Ped = ped;
            Behaviour = behaviour;
            Seat = seat;
            _scope = scope;
            _s = services;
            Interaction = new SuspectInteraction(ped, profile, services.Interaction);
            Current = seat >= 0 ? State.Seated : State.Unaware;
        }

        public IPed Ped { get; }
        public SuspectInteraction Interaction { get; }
        public SuspectBehaviour Behaviour { get; private set; }

        /// <summary>Seat in the suspects' vehicle (0 = driver), or -1 on foot.</summary>
        public int Seat { get; }

        public State Current { get; private set; }
        public SuspectResult? Result { get; private set; }

        /// <summary>What happened, for the player, once <see cref="Result"/> is set.</summary>
        public string? ResultMessage { get; private set; }

        public bool IsDone => Current == State.Done;
        public bool IsDriver => Seat == 0;

        /// <summary>Can be stopped and talked to.</summary>
        public bool Talkable => Current == State.Calm || Current == State.Surrendered;

        public void ShowBlip(string name, MarkerColor color, bool route = false)
        {
            _blip ??= _s.Blips.On(_scope, Ped);
            _blip.Name = name;
            _blip.Color = color;
            _blip.ShowRoute = route;
        }

        /// <summary>The suspect notices the officer and reacts as rolled. Calm ones in a vehicle stay seated.</summary>
        public void Engage()
        {
            ShowBlip("Suspect", MarkerColor.Red);
            switch (Behaviour)
            {
                case SuspectBehaviour.Flee: Run(); break;
                case SuspectBehaviour.Fight: Fight(); break;
                default: Current = State.Calm; break;
            }
        }

        /// <summary>The suspects' vehicle came to a stop after a pursuit: everyone gets out. Calm ones give up.</summary>
        public void BailOut()
        {
            if (Behaviour == SuspectBehaviour.Comply) Surrender(announce: false);
            else Engage();
        }

        /// <summary>The vehicle drives off with them in it: the scene handles them again.</summary>
        public void Reseat()
        {
            if (Talkable && Ped.InVehicle) Current = State.Seated;
        }

        /// <summary>The vehicle is getting away and the suspect is still in it.</summary>
        public void EscapedInVehicle() => Finish(SuspectResult.Escaped, "~o~The vehicle got away.~s~");

        /// <summary>
        /// Advances this suspect. <paramref name="input"/> is only given to the suspect the player is
        /// dealing with. Returns true if the suspect, sitting at the wheel, decided to drive off.
        /// </summary>
        public bool Tick(InteractionInput input)
        {
            if (Current == State.Done) return false;
            if (Ped.IsDead)
            {
                Finish(SuspectResult.Died, Current == State.InCustody ? "~r~The prisoner died.~s~" : "~r~Suspect down.~s~");
                return false;
            }
            if (!Ped.Exists)
            {
                Finish(SuspectResult.Escaped, Current == State.InCustody ? "~o~The prisoner is gone.~s~" : "~o~The suspect is gone.~s~");
                return false;
            }

            IWorld world = _s.World;
            float distance = world.PlayerPosition.DistanceTo(Ped.Position);
            switch (Current)
            {
                case State.Unaware:
                    if (distance <= _s.Settings.OnSceneDistance) Engage();
                    return false;

                case State.Calm:
                case State.Surrendered:
                    return TickInteraction(input);

                case State.Running:
                case State.Fighting:
                    if (distance > _s.Settings.EscapeDistance)
                    {
                        Finish(SuspectResult.Escaped, "~o~A suspect got away.~s~");
                        return false;
                    }
                    bool caught = Current == State.Running && !Ped.InVehicle
                        && distance <= _s.Settings.CatchDistance && world.PlayerOnFoot;
                    if (caught || Ped.IsSubdued) Surrender(announce: true);
                    return false;

                case State.InCustody:
                    TickCustody();
                    return false;
            }
            return false;
        }

        bool TickInteraction(InteractionInput input)
        {
            switch (Interaction.Tick(input))
            {
                case InteractionOutcome.Arrest:
                    _custody = new Custody(Ped, _scope, _s.Custody);
                    ShowBlip("Prisoner", MarkerColor.Blue);
                    Current = State.InCustody;
                    return false;
                case InteractionOutcome.Release:
                    Finish(SuspectResult.Released, "Suspect ~g~let go~s~.");
                    return false;
                case InteractionOutcome.Fled:
                    Behaviour = SuspectBehaviour.Flee;
                    if (IsDriver && Ped.InVehicle)
                    {
                        Current = State.Seated;   // the scene starts the pursuit
                        return true;
                    }
                    Run();
                    return false;
                case InteractionOutcome.Attacked:
                    Behaviour = SuspectBehaviour.Fight;
                    Fight();
                    return false;
            }
            return false;
        }

        void TickCustody()
        {
            switch (_custody!.Tick())
            {
                case CustodyOutcome.Booked:
                    string where = _custody.Station != null ? " at " + _custody.Station.Name : "";
                    Finish(SuspectResult.Arrested, "~g~Suspect booked~s~" + where + ".");
                    break;
                case CustodyOutcome.PrisonerDied:
                    Finish(SuspectResult.Died, "~r~The prisoner died.~s~");
                    break;
                case CustodyOutcome.PrisonerLost:
                    Finish(SuspectResult.Escaped, "~o~The prisoner is gone.~s~");
                    break;
            }
        }

        void Run()
        {
            Current = State.Running;
            ShowBlip("Suspect", MarkerColor.Red);
            Ped.FleeFromPlayer();
            _s.Notifier.Notify("~r~Suspect fleeing on foot!~s~");
        }

        void Fight()
        {
            Current = State.Fighting;
            ShowBlip("Suspect", MarkerColor.Red);
            Ped.FightPlayer();
            _s.Notifier.Notify("~r~Suspect attacking!~s~");
        }

        void Surrender(bool announce)
        {
            ShowBlip("Suspect", MarkerColor.Red);
            Ped.HandsUp();
            Interaction.Surrendered();
            Current = State.Surrendered;
            if (announce) _s.Notifier.Notify("A suspect ~g~gave up~s~.");
        }

        void Finish(SuspectResult result, string message)
        {
            Current = State.Done;
            Result = result;
            ResultMessage = message;
            if (_blip != null) _scope.Release(_blip, ReleaseMode.Dismiss);
            _blip = null;
        }
    }
}
