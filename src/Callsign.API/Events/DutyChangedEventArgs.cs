using System;

namespace Callsign.API.Events
{
    /// <summary>Data for <see cref="IDutyApi.DutyChanged"/>.</summary>
    public sealed class DutyChangedEventArgs : EventArgs
    {
        /// <summary>Creates the event data.</summary>
        public DutyChangedEventArgs(bool isOnDuty) { IsOnDuty = isOnDuty; }

        /// <summary>The new duty state.</summary>
        public bool IsOnDuty { get; }
    }
}
