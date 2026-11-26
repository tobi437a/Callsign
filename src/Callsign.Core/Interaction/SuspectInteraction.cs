using System.Collections.Generic;
using System.Linq;
using Callsign.Core.Ports;
using Callsign.Core.Suspects;
using Callsign.Core.Ui;
using Callsign.Core.World;

namespace Callsign.Core.Interaction
{
    /// <summary>Distances in metres, times in game milliseconds, chances 0..1.</summary>
    public sealed class InteractionSettings
    {
        /// <summary>How close the player must be (on foot) to stop a suspect.</summary>
        public float StopDistance { get; set; } = 6;
        /// <summary>How close the player must be (on foot) to talk to a stopped suspect.</summary>
        public float TalkDistance { get; set; } = 3;
        /// <summary>A stopped suspect walks off once the player is this far away.</summary>
        public float WalkAwayDistance { get; set; } = 25;
        /// <summary>The drug swab kit is in the car: the player's vehicle must be this close.</summary>
        public float DrugKitDistance { get; set; } = 15;

        public long RecordsCheckMs { get; set; } = 5_000;
        public long BreathTestMs { get; set; } = 4_000;
        public long DrugSwabMs { get; set; } = 6_000;
        public long PlateCheckMs { get; set; } = 4_000;

        /// <summary>A nervous suspect with something to hide runs when stopped.</summary>
        public double FleeWhenStopped { get; set; } = 0.2;
        /// <summary>A nervous wanted suspect runs when they hear their name go over the radio.</summary>
        public double FleeOnRecordsCheck { get; set; } = 0.4;
        /// <summary>A wanted suspect runs when told they are under arrest.</summary>
        public double FleeOnArrest { get; set; } = 0.25;
        /// <summary>A nervous suspect with a hidden weapon pulls it when searched.</summary>
        public double AttackOnSearch { get; set; } = 0.35;
        /// <summary>A nervous driver of a stolen vehicle floors it when they hear the plate go over the radio.</summary>
        public double FleeOnPlateCheck { get; set; } = 0.4;
    }

    /// <summary>What the interaction needs from the game.</summary>
    public sealed record InteractionServices(
        IWorld World,
        INotifier Notifier,
        IMenuView MenuView,
        IClock Clock,
        IRandom Random,
        Dialogue Dialogue,
        InteractionSettings Settings)
    {
        /// <summary>The game's own key uses, so holding E near a suspect doesn't hail a taxi.</summary>
        public IGameControls Controls { get; init; } = NoGameControls.Instance;
    }

    /// <summary>What the player pressed this frame.</summary>
    /// <param name="StopHeld">The interact key has just been held long enough to stop someone.</param>
    /// <param name="Interact">The interact key was tapped (opens / closes the menu).</param>
    public readonly record struct InteractionInput(bool StopHeld = false, bool Interact = false, MenuInput Menu = default);

    /// <summary>How an interaction ended. Whoever runs it decides what happens next.</summary>
    public enum InteractionOutcome
    {
        /// <summary>The player arrested them: start custody.</summary>
        Arrest,
        /// <summary>The player let them go.</summary>
        Release,
        /// <summary>The player wrote them a ticket and let them go.</summary>
        Cite,
        /// <summary>The suspect ran.</summary>
        Fled,
        /// <summary>The suspect attacked the player.</summary>
        Attacked,
    }

    /// <summary>
    /// Stopping a suspect on foot and dealing with them through a menu: ID, questions, a records
    /// check over the radio, breath and drug tests, a pat-down, then arrest or let go. The
    /// suspect's hidden traits decide the answers, and can make them run or fight. Used by
    /// callouts, traffic stops and pedestrian stops.
    /// </summary>
    public sealed class SuspectInteraction
    {
        public const string StopHelp = "Hold ~INPUT_CONTEXT~ to stop the suspect";
        public const string TalkHelp = "Press ~INPUT_CONTEXT~ to talk to the suspect";

        // Menu item ids.
        public const string AskId = "id", AskPurpose = "purpose", AskDrinking = "drinking", RunName = "records",
            BreathTest = "breath", DrugSwab = "drugs", StepOut = "stepout", Search = "search", Arrest = "arrest", LetGo = "release",
            RunPlate = "plate", Cite = "cite";

        enum Check { None, Records, Breath, Drugs, Plate }

        readonly IPed _suspect;
        readonly SuspectProfile _profile;
        readonly InteractionServices _s;
        readonly Menu _menu = new Menu("Suspect");
        bool _menuOpen;
        bool _ranOnce;
        /// <summary>Held by a traffic stop: doesn't leave when the player walks off, and speaks when first talked to.</summary>
        bool _held, _greeted;
        Identity? _knownName;
        string? _recordsResult, _breathResult, _drugResult, _plateResult;
        Check _pending;
        long _pendingDoneAt;
        DialogueContext _context = DialogueContext.OnFoot;

