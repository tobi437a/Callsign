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

        /// <summary>A suspect vehicle reacts to the player this close: a calm driver pulls over for lights, others flee.</summary>
        public float VehicleEngageDistance { get; set; } = 35;
        /// <summary>A fleeing vehicle this far from the player has got away.</summary>
        public float VehicleEscapeDistance { get; set; } = 450;
        /// <summary>A vehicle slower than this (m/s) counts as stopped.</summary>
        public float StoppedSpeed { get; set; } = 1.5f;
        /// <summary>A vehicle pulling over counts as stopped once it has been this slow for this long.</summary>
        public long PulledOverMs { get; set; } = 1_000;
        /// <summary>A fleeing vehicle that has stood still this long is abandoned: everyone bails out.</summary>
        public long BailAfterStoppedMs { get; set; } = 2_500;
        /// <summary>A driver who has decided to stop gets out once slower than this (m/s), or after <see cref="StopTimeoutMs"/>.</summary>
        public float GetOutSpeed { get; set; } = 3;
        public long StopTimeoutMs { get; set; } = 8_000;

        /// <summary>How fleeing drivers wear down and give up.</summary>
        public PursuitSettings Pursuit { get; set; } = new PursuitSettings();
    }
}
