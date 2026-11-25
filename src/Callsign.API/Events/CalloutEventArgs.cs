using System;
using Callsign.API.Callouts;

namespace Callsign.API.Events
{
    /// <summary>Data for <see cref="ICalloutsApi.CalloutOffered"/> and <see cref="ICalloutsApi.CalloutAccepted"/>.</summary>
    public class CalloutEventArgs : EventArgs
    {
        /// <summary>Creates the event data.</summary>
        public CalloutEventArgs(CalloutInfo callout) { Callout = callout; }

        /// <summary>The call.</summary>
        public CalloutInfo Callout { get; }
    }

    /// <summary>Data for <see cref="ICalloutsApi.CalloutEnded"/>.</summary>
    public sealed class CalloutEndedEventArgs : CalloutEventArgs
    {
        /// <summary>Creates the event data.</summary>
        public CalloutEndedEventArgs(CalloutInfo callout, CalloutEndReason reason) : base(callout) { Reason = reason; }

        /// <summary>Why the call is over.</summary>
        public CalloutEndReason Reason { get; }
    }

    /// <summary>Why a call ended.</summary>
    public enum CalloutEndReason
    {
        /// <summary>The player declined the offer.</summary>
        Declined,
        /// <summary>The offer timed out unanswered.</summary>
        OfferExpired,
        /// <summary>The player dealt with the suspect.</summary>
        Resolved,
        /// <summary>The suspect got away.</summary>
        SuspectEscaped,
        /// <summary>The suspect died.</summary>
        SuspectDied,
        /// <summary>The player called it off, went off duty, or died.</summary>
        Cancelled,
        /// <summary>The call could not run (e.g. an unknown ped model in a part).</summary>
        Failed,
    }
}