        public SuspectInteraction(IPed suspect, SuspectProfile profile, InteractionServices services)
        {
            _suspect = suspect;
            _profile = profile;
            _s = services;
        }

        /// <summary>The suspect has stopped and is waiting for the player.</summary>
        public bool Stopped { get; private set; }

        public bool MenuOpen => _menuOpen;

        /// <summary>The menu, for dev tools.</summary>
        internal Menu Menu => _menu;

        public SuspectProfile Profile => _profile;

        /// <summary>The vehicle they were driving, if any: adds a plate check to the menu.</summary>
        public VehicleRecord? Vehicle { get; set; }

        /// <summary>The player has opened the menu on them at least once.</summary>
        public bool Questioned { get; private set; }

        /// <summary>
        /// Their vehicle was pulled over: they count as stopped already (no need to hold the key),
        /// wait in their seat whatever the distance, and greet the officer when first talked to.
        /// </summary>
        public void PulledOver()
        {
            Stopped = true;
            _held = true;
            _context = DialogueContext.InVehicle;
        }

        /// <summary>The suspect gave up (e.g. caught after running): they stay put; no new escape attempts.</summary>
        public void Surrendered()
        {
            Stopped = true;
            _ranOnce = true;
            _pending = Check.None;
        }

        /// <summary>Advances the interaction. Returns the outcome once it is over, otherwise null.</summary>
        public InteractionOutcome? Tick(InteractionInput input)
        {
            IWorld world = _s.World;
            float distance = world.PlayerPosition.DistanceTo(_suspect.Position);
            bool onFoot = world.PlayerOnFoot;
            // Close enough that E means this suspect (stop, talk, menu): the game's own E waits.
            if (onFoot && distance <= _s.Settings.StopDistance) _s.Controls.BlockInteractKey();

            if (!Stopped)
            {
                if (distance > _s.Settings.StopDistance || !onFoot) return null;
                if (!input.StopHeld)
                {
                    _s.Notifier.Help(StopHelp);
                    return null;
                }
                return Stop();
            }

            if (_pending != Check.None && _s.Clock.NowMs >= _pendingDoneAt) FinishCheck();

            if (!_held && distance > _s.Settings.WalkAwayDistance)
            {
                // The player left them standing: they go on their way.
                Stopped = false;
                _menuOpen = false;
                if (_suspect.InVehicle) _suspect.DriveAround();
                else _suspect.Wander();
                return null;
            }

            bool inReach = distance <= _s.Settings.TalkDistance && onFoot;
            if (!_menuOpen)
            {
                if (!inReach) return null;
                if (input.Interact)
                {
                    _menuOpen = true;
                    Questioned = true;
                    if (!_greeted) Say(_s.Dialogue.Say(DialogueTopic.Stopped, _profile, _context));
                    _greeted = true;
                }
                else
                {
                    _s.Notifier.Help(TalkHelp);
                    return null;
                }
            }
            else if (!inReach || input.Interact || input.Menu.Back)
            {
                _menuOpen = false;
                return null;
            }

            _menu.SetItems(Items());
            string? chosen = _menu.Handle(input.Menu);
            InteractionOutcome? outcome = chosen == null ? null : Do(chosen);
            if (outcome != null) return Close(outcome.Value);
            _menu.SetItems(Items());   // show what the choice changed straight away
            _s.MenuView.Draw(_menu);
            return null;
        }

        InteractionOutcome? Stop()
        {
            if (Hiding && _profile.Has(SuspectTraits.Nervous) && !_ranOnce && Chance(_s.Settings.FleeWhenStopped))
            {
                Say("~r~Nope!~s~");
                return Close(InteractionOutcome.Fled);
            }
            StoppedByOfficer();
            return null;
        }

        /// <summary>
        /// The player has just stopped them and they went along with it (whoever started the stop
        /// already rolled whether they would): they stop, face the officer and say something.
        /// </summary>
        public void StoppedByOfficer()
        {
            Stopped = true;
            // A driver stays at the wheel and talks through the window until told to step out.
            _context = _suspect.InVehicle ? DialogueContext.InVehicle : DialogueContext.OnFoot;
            if (!_suspect.InVehicle) _suspect.FacePlayer();
            Say(_s.Dialogue.Say(DialogueTopic.Stopped, _profile, _context));
            _greeted = true;
        }

