namespace Callsign.Core.Callouts
{
    /// <summary>Timings and distances for callouts. Times in game milliseconds, distances in metres.</summary>
    public sealed class CalloutSettings
    {
        public float MinDistance { get; set; } = 250;
        public float MaxDistance { get; set; } = 1200;

        public long FirstOfferMinMs { get; set; } = 20_000;
        public long FirstOfferMaxMs { get; set; } = 40_000;
        public long NextOfferMinMs { get; set; } = 60_000;
        public long NextOfferMaxMs { get; set; } = 120_000;
        /// <summary>Wait before trying again when no incident fits where the player is.</summary>
        public long RetryMs { get; set; } = 15_000;
        public long OfferTimeoutMs { get; set; } = 20_000;

        /// <summary>The suspect spawns once the player is this close to the scene.</summary>
        public float SpawnDistance { get; set; } = 150;
        /// <summary>The player counts as on scene this close to the suspect.</summary>
        public float OnSceneDistance { get; set; } = 30;
        /// <summary>How close the player must be (on foot) to deal with the suspect.</summary>
        public float InteractDistance { get; set; } = 3;
        /// <summary>A fleeing suspect gives up when the player gets this close on foot.</summary>
        public float CatchDistance { get; set; } = 2.5f;
        /// <summary>A suspect this far from the player has got away.</summary>
        public float EscapeDistance { get; set; } = 250;
    }
}
