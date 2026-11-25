using System;
using Callsign.API.Events;

namespace Callsign.API
{
    /// <summary>A plugin's handle to Callsign. Each plugin gets its own instance.</summary>
    /// <remarks>Implemented by Callsign, never by plugins, so members can be added in
    /// minor versions without breaking anyone.</remarks>
    public interface ICallsignApi
    {
        /// <summary>Version of the API the running mod implements.</summary>
        Version ApiVersion { get; }

        /// <summary>Duty state of the player.</summary>
        IDutyApi Duty { get; }

        /// <summary>Writes to Callsign.log, prefixed with the plugin's name.</summary>
        IPluginLog Log { get; }
    }

    /// <summary>Player duty state.</summary>
    public interface IDutyApi
    {
        /// <summary>True while the player is on duty.</summary>
        bool IsOnDuty { get; }

        /// <summary>Puts the player on or off duty (same as pressing the duty key).</summary>
        void SetOnDuty(bool onDuty);

        /// <summary>Raised after the player goes on or off duty.</summary>
        event EventHandler<DutyChangedEventArgs> DutyChanged;
    }

    /// <summary>Plugin logging into Callsign.log.</summary>
    public interface IPluginLog
    {
        /// <summary>Informational message.</summary>
        void Info(string message);
        /// <summary>Something unexpected but recoverable.</summary>
        void Warn(string message);
        /// <summary>A failure, optionally with its exception.</summary>
        void Error(string message, Exception? exception = null);
    }
}
