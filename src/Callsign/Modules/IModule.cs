namespace Callsign.Modules
{
    /// <summary>
    /// One feature of the mod. Created and wired by the bootstrap, ticked in registration order.
    /// Modules talk to each other only through the event bus and shared Core services.
    /// </summary>
    internal interface IModule
    {
        string Name { get; }

        void Start();

        /// <summary>Every frame, on the game script thread. Never block (no Script.Wait).</summary>
        void Tick();

        /// <summary>Script reload/unload. Must leave the world as the game expects it.</summary>
        void Stop();
    }
}
