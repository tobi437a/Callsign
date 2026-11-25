namespace Callsign.Core.Duty
{
    /// <summary>Published after the player goes on or off duty.</summary>
    public sealed record DutyChanged(bool OnDuty);
}
