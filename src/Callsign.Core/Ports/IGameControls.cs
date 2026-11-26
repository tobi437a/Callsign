namespace Callsign.Core.Ports
{
    /// <summary>The game's own use of keys the mod also uses.</summary>
    public interface IGameControls
    {
        /// <summary>
        /// For this frame only, the game ignores the interact key (E) itself, so holding it to stop
        /// someone doesn't also hail a taxi or start some other game action. Call it every tick while
        /// the key is ours.
        /// </summary>
        void BlockInteractKey();
    }

    /// <summary>Does nothing (tests, or no game).</summary>
    public sealed class NoGameControls : IGameControls
    {
        public static readonly NoGameControls Instance = new NoGameControls();
        public void BlockInteractKey() { }
    }
}
