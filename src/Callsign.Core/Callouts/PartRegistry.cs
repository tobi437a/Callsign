using System;
using System.Collections.Generic;

namespace Callsign.Core.Callouts
{
    /// <summary>
    /// The parts of one kind the generator can draw from. Built-in content, data packs and plugins
    /// all add through <see cref="Add"/>; disposing the returned handle removes the part again
    /// (used when a plugin unloads).
    /// </summary>
    public sealed class PartRegistry<T> where T : ICalloutPart
    {
        readonly List<T> _parts = new List<T>();

        public IReadOnlyList<T> All => _parts;

        /// <exception cref="ArgumentException">The id is empty or already registered, or the part is incomplete.</exception>
        public IDisposable Add(T part)
        {
            if (part == null) throw new ArgumentNullException(nameof(part));
            if (string.IsNullOrWhiteSpace(part.Id)) throw new ArgumentException("A part needs an id", nameof(part));
            part.Validate();
            if (Contains(part.Id)) throw new ArgumentException("A " + typeof(T).Name + " with id '" + part.Id + "' is already registered", nameof(part));
            _parts.Add(part);
            return new Removal(() => _parts.Remove(part));
        }

        public bool Contains(string id) => _parts.Exists(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

        sealed class Removal : IDisposable
        {
            Action? _remove;
            public Removal(Action remove) { _remove = remove; }
            public void Dispose() { _remove?.Invoke(); _remove = null; }
        }
    }

    /// <summary>All part registries, one per kind.</summary>
    public sealed class CalloutParts
    {
        public PartRegistry<SituationPart> Situations { get; } = new PartRegistry<SituationPart>();
        public PartRegistry<LocationPart> Locations { get; } = new PartRegistry<LocationPart>();
        public PartRegistry<SuspectPart> Suspects { get; } = new PartRegistry<SuspectPart>();
        public PartRegistry<OutcomePart> Outcomes { get; } = new PartRegistry<OutcomePart>();
        public PartRegistry<ComplicationPart> Complications { get; } = new PartRegistry<ComplicationPart>();
    }
}
