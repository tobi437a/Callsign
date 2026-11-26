using System.Collections.Generic;
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

        /// <summary>The player is aiming a weapon at this ped, or at the vehicle they are in.</summary>
        bool PlayerAimingAt { get; }

        /// <summary>Sitting in a vehicle (any vehicle).</summary>
        bool InVehicle { get; }

        /// <summary>Puts the ped straight into a seat: 0 = driver, 1 = front passenger, 2 and 3 = back seats.</summary>
        void WarpInto(IVehicle vehicle, int seat);

        /// <summary>Drives around like normal traffic.</summary>
        void DriveAround();

        /// <summary>Drives away from the player, fast and recklessly.</summary>
        void DriveAway();

        /// <summary>Pulls over to the side of the road a little ahead and stops there.</summary>
        void PullOver();

        /// <summary>After <see cref="PullOver"/>: the vehicle has reached the spot it was pulling over at.</summary>
        bool AtPullOverSpot { get; }

        /// <summary>Stays stopped at the wheel with the engine off (pulled over, waiting for the officer).</summary>
        void WaitInVehicle();

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

    /// <summary>A vehicle we spawned (or took over). All members are safe to use after the vehicle is gone.</summary>
    public interface IVehicle : ITracked
    {
        bool Exists { get; }
        Position Position { get; }

        /// <summary>The licence plate text.</summary>
        string Plate { get; }

        /// <summary>What it is, as a player would name it (e.g. "Futo").</summary>
        string Model { get; }

        /// <summary>Metres per second.</summary>
        float Speed { get; }

        /// <summary>Destroyed or no longer drivable.</summary>
        bool IsWrecked { get; }

        /// <summary>How many tyres are burst (shot out, spiked).</summary>
        int BurstTyres { get; }

        /// <summary>0 = dead engine, 1 = undamaged.</summary>
        float EngineHealth { get; }
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

    /// <summary>The traffic around the player.</summary>
    public interface ITraffic
    {
        /// <summary>
        /// Takes over the vehicle driving just ahead of the player's vehicle (within
        /// <paramref name="maxDistance"/>, roughly the same way), with everyone in it, into
        /// <paramref name="scope"/>. False if there is none. <paramref name="occupants"/> start with the driver.
        /// </summary>
        bool TryTakeVehicleAhead(EntityScope scope, float maxDistance, out IVehicle? vehicle, out IReadOnlyList<IPed> occupants);
    }

    /// <summary>The people walking around the player.</summary>
    public interface IPedestrians
    {
        /// <summary>Someone <see cref="TryTakePedNearby"/> would take is there right now.</summary>
        bool AnyoneToStop(float maxDistance);

        /// <summary>
        /// Takes over the pedestrian on foot closest in front of the player (within
        /// <paramref name="maxDistance"/>; not police, not someone a mission or we already use)
        /// into <paramref name="scope"/>. False if there is none.
        /// </summary>
        bool TryTakePedNearby(EntityScope scope, float maxDistance, out IPed? ped);
    }

    /// <summary>Creates map markers into a scope.</summary>
    public interface IBlips
    {
        IBlip At(EntityScope scope, Position position);

        /// <summary>A marker that follows <paramref name="ped"/> (one from <see cref="IPedFactory"/>).</summary>
        IBlip On(EntityScope scope, IPed ped);
    }
}
