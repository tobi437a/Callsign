using Callsign.Core.Entities;
using Callsign.Core.World;

namespace Callsign.Core.Ports
{
    /// <summary>A ped we spawned. All members are safe to use after the ped is gone.</summary>
    public interface IPed : ITracked
    {
        bool Exists { get; }
        bool IsDead { get; }
        Position Position { get; }

        /// <summary>Gives a weapon by its game name (e.g. "WEAPON_KNIFE") and equips it.</summary>
        void GiveWeapon(string weapon);

        // Behaviour orders. Each replaces whatever the ped was doing; all do nothing once the ped is gone.

        /// <summary>Walks around the area like a normal pedestrian.</summary>
        void Wander();

        /// <summary>Stops and keeps facing the player.</summary>
        void FacePlayer();

        /// <summary>Stops and puts their hands up.</summary>
        void HandsUp();

        /// <summary>Runs from the player.</summary>
        void FleeFromPlayer();

        /// <summary>Attacks the player.</summary>
        void FightPlayer();
    }

    /// <summary>A vehicle we spawned. All members are safe to use after the vehicle is gone.</summary>
    public interface IVehicle : ITracked
    {
        bool Exists { get; }
        Position Position { get; }
    }

    /// <summary>A map marker we created.</summary>
    public interface IBlip : ITracked
    {
        bool Exists { get; }
        string Name { set; }
        bool ShowRoute { set; }
        MarkerColor Color { set; }
    }

    public enum MarkerColor
    {
        /// <summary>A place to go to.</summary>
        Yellow,
        /// <summary>A suspect.</summary>
        Red,
    }

    /// <summary>
    /// Spawns peds into a scope. Never blocks: the first calls for a model start streaming it and
    /// return false; keep calling each tick until it returns true.
    /// </summary>
    public interface IPedFactory
    {
        /// <exception cref="System.ArgumentException">The model name is not a ped model.</exception>
        bool TrySpawn(EntityScope scope, string model, Position at, float heading, out IPed? ped);
    }

    /// <summary>Spawns vehicles into a scope. Same non-blocking contract as <see cref="IPedFactory"/>.</summary>
    public interface IVehicleFactory
    {
        /// <exception cref="System.ArgumentException">The model name is not a vehicle model.</exception>
        bool TrySpawn(EntityScope scope, string model, Position at, float heading, out IVehicle? vehicle);
    }

    /// <summary>Creates map markers into a scope.</summary>
    public interface IBlips
    {
        IBlip At(EntityScope scope, Position position);

        /// <summary>A marker that follows <paramref name="ped"/> (one from <see cref="IPedFactory"/>).</summary>
        IBlip On(EntityScope scope, IPed ped);
    }
}
