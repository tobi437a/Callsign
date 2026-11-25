using Callsign.Core.Ui;

namespace Callsign.Core.Ports
{
    /// <summary>Shows a menu on screen.</summary>
    public interface IMenuView
    {
        /// <summary>
        /// Draws <paramref name="menu"/> for this frame only (call every tick while it is open) and
        /// keeps the game from using the menu keys meanwhile.
        /// </summary>
        void Draw(Menu menu);
    }
}
