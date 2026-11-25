using Callsign.Core.World;

namespace Callsign.Core.Ports
{
    /// <summary>Questions about the game world and the player.</summary>
    public interface IWorld
    {
        Position PlayerPosition { get; }

        bool PlayerOnFoot { get; }

        bool PlayerInVehicle { get; }

        /// <summary>Where the vehicle the player is in (or last used) is. False if there is none.</summary>
        bool TryGetPlayerVehiclePosition(out Position position);

        bool PlayerDead { get; }

        /// <summary>The player is in a vehicle with its emergency lights on.</summary>
        bool PlayerLightsOn { get; }

        /// <summary>The nearest road position to <paramref name="near"/> and the road's heading there.</summary>
        bool TryFindStreetNear(Position near, out Position position, out float heading);

        /// <summary>A spot on the ground near <paramref name="near"/> where a ped can stand (prefers sidewalks).</summary>
        bool TryFindPedSpotNear(Position near, out Position position);

        /// <summary>How dispatch would name the place, e.g. "Alta St, Downtown".</summary>
        string DescribeLocation(Position position);
    }
}
