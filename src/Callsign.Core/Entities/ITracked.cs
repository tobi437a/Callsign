namespace Callsign.Core.Entities
{
    /// <summary>Something we created in the world (ped, vehicle, blip) that a scope must clean up.</summary>
    public interface ITracked
    {
        /// <summary>Hands the thing back to the game or removes it. Must be safe to call when it's already gone.</summary>
        void Release(ReleaseMode mode);
    }

    public enum ReleaseMode
    {
        /// <summary>
        /// Scene ended normally: peds and vehicles go back to the game, which despawns them once
        /// out of sight (no popping). Markers are removed.
        /// </summary>
        Dismiss,

        /// <summary>Script reload/unload: remove everything right now, so no ghost peds are left behind.</summary>
        Delete,
    }
}
