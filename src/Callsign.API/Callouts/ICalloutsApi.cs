using System;
using Callsign.API.Events;

namespace Callsign.API.Callouts
{
    /// <summary>
    /// Callouts: add your own parts to the incident generator, and follow calls as they happen.
    /// </summary>
    /// <remarks>
    /// Every incident is drawn as situation × location × suspect × outcome from all registered parts:
    /// the built-in ones and those of every plugin. Parts you add mix in with the rest; a situation you
    /// add can use built-in locations and suspects and the other way round, through tags.
    /// Parts are removed automatically when your plugin unloads. Give them ids that start with your
    /// plugin's name (e.g. "myplugin-noise-complaint"): ids must be unique across all plugins.
    /// </remarks>
    public interface ICalloutsApi
    {
        /// <summary>Adds a situation. Dispose the result to remove it early.</summary>
        /// <exception cref="ArgumentException">The id is empty or already taken, or the title or dispatch text is empty.</exception>
        IDisposable AddSituation(CalloutSituation situation);

        /// <summary>Adds a location. Dispose the result to remove it early.</summary>
        /// <exception cref="ArgumentException">The id is empty or already taken, or the name is empty.</exception>
        IDisposable AddLocation(CalloutLocation location);

        /// <summary>Adds a suspect profile. Dispose the result to remove it early.</summary>
        /// <exception cref="ArgumentException">The id is empty or already taken, or the description or models are empty.</exception>
        IDisposable AddSuspect(CalloutSuspect suspect);

        /// <summary>Adds an outcome. Dispose the result to remove it early.</summary>
        /// <exception cref="ArgumentException">The id is empty or already taken.</exception>
        IDisposable AddOutcome(CalloutOutcome outcome);

        /// <summary>The call currently offered or running, or null.</summary>
        CalloutInfo? Current { get; }

        /// <summary>Raised when dispatch offers a call.</summary>
        event EventHandler<CalloutEventArgs> CalloutOffered;

        /// <summary>Raised when the player accepts a call.</summary>
        event EventHandler<CalloutEventArgs> CalloutAccepted;

        /// <summary>Raised once per offered call when it is over: declined, expired, resolved, ...</summary>
        event EventHandler<CalloutEndedEventArgs> CalloutEnded;
    }
}