        IEnumerable<MenuItem> Items()
        {
            bool busy = _pending != Check.None;
            yield return new MenuItem(AskId, _knownName == null ? "Ask for identification" : "Name: " + _knownName.FullName + ", " + _knownName.Age);
            yield return new MenuItem(AskPurpose, "Ask what they're doing here");
            yield return new MenuItem(AskDrinking, "Ask if they've been drinking or using");
            yield return new MenuItem(RunName,
                _recordsResult ?? (_pending == Check.Records ? "Records: waiting for dispatch..." : _knownName == null ? "Run their name (need a name first)" : "Run their name over the radio"),
                _knownName != null && _recordsResult == null && !busy);
            yield return new MenuItem(BreathTest,
                _breathResult ?? (_pending == Check.Breath ? "Breathalyzer: blowing..." : "Breathalyzer test"),
                _breathResult == null && !busy);
            bool kitNear = DrugKitNear();
            yield return new MenuItem(DrugSwab,
                _drugResult ?? (_pending == Check.Drugs ? "Drug swab: waiting for result..." : kitNear ? "Drug swab (kit from your vehicle)" : "Drug swab (your vehicle must be nearby)"),
                _drugResult == null && !busy && kitNear);
            if (Vehicle != null)
                yield return new MenuItem(RunPlate,
                    _plateResult ?? (_pending == Check.Plate ? "Plate: waiting for dispatch..." : "Run the plate (" + Vehicle.Plate + ")"),
                    _plateResult == null && !busy);
            bool seated = _suspect.InVehicle;
            if (seated) yield return new MenuItem(StepOut, "Order them out of the vehicle");
            yield return new MenuItem(Search, seated ? "Pat down (order them out first)" : "Pat down", !seated);
            yield return new MenuItem(Arrest, "~r~Arrest~s~");
            yield return new MenuItem(Cite, "~y~Issue a citation~s~");
            yield return new MenuItem(LetGo, "~g~Let them go with a warning~s~");
        }

        bool DrugKitNear() =>
            _s.World.TryGetPlayerVehiclePosition(out Position car)
            && car.DistanceTo(_s.World.PlayerPosition) <= _s.Settings.DrugKitDistance;

        InteractionOutcome? Do(string id)
        {
            switch (id)
            {
                case AskId:
                    if (_profile.Has(SuspectTraits.NoId))
                    {
                        Say(_s.Dialogue.Say(DialogueTopic.NoId, _profile, _context));
                        _knownName = _profile.Stated;
                    }
                    else
                    {
                        Say(_s.Dialogue.Say(DialogueTopic.HandsId, _profile, _context));
                        _knownName = _profile.Real;
                        _s.Notifier.Notify("~b~ID card~s~~n~" + _profile.Real.FullName + "~n~Age " + _profile.Real.Age);
                    }
                    return null;

                case AskPurpose:
                    Say(_s.Dialogue.Say(DialogueTopic.Purpose, _profile, _context));
                    return null;

                case AskDrinking:
                    Say(_s.Dialogue.Say(DialogueTopic.Drinking, _profile, _context));
                    return null;

                case RunName:
                    _s.Notifier.Notify("~b~You:~s~ Dispatch, requesting a records check on " + _knownName!.FullName + ".");
                    if (_profile.Has(SuspectTraits.Warrant) && _profile.Has(SuspectTraits.Nervous) && !_profile.Has(SuspectTraits.FalseName)
                        && !_ranOnce && Chance(_s.Settings.FleeOnRecordsCheck))
                        return Flee();
                    StartCheck(Check.Records, _s.Settings.RecordsCheckMs);
                    return null;

                case BreathTest:
                    _s.Notifier.Subtitle("The suspect blows into the breathalyzer...", (int)_s.Settings.BreathTestMs);
                    StartCheck(Check.Breath, _s.Settings.BreathTestMs);
                    return null;

                case DrugSwab:
                    _s.Notifier.Subtitle("You take a swab from the suspect's mouth...", (int)_s.Settings.DrugSwabMs);
                    StartCheck(Check.Drugs, _s.Settings.DrugSwabMs);
                    return null;

                case StepOut:
                    _suspect.FacePlayer();   // gets out first
                    Say(_s.Dialogue.Say(DialogueTopic.StepOut, _profile, _context));
                    return null;

                case Search:
                    if (_profile.ConcealedWeapon != null && _profile.Has(SuspectTraits.Nervous) && !_ranOnce
                        && Chance(_s.Settings.AttackOnSearch))
                    {
                        _suspect.GiveWeapon(_profile.ConcealedWeapon, equip: true);
                        Say("~r~Get away from me!~s~");
                        return InteractionOutcome.Attacked;
                    }
                    _s.Notifier.Notify("~b~Pat-down~s~~n~" + (_profile.Pockets.Count == 0 ? "Nothing found." : "Found: " + string.Join(", ", _profile.Pockets) + "."));
                    if (_profile.ConcealedWeapon != null) _suspect.RemoveWeapons();
                    return null;

                case Arrest:
                    if (_profile.Has(SuspectTraits.Warrant) && !_ranOnce && Chance(_s.Settings.FleeOnArrest)) return Flee();
                    return InteractionOutcome.Arrest;

                case RunPlate:
                    _s.Notifier.Notify("~b~You:~s~ Dispatch, running plate " + Vehicle!.Plate + ".");
                    if (Vehicle.Stolen && _profile.Has(SuspectTraits.Nervous) && !_ranOnce && Chance(_s.Settings.FleeOnPlateCheck))
                        return Flee();
                    StartCheck(Check.Plate, _s.Settings.PlateCheckMs);
                    return null;

                case Cite:
                    _s.Notifier.Notify("You hand " + (_knownName?.FirstName ?? (Vehicle != null ? "the driver" : "them")) + " a ~y~citation~s~.");
                    return InteractionOutcome.Cite;

                case LetGo:
                    return InteractionOutcome.Release;
            }
            return null;
        }

