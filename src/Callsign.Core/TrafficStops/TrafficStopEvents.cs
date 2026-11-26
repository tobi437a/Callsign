namespace Callsign.Core.TrafficStops
{
    /// <summary>How a traffic stop ended, by its weightiest result.</summary>
    public enum TrafficStopResult
    {
        /// <summary>Someone was arrested and booked.</summary>
        Arrested,
        /// <summary>The driver got a citation.</summary>
        Cited,
        /// <summary>Let go with a warning.</summary>
        Warned,
        /// <summary>They got away.</summary>
        Escaped,
        SuspectDied,
        /// <summary>The player called it off, drove away, went off duty or died.</summary>
        Cancelled,
    }

    /// <summary>The player pulled a vehicle over (it may still run).</summary>
    public sealed record TrafficStopStarted(string Vehicle, string Plate, int Occupants);

    public sealed record TrafficStopEnded(string Vehicle, TrafficStopResult Result);
}
