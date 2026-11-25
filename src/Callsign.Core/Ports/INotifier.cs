namespace Callsign.Core.Ports
{
    /// <summary>Messages to the player. Text may use the game's colour codes (~r~, ~b~, ~s~, ...).</summary>
    public interface INotifier
    {
        /// <summary>A notification above the minimap (dispatch, status changes).</summary>
        void Notify(string text);

        /// <summary>A line at the bottom of the screen.</summary>
        void Subtitle(string text, int durationMs);

        /// <summary>A help box in the top left, for this frame only: call it every tick while it applies.</summary>
        void Help(string text);
    }
}
