using System;
using GTA;
using Callsign.Core.Ports;

namespace Callsign.Engine
{
    /// <summary><see cref="IClock"/> on the game's timer and world clock.</summary>
    internal sealed class GameClock : IClock
    {
        public long NowMs => Game.GameTime;

        public TimeSpan TimeOfDay => World.CurrentTimeOfDay;
    }
}
