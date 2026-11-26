namespace Callsign.Core.Ports
{
    /// <summary>The vehicle the player is in, or the one they just got out of (close by).</summary>
    public interface IPlayerVehicle
    {
        /// <summary>There is such a vehicle and it isn't wrecked.</summary>
        bool Available { get; }

        /// <summary>Fixes body, engine, tyres and windows and washes it. False if no vehicle is <see cref="Available"/>.</summary>
        bool Repair();
    }
}
