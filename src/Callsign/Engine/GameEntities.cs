using System;
using GTA;
using GTA.Math;
using Callsign.Core.Entities;
using Callsign.Core.Ports;
using Callsign.Core.World;

namespace Callsign.Engine
{
    internal static class VectorExtensions
    {
        public static Position ToPosition(this Vector3 v) => new Position(v.X, v.Y, v.Z);
        public static Vector3 ToVector(this Position p) => new Vector3(p.X, p.Y, p.Z);
    }

    internal sealed class GameWorld : IWorld
    {
        public Position PlayerPosition => Game.Player.Character.Position.ToPosition();

        public bool PlayerOnFoot => Game.Player.Character.IsOnFoot;

        public bool PlayerDead => Game.Player.IsDead;

        public bool TryFindStreetNear(Position near, out Position position, out float heading)
        {
            Vector3 found = World.GetNextPositionOnStreetWithHeading(near.ToVector(), out heading, false);
            position = found.ToPosition();
            return found != Vector3.Zero;
        }

        public bool TryFindPedSpotNear(Position near, out Position position)
        {
            Vector3 found = World.GetSafeCoordForPed(near.ToVector(), true);
            if (found == Vector3.Zero) found = World.GetSafeCoordForPed(near.ToVector(), false);
            position = found.ToPosition();
            return found != Vector3.Zero;
        }

        public string DescribeLocation(Position position)
        {
            Vector3 v = position.ToVector();
            string street = World.GetStreetName(v);
            string zone = World.GetZoneLocalizedName(v);
            if (string.IsNullOrEmpty(street)) return zone;
            return string.IsNullOrEmpty(zone) ? street : street + ", " + zone;
        }
    }

    internal sealed class GamePed : IPed
    {
        public GamePed(Ped ped) { Ped = ped; }

        public Ped Ped { get; }
        public bool Exists => Ped.Exists();
        public bool IsDead => !Ped.Exists() || Ped.IsDead;
        public Position Position => Ped.Position.ToPosition();

        public void GiveWeapon(string weapon)
        {
            if (Ped.Exists()) Ped.Weapons.Give((WeaponHash)(uint)Game.GenerateHash(weapon), 250, true, true);
        }

        public void Wander() => Order(t => t.WanderAround());

        public void FacePlayer() => Order(t => t.TurnTo(Game.Player.Character, -1));

        public void HandsUp() => Order(t => t.HandsUp(-1));

        public void FleeFromPlayer() => Order(t => t.FleeFrom(Game.Player.Character));

        public void FightPlayer() => Order(t => t.Combat(Game.Player.Character));

        /// <summary>Replaces the ped's task. Our orders stick: the ped doesn't drop them to react to the world.</summary>
        void Order(Action<TaskInvoker> task)
        {
            if (!Ped.Exists()) return;
            Ped.BlockPermanentEvents = true;
            Ped.AlwaysKeepTask = true;
            Ped.Task.ClearAll();
            task(Ped.Task);
        }

        public void Release(ReleaseMode mode)
        {
            // Back to normal ped AI, so a dismissed suspect doesn't stand with hands up forever.
            if (mode == ReleaseMode.Dismiss && Ped.Exists() && !Ped.IsDead)
            {
                Ped.BlockPermanentEvents = false;
                Ped.AlwaysKeepTask = false;
                Ped.Task.ClearAll();
            }
            ReleaseEntity(Ped, mode);
        }

        internal static void ReleaseEntity(Entity entity, ReleaseMode mode)
        {
            if (!entity.Exists()) return;
            if (mode == ReleaseMode.Delete) entity.Delete();
            else entity.MarkAsNoLongerNeeded();
        }
    }

    internal sealed class GameVehicle : IVehicle
    {
        public GameVehicle(Vehicle vehicle) { Vehicle = vehicle; }

        public Vehicle Vehicle { get; }
        public bool Exists => Vehicle.Exists();
        public Position Position => Vehicle.Position.ToPosition();

        public void Release(ReleaseMode mode) => GamePed.ReleaseEntity(Vehicle, mode);
    }

    internal sealed class GameBlip : IBlip
    {
        readonly Blip _blip;

        public GameBlip(Blip blip) { _blip = blip; }

        public bool Exists => _blip.Exists();
        public string Name { set { if (_blip.Exists()) _blip.Name = value; } }
        public bool ShowRoute { set { if (_blip.Exists()) _blip.ShowRoute = value; } }
        public MarkerColor Color { set { if (_blip.Exists()) _blip.Color = value == MarkerColor.Red ? BlipColor.Red : BlipColor.Yellow; } }

        // Markers are always removed, whatever the mode: a dismissed scene leaves nothing on the map.
        public void Release(ReleaseMode mode) { if (_blip.Exists()) _blip.Delete(); }
    }

    /// <summary>Streams models without blocking: request, then report "not yet" until loaded.</summary>
    internal static class ModelStreaming
    {
        public static bool TryLoad(string name, Func<Model, bool> isRightKind, out Model model)
        {
            model = new Model(name);
            if (!model.IsInCdImage || !isRightKind(model))
                throw new ArgumentException("Unknown or wrong kind of model: " + name, nameof(name));
            if (model.IsLoaded) return true;
            model.Request();
            return false;
        }
    }

    internal sealed class GamePedFactory : IPedFactory
    {
        public bool TrySpawn(EntityScope scope, string model, Position at, float heading, out IPed? ped)
        {
            ped = null;
            if (!ModelStreaming.TryLoad(model, m => m.IsPed, out Model m)) return false;
            // The model is loaded, so CreatePed doesn't wait for it.
            Ped created = World.CreatePed(m, at.ToVector(), heading);
            m.MarkAsNoLongerNeeded();
            if (created == null) return false;   // ped pool full: try again later
            ped = scope.Track(new GamePed(created));
            return true;
        }
    }

    internal sealed class GameVehicleFactory : IVehicleFactory
    {
        public bool TrySpawn(EntityScope scope, string model, Position at, float heading, out IVehicle? vehicle)
        {
            vehicle = null;
            if (!ModelStreaming.TryLoad(model, m => m.IsVehicle, out Model m)) return false;
            Vehicle created = World.CreateVehicle(m, at.ToVector(), heading);
            m.MarkAsNoLongerNeeded();
            if (created == null) return false;
            vehicle = scope.Track(new GameVehicle(created));
            return true;
        }
    }

    internal sealed class GameBlips : IBlips
    {
        public IBlip At(EntityScope scope, Position position) =>
            scope.Track(new GameBlip(World.CreateBlip(position.ToVector())));

        public IBlip On(EntityScope scope, IPed ped) =>
            scope.Track(new GameBlip(((GamePed)ped).Ped.AddBlip()));
    }
}
