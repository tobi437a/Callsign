using Callsign.Core.Settings;

namespace Callsign.Core.Ports
{
    /// <summary>Where the player's settings are kept between sessions.</summary>
    public interface ISettingsStore
    {
        /// <summary>The saved settings, or defaults when there are none (or they can't be read).</summary>
        ModSettings Load();

        void Save(ModSettings settings);
    }
}
