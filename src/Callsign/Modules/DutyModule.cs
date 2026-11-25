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
    /// Duty key + what being on duty means for the game world (no wanted level, no game cops).
    /// Reacts to DutyChanged, so duty set from anywhere (key, plugin) applies the same way.
    /// </summary>
    internal sealed class DutyModule : IModule
    {
        const Keys DutyKey = Keys.F7;   // F4 (console) and Insert (reload) are taken by SHVDN

        readonly DutyService _duty;
        readonly IEventBus _bus;
        readonly KeyInput _input;
        IDisposable? _subscription;

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
        }

        public void Stop()
        {
            _subscription?.Dispose();
            // Script reload (Insert) or unload: never leave the world without cops.
            if (_duty.OnDuty) ApplyToWorld(onDuty: false, announce: false);
        }

        static void ApplyToWorld(bool onDuty, bool announce)
        {
            Game.MaxWantedLevel = onDuty ? 0 : 5;
            // Dispatch service ids 1..15 (police cars, helis, SWAT, roadblocks, ...)
            for (int i = 1; i <= 15; i++)
                Function.Call(Hash.ENABLE_DISPATCH_SERVICE, i, !onDuty);
            if (announce)
                GTA.UI.Notification.PostTicker(onDuty ? "~b~On duty~s~" : "~r~Off duty~s~", false, false);
        }
    }
}
