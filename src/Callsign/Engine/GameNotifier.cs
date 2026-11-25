using Callsign.Core.Ports;

namespace Callsign.Engine
{
    internal sealed class GameNotifier : INotifier
    {
        public void Notify(string text) => GTA.UI.Notification.PostTicker(text, false, false);

        public void Subtitle(string text, int durationMs) => GTA.UI.Screen.ShowSubtitle(text, durationMs);

        public void Help(string text) => GTA.UI.Screen.ShowHelpTextThisFrame(text);
    }
}