        void StartCheck(Check check, long ms)
        {
            _pending = check;
            _pendingDoneAt = _s.Clock.NowMs + ms;
        }

        void FinishCheck()
        {
            Check check = _pending;
            _pending = Check.None;
            switch (check)
            {
                case Check.Records:
                    if (_knownName != _profile.Real)
                        _recordsResult = "Records: ~o~no match~s~ for that name";
                    else if (_profile.WarrantOffence != null)
                        _recordsResult = "Records: ~r~WANTED~s~ for " + _profile.WarrantOffence;
                    else
                        _recordsResult = "Records: ~g~no warrants~s~";
                    _s.Notifier.Notify("~b~Dispatch:~s~ " + _recordsResult.Substring("Records: ".Length) + ".");
                    break;

                case Check.Breath:
                    string bac = _profile.BloodAlcohol.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + "%";
                    _breathResult = "Breathalyzer: " + (_profile.OverAlcoholLimit ? "~r~" + bac + " (over the limit)~s~" : "~g~" + bac + "~s~");
                    _s.Notifier.Notify(_breathResult);
                    break;

                case Check.Plate:
                    VehicleRecord v = Vehicle!;
                    _plateResult = "Plate: " + PlateStatus(v) + ", owner " + v.Owner.FullName;
                    _s.Notifier.Notify("~b~Dispatch:~s~ " + v.Plate + " comes back to a " + v.Model
                        + ", registered to " + v.Owner.FullName + ". " + PlateStatus(v) + ".");
                    break;

                case Check.Drugs:
                    var positive = DrugPanel.In(_profile.Drugs).Select(DrugPanel.Name).ToList();
                    _drugResult = "Drug swab: " + (positive.Count == 0 ? "~g~all negative~s~" : "~r~" + string.Join(", ", positive) + "~s~");
                    _s.Notifier.Notify("~b~Drug swab~s~" + string.Concat(DrugPanel.All.Select(d =>
                        "~n~" + DrugPanel.Name(d) + ": " + ((_profile.Drugs & d) != 0 ? "~r~POSITIVE~s~" : "~g~negative~s~"))));
                    break;
            }
        }

        static string PlateStatus(VehicleRecord v)
        {
            if (v.Stolen) return "~r~reported STOLEN~s~";
            var problems = new List<string>();
            if (v.RegistrationExpired) problems.Add("registration expired");
            if (v.Uninsured) problems.Add("no insurance");
            return problems.Count == 0 ? "~g~valid~s~" : "~o~" + string.Join(", ", problems) + "~s~";
        }

        InteractionOutcome Flee()
        {
            Say("~r~Forget this!~s~");
            return InteractionOutcome.Fled;
        }

        /// <summary>Something outside the conversation made them break off (a twist): the menu closes, no new escape rolls.</summary>
        public void Interrupt() => Close(InteractionOutcome.Fled);

        InteractionOutcome Close(InteractionOutcome outcome)
        {
            _menuOpen = false;
            _pending = Check.None;
            Stopped = false;
            _held = false;
            if (outcome == InteractionOutcome.Fled || outcome == InteractionOutcome.Attacked) _ranOnce = true;
            return outcome;
        }

        bool Hiding => (_profile.Traits & (SuspectTraits.Warrant | SuspectTraits.CarryingDrugs | SuspectTraits.ConcealedWeapon)) != 0;

        bool Chance(double p) => _s.Random.NextDouble() < p;

        void Say(string line)
        {
            if (line.Length > 0) _s.Notifier.Subtitle("~y~Suspect:~s~ " + line, 5000);
        }
    }
}
