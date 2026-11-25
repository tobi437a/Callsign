namespace Callsign.Core.Callouts
{
    /// <summary>Dispatch offered a call; the player can accept or decline it.</summary>
    public sealed record CalloutOffered(Incident Incident);

    /// <summary>The player took the call and is en route.</summary>
    public sealed record CalloutAccepted(Incident Incident);

    /// <summary>The call is over (or the offer went away). Published exactly once per offer.</summary>
    public sealed record CalloutEnded(Incident Incident, CalloutEndReason Reason);

    public enum CalloutEndReason
    {
        /// <summary>The player declined the offer.</summary>
        Declined,
        /// <summary>The offer timed out unanswered.</summary>
        OfferExpired,
        /// <summary>The player dealt with the suspect without an arrest (e.g. let them go).</summary>
        Resolved,
        /// <summary>The suspect got away.</summary>
        SuspectEscaped,
        /// <summary>The suspect died.</summary>
        SuspectDied,
        /// <summary>The player called it off, went off duty, or died.</summary>
        Cancelled,
        /// <summary>The call could not run (e.g. a bad model in the content).</summary>
        Failed,
        /// <summary>The suspect was arrested and booked at a station.</summary>
        Arrested,
    }
}
