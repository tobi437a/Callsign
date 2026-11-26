using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
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

        public bool PlayerInVehicle => Game.Player.Character.IsInVehicle();

        public bool TryGetPlayerVehiclePosition(out Position position)
        {
            Vehicle? vehicle = GamePed.PlayerVehicle();
            position = vehicle?.Position.ToPosition() ?? default;
            return vehicle != null;
        }

        public bool PlayerDead => Game.Player.IsDead;

        public bool PlayerLightsOn
        {
            get
            {
                Vehicle? vehicle = Game.Player.Character.CurrentVehicle;
                return vehicle != null && vehicle.Exists() && vehicle.IsSirenActive;
            }
        }

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
        const string CuffDict = "mp_arresting";
        const string CuffAnim = "idle";

        bool _cuffed;

        public GamePed(Ped ped) { Ped = ped; }

        public Ped Ped { get; }
        public bool Exists => Ped.Exists();
        public bool IsDead => !Ped.Exists() || Ped.IsDead;
        public Position Position => Ped.Position.ToPosition();

        public bool IsMale => !Ped.Exists() || Ped.Gender == Gender.Male;

        public void GiveWeapon(string weapon, bool equip)
        {
            if (Ped.Exists()) Ped.Weapons.Give((WeaponHash)(uint)Game.GenerateHash(weapon), 250, equip, true);
        }

        public void RemoveWeapons()
        {
            if (Ped.Exists()) Ped.Weapons.RemoveAll();
        }

        public void Wander() => Order(t => t.WanderAround());

        public void FacePlayer() => Order(t => t.TurnTo(Game.Player.Character, -1));

        public void HandsUp() => Order(t => t.HandsUp(-1));

        public void FleeFromPlayer() => Order(t => t.FleeFrom(Game.Player.Character));

        public void FightPlayer()
        {
            if (Ped.Exists()) Ped.SetCombatAttribute(CombatAttributes.CanLeaveVehicle, true);
            Order(t => t.Combat(Game.Player.Character));
        }

        // --- vehicles ---

        public bool InVehicle => Ped.Exists() && Ped.IsInVehicle();

        public bool PlayerAimingAt
        {
            get
            {
                if (!Ped.Exists()) return false;
                Player player = Game.Player;
                if (player.IsTargeting(Ped)) return true;
                Vehicle? vehicle = Ped.CurrentVehicle;
                return vehicle != null && vehicle.Exists() && player.IsTargeting(vehicle);
            }
        }

        public void WarpInto(IVehicle vehicle, int seat)
        {
            Vehicle v = ((GameVehicle)vehicle).Vehicle;
            if (!Ped.Exists() || !v.Exists()) return;
            VehicleSeat s = seat == 0 ? VehicleSeat.Driver : seat == 1 ? VehicleSeat.Passenger
                : seat == 2 ? VehicleSeat.LeftRear : VehicleSeat.RightRear;
            Ped.SetIntoVehicle(v, s);
        }

        public void DriveAround()
        {
            if (!Driving(out Vehicle v)) return;
            v.IsEngineRunning = true;
            Order(t => t.CruiseWithVehicle(v, 14f, VehicleDrivingFlags.DrivingModeStopForVehicles), onFoot: false);
        }

        public void DriveAway()
        {
            if (!Driving(out Vehicle v)) return;
            v.IsEngineRunning = true;
            Ped.DrivingAggressiveness = 1f;
            Function.Call(Hash.SET_DRIVER_ABILITY, Ped, 1f);
            Order(t => t.StartVehicleMission(v, Game.Player.Character, VehicleMissionType.Flee, 45f,
                VehicleDrivingFlags.DrivingModeAvoidVehiclesReckless, -1f, -1f, true), onFoot: false);
        }

        public void PullOver()
        {
            if (!Driving(out Vehicle v)) return;
            // Roll on a little and steer to the kerb, then brake and stay there.
            Vector3 kerb = v.Position + v.ForwardVector * 20f + v.RightVector * 3.5f;
            Order(_ =>
            {
                using (var sequence = new TaskSequence())
                {
                    sequence.AddTask.DriveTo(v, kerb, 6f, VehicleDrivingFlags.DrivingModeStopForVehicles, 4f);
                    Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, 0, v, 27, 10_000);   // 27 = brake until stopped
                    sequence.Close();
                    Ped.Task.PerformSequence(sequence);
                }
            }, onFoot: false);
        }

        public void WaitInVehicle()
        {
            if (!Driving(out Vehicle v)) return;
            Order(t => t.StandStill(-1), onFoot: false);
            v.IsEngineRunning = false;
        }

        public void ShootFromVehicle()
        {
            if (!Ped.Exists() || !Ped.IsInVehicle()) return;
            Ped.SetCombatAttribute(CombatAttributes.CanLeaveVehicle, false);
            Ped.SetCombatAttribute(CombatAttributes.CanDoDrivebys, true);
            Ped.Weapons.Select(Ped.Weapons.BestWeapon);
            Order(t => t.Combat(Game.Player.Character), onFoot: false);
        }

        /// <summary>The vehicle this ped last sat in, if it's close, drivable, not the player's and nobody's at the wheel.</summary>
        bool OwnCarNearby(out Vehicle vehicle)
        {
            vehicle = Ped.LastVehicle;
            return vehicle != null && vehicle.Exists() && vehicle.IsDriveable && vehicle != PlayerVehicle()
                && vehicle.Driver == null && vehicle.Position.DistanceTo(Ped.Position) < 25f;
        }

        bool Driving(out Vehicle vehicle)
        {
            vehicle = Ped.Exists() ? Ped.CurrentVehicle : null!;
            return vehicle != null && vehicle.Exists() && vehicle.Driver == Ped;
        }

        public bool IsSubdued
        {
            get
            {
                if (!Ped.Exists() || Ped.IsDead) return false;
                if (Ped.IsBeingStunned) return true;
                // Health above the "dies" line, as a share of what it can be: under half = gives up.
                float floor = Math.Max(0f, Ped.FatalInjuryHealthThreshold);
                float range = Ped.MaxHealthFloat - floor;
                return range > 0 && (Ped.HealthFloat - floor) <= range * 0.5f;
            }
        }

        public bool TryCuff()
        {
            if (!Ped.Exists()) return true;
            if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, CuffDict))
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, CuffDict);
                return false;
            }
            Ped.Weapons.RemoveAll();
            _cuffed = true;
            Order(t => t.StandStill(-1));
            return true;
        }

        public void FollowPlayer() =>
            Order(t => t.FollowToOffsetFromEntity(Game.Player.Character, new Vector3(0.6f, -1.2f, 0f), 1.6f, -1, 1.5f, true));

        public bool EnterPlayerVehicle(bool warp)
        {
            Vehicle? vehicle = PlayerVehicle();
            if (vehicle == null || !Ped.Exists()) return false;
            VehicleSeat seat = vehicle.IsSeatFree(VehicleSeat.RightRear) ? VehicleSeat.RightRear
                : vehicle.IsSeatFree(VehicleSeat.LeftRear) ? VehicleSeat.LeftRear
                : VehicleSeat.None;
            if (seat == VehicleSeat.None) return false;

            if (warp)
            {
                Ped.SetIntoVehicle(vehicle, seat);
                Order(t => t.StandStill(-1));
            }
            else
            {
                Order(t => t.EnterVehicle(vehicle, seat, 20_000, 1.5f, EnterVehicleFlags.None));
            }
            return true;
        }

        public bool InPlayerVehicle
        {
            get
            {
                Vehicle? vehicle = PlayerVehicle();
                return vehicle != null && Ped.Exists() && Ped.IsInVehicle(vehicle);
            }
        }

        /// <summary>The vehicle the player is in, or last drove, if it still exists.</summary>
        internal static Vehicle? PlayerVehicle()
        {
            Ped player = Game.Player.Character;
            Vehicle? vehicle = player.CurrentVehicle ?? player.LastVehicle;
            return vehicle != null && vehicle.Exists() && !vehicle.IsDead ? vehicle : null;
        }

        /// <summary>
        /// Replaces the ped's task. Our orders stick: the ped doesn't drop them to react to the world.
        /// An <paramref name="onFoot"/> order makes a ped in a vehicle get out first.
        /// </summary>
        void Order(Action<TaskInvoker> task, bool onFoot = true)
        {
            if (!Ped.Exists()) return;
            Ped.BlockPermanentEvents = true;
            Ped.AlwaysKeepTask = true;
            Ped.Task.ClearAll();
            if (onFoot && Ped.IsInVehicle())
            {
                using (var sequence = new TaskSequence())
                {
                    sequence.AddTask.LeaveVehicle(LeaveVehicleFlags.None);
                    task(sequence.AddTask);
                    sequence.Close();
                    Ped.Task.PerformSequence(sequence);
                }
            }
            else
            {
                task(Ped.Task);
            }
            // Cuffs are an upper-body animation on top of whatever the ped is told to do.
            if (_cuffed)
                Ped.Task.PlayAnimation(CuffDict, CuffAnim, 8f, -8f, -1,
                    AnimationFlags.Loop | AnimationFlags.UpperBodyOnly | AnimationFlags.Secondary, 0f);
        }

        public void Release(ReleaseMode mode)
        {
            // Back to normal ped AI, so a dismissed suspect doesn't stand with hands up forever.
            if (mode == ReleaseMode.Dismiss && Ped.Exists() && !Ped.IsDead)
            {
                Ped.BlockPermanentEvents = false;
                Ped.AlwaysKeepTask = false;
                Ped.Task.ClearAll();
                Ped.Task.ClearSecondary();   // uncuffed
                if (InPlayerVehicle) Ped.Task.LeaveVehicle(LeaveVehicleFlags.None);
                else if (Driving(out Vehicle v))
                {
                    v.IsEngineRunning = true;
                    Ped.Task.CruiseWithVehicle(v, 14f, VehicleDrivingFlags.DrivingModeStopForVehicles);
                }
                else if (OwnCarNearby(out Vehicle car))
                {
                    // A driver who was ordered out goes back to their car and drives on.
                    using (var sequence = new TaskSequence())
                    {
                        sequence.AddTask.EnterVehicle(car, VehicleSeat.Driver, 20_000, 1f, EnterVehicleFlags.None);
                        sequence.AddTask.CruiseWithVehicle(car, 14f, VehicleDrivingFlags.DrivingModeStopForVehicles);
                        sequence.Close();
                        Ped.Task.PerformSequence(sequence);
                    }
                }
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
        public float Speed => Vehicle.Exists() ? Vehicle.Speed : 0f;
        public string Plate => Vehicle.Exists() ? Vehicle.Mods.LicensePlate.Trim() : "";
        public string Model => Vehicle.Exists() ? Vehicle.LocalizedName : "vehicle";
        public bool IsWrecked => !Vehicle.Exists() || Vehicle.IsDead || !Vehicle.IsDriveable;

        // Wheel ids 0..5: front, middle (6-wheelers) and rear pairs.
        public int BurstTyres
        {
            get
            {
                if (!Vehicle.Exists()) return 0;
                int burst = 0;
                for (int wheel = 0; wheel <= 5; wheel++)
                    if (Function.Call<bool>(Hash.IS_VEHICLE_TYRE_BURST, Vehicle, wheel, false)) burst++;
                return burst;
            }
        }

        public float EngineHealth => Vehicle.Exists() ? Math.Max(0f, Math.Min(1f, Vehicle.EngineHealth / 1000f)) : 0f;

        public void Release(ReleaseMode mode) => GamePed.ReleaseEntity(Vehicle, mode);
    }

    internal sealed class GameBlip : IBlip
    {
        readonly Blip _blip;

        public GameBlip(Blip blip) { _blip = blip; }

        public bool Exists => _blip.Exists();
        public string Name { set { if (_blip.Exists()) _blip.Name = value; } }
        public bool ShowRoute { set { if (_blip.Exists()) _blip.ShowRoute = value; } }
        public MarkerColor Color { set { if (_blip.Exists()) _blip.Color = ToBlipColor(value); } }

        static BlipColor ToBlipColor(MarkerColor color) =>
            color == MarkerColor.Red ? BlipColor.Red : color == MarkerColor.Blue ? BlipColor.Blue : BlipColor.Yellow;

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

    internal sealed class GameTraffic : ITraffic
    {
        public bool TryTakeVehicleAhead(EntityScope scope, float maxDistance, out IVehicle? vehicle, out IReadOnlyList<IPed> occupants)
        {
            vehicle = null;
            occupants = Array.Empty<IPed>();
            Ped player = Game.Player.Character;
            Vehicle? own = player.CurrentVehicle;
            if (own == null || !own.Exists()) return false;

            Vehicle? best = null;
            float bestScore = float.MaxValue;
            foreach (Vehicle v in World.GetNearbyVehicles(own.Position, maxDistance))
            {
                if (v == own || !v.Exists() || v.IsDead || v.IsPersistent) continue;   // persistent = a mission's or ours
                Ped? driver = v.Driver;
                if (driver == null || !driver.Exists() || driver.IsPlayer || driver.IsDead) continue;
                Vector3 to = v.Position - own.Position;
                float distance = to.Length();
                if (distance < 1f) continue;
                float ahead = Vector3.Dot(to / distance, own.ForwardVector);                  // 1 = dead ahead
                float sameWay = Vector3.Dot(v.ForwardVector, own.ForwardVector);
                if (ahead < 0.85f || sameWay < 0.3f) continue;
                float score = distance / ahead;
                if (score < bestScore) { best = v; bestScore = score; }
            }
            if (best == null) return false;

            best.IsPersistent = true;
            vehicle = scope.Track(new GameVehicle(best));
            var peds = new List<IPed>();
            foreach (VehicleSeat seat in new[] { VehicleSeat.Driver, VehicleSeat.Passenger, VehicleSeat.LeftRear, VehicleSeat.RightRear })
            {
                Ped? ped = best.GetPedOnSeat(seat);
                if (ped == null || !ped.Exists() || ped.IsDead) continue;
                ped.IsPersistent = true;
                peds.Add(scope.Track(new GamePed(ped)));
            }
            occupants = peds;
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
