using System;

namespace Callsign.Core.World
{
    /// <summary>A point in the game world (metres). Core's stand-in for the game's vector type.</summary>
    public readonly record struct Position(float X, float Y, float Z)
    {
        public float DistanceTo(Position other)
        {
            float dx = X - other.X, dy = Y - other.Y, dz = Z - other.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
