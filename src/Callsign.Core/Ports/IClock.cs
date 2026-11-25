using System;

namespace Callsign.Core.Ports
{
    /// <summary>
    /// Time as the game sees it. Core never reads <see cref="DateTime.Now"/>; tests use a fake clock.
    /// </summary>
    public interface IClock
    {
        /// <summary>
        /// Milliseconds the game has been running. Only moves forward; pauses while the game is paused.
        /// Use it for timers and cooldowns.
        /// </summary>
        long NowMs { get; }

        /// <summary>The in-game clock (hours, minutes). Use it for day/night shifts and odds.</summary>
        TimeSpan TimeOfDay { get; }
    }
}
