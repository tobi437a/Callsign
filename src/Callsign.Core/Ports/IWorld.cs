using Callsign.Core.World;

namespace Callsign.Core.Ports
{
    /// <summary>Questions about the game world.</summary>
    public interface IWorld
    {
        Position PlayerPosition { get; }

        /// <summary>The nearest road position to <paramref name="near"/> and the road's heading there.</summary>
        bool TryFindStreetNear(Position near, out Position position, out float heading);
    }
}
