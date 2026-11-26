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

        const int HighwayNodeFlag = 64;
        const int StreetNodesToTry = 25;

        /// <summary>The nearest road node that isn't a highway (no foot calls on a freeway, no traffic stops at 120 km/h).</summary>
        public bool TryFindStreetNear(Position near, out Position position, out float heading)
        {
            Vector3 at = near.ToVector();
            for (int n = 1; n <= StreetNodesToTry; n++)
            {
                var nodeOut = new OutputArgument();
                var headingOut = new OutputArgument();
                var lanesOut = new OutputArgument();
                if (!Function.Call<bool>(Hash.GET_NTH_CLOSEST_VEHICLE_NODE_WITH_HEADING, at.X, at.Y, at.Z, n, nodeOut, headingOut, lanesOut, 0, 3f, 0f))
                    break;
                Vector3 node = nodeOut.GetResult<Vector3>();
                var density = new OutputArgument();
                var flags = new OutputArgument();
                if (Function.Call<bool>(Hash.GET_VEHICLE_NODE_PROPERTIES, node.X, node.Y, node.Z, density, flags)
                    && (flags.GetResult<int>() & HighwayNodeFlag) != 0)
                    continue;
                position = node.ToPosition();
                heading = headingOut.GetResult<float>();
                return true;
            }
            position = default;
            heading = 0;
            return false;
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
            ReadyToDrive(v);
            Order(t => t.CruiseWithVehicle(v, 14f, VehicleDrivingFlags.DrivingModeStopForVehicles), onFoot: false);
        }

        public void DriveAway()
        {
            if (!Driving(out Vehicle v)) return;
            ReadyToDrive(v);
            Ped.DrivingAggressiveness = 1f;
            Function.Call(Hash.SET_DRIVER_ABILITY, Ped, 1f);
            Order(t => t.StartVehicleMission(v, Game.Player.Character, VehicleMissionType.Flee, 45f,
                VehicleDrivingFlags.DrivingModeAvoidVehiclesReckless, -1f, -1f, true), onFoot: false);
        }

        public void PullOver()
        {
            if (!Driving(out Vehicle v)) return;
            // Roll on to a spot at the right-hand kerb a little ahead, slow down on the way, then brake there.
            ReadyToDrive(v);
            Vector3 kerb = KerbSpotAhead(v);
            _pullOverSpot = kerb;
            Order(_ =>
            {
                using (var sequence = new TaskSequence())
                {
                    // Avoids traffic but doesn't wait at red lights: a car being pulled over rolls on to the kerb.
                    sequence.AddTask.DriveTo(v, kerb, 7f, VehicleDrivingFlags.DrivingModeAvoidVehicles, 2f);
                    Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, 0, v, 27, 10_000);   // 27 = brake until stopped
                    sequence.Close();
                    Ped.Task.PerformSequence(sequence);
                }
            }, onFoot: false);
        }

        public bool AtPullOverSpot =>
            _pullOverSpot != null && Ped.Exists() && Ped.IsInVehicle()
            && Ped.CurrentVehicle.Position.DistanceTo2D(_pullOverSpot.Value) <= 4f;

        /// <summary>
        /// Where a car pulls over: the right-hand road edge some way ahead, moved in by about half a
        /// car's width so it stands at the kerb rather than on it. Spots in or next to a junction
        /// (crossings, lights, give-way lines) are skipped. Falls back to a plain offset.
        /// </summary>
        /// <summary>How the last pull-over spot was chosen (dev tools).</summary>
        internal static string PullOverDebug = "";
        internal static Vector3 LastPullOverSpot;

        static Vector3 KerbSpotAhead(Vehicle v)
        {
            var debug = new System.Text.StringBuilder();
            Vector3 spot = FindKerbSpot(v, debug);
            PullOverDebug = debug.ToString();
            LastPullOverSpot = spot;
            return spot;
        }

        static Vector3 FindKerbSpot(Vehicle v, System.Text.StringBuilder debug)
        {
            // Which way the road runs here, the way the car is going (a car mid-turn points elsewhere).
            if (!RoadAt(v.Position, v.Heading, out _, out float roadHeading)) roadHeading = v.Heading;
            Vector3 along = Direction(roadHeading);

            // Strict first (clear of junctions and of the queue for their lights); in a dense grid
            // where nothing is, settle for anywhere outside the junction itself.
            foreach (int avoid in new[] { JunctionFlags, Junction })
            {
                // Nearest first after a short roll-on; city blocks are short, so look a fair way ahead too.
                foreach (float distance in new[] { 25f, 32f, 40f, 18f, 50f, 60f, 12f, 75f, 90f, 105f, 120f })
                {
                    Vector3 guess = v.Position + along * distance;
                    if (!RoadAt(guess, roadHeading, out Vector3 node, out float heading)) { debug.Append(distance + ":nonode "); continue; }
                    Vector3 dir = Direction(heading);
                    var right = new Vector3(dir.Y, -dir.X, 0);
                    // Still the same road: not around a bend, onto a crossing street or the far carriageway.
                    // (Nodes are 10-20 m apart along a road, so only the sideways distance says anything.)
                    float turn = Turn(heading, roadHeading), sideways = Math.Abs(Vector3.Dot(node - guess, right));
                    if (turn > 30f || sideways > 8f) { debug.Append(distance + ":turn" + (int)turn + "/side" + (int)sideways + " "); continue; }
                    if (Flags(node, avoid) || Flags(guess, avoid) || Flags(guess + dir * 8f, Junction) || Flags(guess - dir * 8f, Junction))
                    { debug.Append(distance + ":junction "); continue; }
                    if (TryKerb(guess, heading, out Vector3 kerb) || TryKerb(node, heading, out kerb)) { debug.Append(distance + ":OK"); return kerb; }
                    debug.Append(distance + ":noedge" + LastEdge + " ");
                }
                debug.Append("| ");
            }
            debug.Append("FALLBACK");
            return v.Position + along * 25f + new Vector3(along.Y, -along.X, 0) * 3.5f;
        }

        /// <summary>The road node nearest <paramref name="at"/>, with its heading turned to within 90° of <paramref name="heading"/>.</summary>
        static bool RoadAt(Vector3 at, float heading, out Vector3 node, out float roadHeading)
        {
            var nodeOut = new OutputArgument();
            var headingOut = new OutputArgument();
            node = default;
            roadHeading = heading;
            if (!Function.Call<bool>(Hash.GET_CLOSEST_VEHICLE_NODE_WITH_HEADING, at.X, at.Y, at.Z, nodeOut, headingOut, 1, 3f, 0)) return false;
            node = nodeOut.GetResult<Vector3>();
            roadHeading = headingOut.GetResult<float>();
            // Nodes are shared by both directions of a road, so their heading may point either way.
            if (Turn(roadHeading, heading) > 90f) roadHeading = (roadHeading + 180f) % 360f;
            return true;
        }

        static float Turn(float a, float b) => Math.Abs(((a - b) % 360 + 540) % 360 - 180);

        /// <summary>Unit vector for a game heading (0 = north, counter-clockwise).</summary>
        static Vector3 Direction(float heading)
        {
            double r = heading * Math.PI / 180;
            return new Vector3(-(float)Math.Sin(r), (float)Math.Cos(r), 0);
        }

        /// <summary>The right-hand road edge level with <paramref name="at"/> for traffic heading <paramref name="heading"/>, moved in to where a car stands.</summary>
        static int LastEdge;

        /// <summary>
        /// The right-hand road edge level with <paramref name="at"/> for traffic heading <paramref name="heading"/>,
        /// moved in to where a car stands. The game's edge lookup follows the road link's own direction,
        /// which may be either way, so both are asked and the rightmost edge is kept.
        /// </summary>
        static bool TryKerb(Vector3 at, float heading, out Vector3 kerb)
        {
            kerb = default;
            LastEdge = -99;
            Vector3 dir = Direction(heading);
            var right = new Vector3(dir.Y, -dir.X, 0);
            float best = float.MinValue;
            Vector3 bestEdge = default;
            foreach (float h in new[] { heading, (heading + 180f) % 360f })
            {
                var edge = new OutputArgument();
                if (!Function.Call<bool>(Hash.GET_ROAD_BOUNDARY_USING_HEADING, at.X, at.Y, at.Z, h, edge)) continue;
                Vector3 boundary = edge.GetResult<Vector3>();
                float offset = Vector3.Dot(boundary - at, right);
                LastEdge = (int)offset;
                if (offset > best) { best = offset; bestEdge = boundary; }   // the rightmost edge is ours
            }
            // Within reason: the point may already be near (or just over) the kerb, but not far off the road.
            if (best < -6f || best > 15f) return false;
            kerb = at + right * (best - 1.4f);
            kerb.Z = bestEdge.Z;
            return true;
        }

        // Road node flags (game data): 128 = junction, 256 = approach to traffic lights, 512 = give way.
        const int Junction = 128, JunctionFlags = 128 | 256 | 512;

        /// <summary>The road node nearest <paramref name="at"/> has any of <paramref name="mask"/>.</summary>
        static bool Flags(Vector3 at, int mask)
        {
            var density = new OutputArgument();
            var flags = new OutputArgument();
            if (!Function.Call<bool>(Hash.GET_VEHICLE_NODE_PROPERTIES, at.X, at.Y, at.Z, density, flags)) return false;
            return (flags.GetResult<int>() & mask) != 0;
        }

        /// <summary>The spot the driver was last sent to pull over at.</summary>
        Vector3? _pullOverSpot;

        public void WaitInVehicle()
        {
            if (!Driving(out Vehicle v)) return;
            _pullOverSpot = null;
            // Brake "forever" (rather than stand still, which in a car lets the driving AI pick up
            // again), engine off and kept off, handbrake on.
            Order(_ => Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, Ped, v, 1, int.MaxValue), onFoot: false);
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, v, false, true, true);
            Function.Call(Hash.SET_VEHICLE_HANDBRAKE, v, true);
        }

        /// <summary>Undoes <see cref="WaitInVehicle"/> before the ped drives again.</summary>
        static void ReadyToDrive(Vehicle v)
        {
            Function.Call(Hash.SET_VEHICLE_HANDBRAKE, v, false);
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, v, true, true, false);
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
                    ReadyToDrive(v);
                    Ped.Task.CruiseWithVehicle(v, 14f, VehicleDrivingFlags.DrivingModeStopForVehicles);
                }
                else if (OwnCarNearby(out Vehicle car))
                {
                    // A driver who was ordered out goes back to their car and drives on.
                    ReadyToDrive(car);
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
