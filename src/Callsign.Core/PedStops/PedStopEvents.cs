namespace Callsign.Core.PedStops
{
    /// <summary>How a pedestrian stop ended, by its weightiest result.</summary>
    public enum PedStopResult
    {
        /// <summary>The person was arrested and booked.</summary>
        Arrested,
        /// <summary>Let go.</summary>
        Released,
        /// <summary>They got away.</summary>
        Escaped,
        SuspectDied,
        /// <summary>The player called it off, walked away, went off duty or died.</summary>
        Cancelled,
    }

    /// <summary>The player stopped a pedestrian (they may still run).</summary>
    public sealed record PedStopStarted;

    public sealed record PedStopEnded(PedStopResult Result);
}
