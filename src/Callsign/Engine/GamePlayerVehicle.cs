using Callsign.Core.Ports;
using GTA;

namespace Callsign.Engine
{
    internal sealed class GamePlayerVehicle : IPlayerVehicle
    {
        /// <summary>On foot, the last vehicle counts while the player is this close to it.</summary>
        const float NearbyDistance = 20;

        public bool Available => Find() != null;

        public bool Repair()
        {
            Vehicle? vehicle = Find();
            if (vehicle == null) return false;
            vehicle.Repair();   // body, engine, tyres, windows, lights
            vehicle.DirtLevel = 0;
            return true;
        }

        static Vehicle? Find()
        {
            Ped player = Game.Player.Character;
            Vehicle? vehicle = player.CurrentVehicle;
            if (vehicle == null)
            {
                vehicle = player.LastVehicle;
                if (vehicle != null && vehicle.Exists() && vehicle.Position.DistanceTo(player.Position) > NearbyDistance) vehicle = null;
            }
            return vehicle != null && vehicle.Exists() && !vehicle.IsDead ? vehicle : null;
        }
    }
}
