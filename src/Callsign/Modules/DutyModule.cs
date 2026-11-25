using System;
using GTA;
using GTA.Native;
using Callsign.Core.Duty;
using Callsign.Core.Events;
using Callsign.Engine;
using Keys = System.Windows.Forms.Keys;

namespace Callsign.Modules
{
    /// <summary>
    /// Duty key + what being on duty means for the game world (no wanted level, no game cops,
    /// no story calls or random events).
    /// Reacts to DutyChanged, so duty set from anywhere (key, plugin) applies the same way.
    /// </summary>
    internal sealed class DutyModule : IModule
    {
        const Keys DutyKey = Keys.F7;   // F4 (console) and Insert (reload) are taken by SHVDN

        readonly DutyService _duty;
        readonly IEventBus _bus;
        readonly KeyInput _input;
        IDisposable? _subscription;
        bool _ownsMissionFlag;

        public DutyModule(DutyService duty, IEventBus bus, KeyInput input)
        {
            _duty = duty;
            _bus = bus;
            _input = input;
        }

        public string Name => "Duty";

        public void Start()
        {
            _subscription = _bus.Subscribe<DutyChanged>(e => ApplyToWorld(e.OnDuty, announce: true));
        }

        public void Tick()
        {
            if (_input.WasPressed(DutyKey)) _duty.Toggle();

            // The game's own police stay out of it while we're the police.
            if (_duty.OnDuty) Game.Player.Wanted.SetWantedLevel(0, false);

            // Story scripts may clear the flag (e.g. when one of their own scenes ends); take it back.
            if (_duty.OnDuty && _ownsMissionFlag && !Function.Call<bool>(Hash.GET_MISSION_FLAG))
                Function.Call(Hash.SET_MISSION_FLAG, true);
        }

        public void Stop()
        {
            _subscription?.Dispose();
            // Script reload (Insert) or unload: never leave the world without cops.
            if (_duty.OnDuty) ApplyToWorld(onDuty: false, announce: false);
        }

        void ApplyToWorld(bool onDuty, bool announce)
        {
            Game.MaxWantedLevel = onDuty ? 0 : 5;
            QuietStory(onDuty);
            // Dispatch service ids 1..15 (police cars, helis, SWAT, roadblocks, ...)
            for (int i = 1; i <= 15; i++)
                Function.Call(Hash.ENABLE_DISPATCH_SERVICE, i, !onDuty);
            if (announce)
                GTA.UI.Notification.PostTicker(onDuty ? "~b~On duty~s~" : "~r~Off duty~s~", false, false);
        }

        /// <summary>
        /// While on duty the game treats us as "on a mission": story phone calls, texts and mission
        /// triggers wait, and ambient random events stop. Only the flag we set is cleared again, so a
        /// real story mission that was already running keeps its flag.
        /// </summary>
        void QuietStory(bool onDuty)
        {
            Function.Call(Hash.SET_RANDOM_EVENT_FLAG, !onDuty);
            if (onDuty && !Function.Call<bool>(Hash.GET_MISSION_FLAG))
            {
                Function.Call(Hash.SET_MISSION_FLAG, true);
                _ownsMissionFlag = true;
            }
            else if (!onDuty && _ownsMissionFlag)
            {
                Function.Call(Hash.SET_MISSION_FLAG, false);
                _ownsMissionFlag = false;
            }
        }
    }
}
