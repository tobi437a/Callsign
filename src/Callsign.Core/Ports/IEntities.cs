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

        bool IsMale { get; }

        /// <summary>Gives a weapon by its game name (e.g. "WEAPON_KNIFE"); <paramref name="equip"/> puts it in their hand.</summary>
        void GiveWeapon(string weapon, bool equip);

        /// <summary>Takes all their weapons.</summary>
        void RemoveWeapons();

        // Behaviour orders. Each replaces whatever the ped was doing; all do nothing once the ped is gone.
        // The on-foot orders (up to FightPlayer, and FollowPlayer) make a ped in a vehicle get out first.

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

        /// <summary>Stunned, or hurt enough that they stop fighting or running.</summary>
        bool IsSubdued { get; }

        // Vehicles. The driving orders do nothing unless the ped is driving.

        /// <summary>Sitting in a vehicle (any vehicle).</summary>
        bool InVehicle { get; }

        /// <summary>Puts the ped straight into a seat: 0 = driver, 1 = front passenger, 2 and 3 = back seats.</summary>
        void WarpInto(IVehicle vehicle, int seat);

        /// <summary>Drives around like normal traffic.</summary>
        void DriveAround();

        /// <summary>Drives away from the player, fast and recklessly.</summary>
        void DriveAway();

        /// <summary>Pulls over to the side of the road and stops there.</summary>
        void PullOver();

        /// <summary>Shoots at the player from their seat (a passenger with a gun). Stays in the vehicle.</summary>
        void ShootFromVehicle();

        // Arrest. A cuffed ped keeps their cuffs through the orders below.

        /// <summary>
        /// Takes their weapons and cuffs them. Never blocks: returns false while the animation is
        /// still loading; call again next tick. True once cuffed (or if the ped is gone).
        /// </summary>
        bool TryCuff();

        /// <summary>Walks just behind the player.</summary>
        void FollowPlayer();

        /// <summary>
        /// Gets into a free back seat of the player's current (or last) vehicle.
        /// <paramref name="warp"/> puts them in at once. False if there is no such seat.
        /// </summary>
        bool EnterPlayerVehicle(bool warp);

        /// <summary>Sitting in the player's current (or last) vehicle.</summary>
        bool InPlayerVehicle { get; }
    }

    /// <summary>A vehicle we spawned. All members are safe to use after the vehicle is gone.</summary>
    public interface IVehicle : ITracked
    {
        bool Exists { get; }
        Position Position { get; }

        /// <summary>Metres per second.</summary>
        float Speed { get; }

        /// <summary>Destroyed or no longer drivable.</summary>
        bool IsWrecked { get; }
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
        /// <summary>A prisoner, or a police place.</summary>
        Blue,
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
