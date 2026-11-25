using GTA.Math;

namespace Callsign.API.Callouts
{
    /// <summary>A snapshot of one call: which parts it was built from.</summary>
    public sealed class CalloutInfo
    {
        /// <summary>Creates the snapshot (done by Callsign).</summary>
        public CalloutInfo(string title, string dispatchMessage, string situationId, string locationId,
            string locationName, Vector3 position, string suspectId, string outcomeId)
        {
            Title = title;
            DispatchMessage = dispatchMessage;
            SituationId = situationId;
            LocationId = locationId;
            LocationName = locationName;
            Position = position;
            SuspectId = suspectId;
            OutcomeId = outcomeId;
        }

        /// <summary>Call title.</summary>
        public string Title { get; }
        /// <summary>The dispatch line as shown to the player.</summary>
        public string DispatchMessage { get; }
        /// <summary>Id of the situation part.</summary>
        public string SituationId { get; }
        /// <summary>Id of the location part ("street" for a street picked near the player).</summary>
        public string LocationId { get; }
        /// <summary>Name of the location.</summary>
        public string LocationName { get; }
        /// <summary>Position of the location.</summary>
        public Vector3 Position { get; }
        /// <summary>Id of the suspect part.</summary>
        public string SuspectId { get; }
        /// <summary>Id of the outcome part. The player doesn't know it yet, so don't give it away.</summary>
        public string OutcomeId { get; }
    }
}
