using System;
using System.Collections.Generic;
using Callsign.Core.Ports;

namespace Callsign.Core.Entities
{
    /// <summary>
    /// Owns everything one scene (a callout, a traffic stop, ...) spawned. Closing the scope releases
    /// it all, newest first. Open scopes through <see cref="EntityScopes"/> so a reload can close them.
    /// </summary>
    public sealed class EntityScope
    {
        readonly List<ITracked> _items = new List<ITracked>();
        readonly ILog _log;
        readonly Action<EntityScope> _onClosed;

        internal EntityScope(string name, ILog log, Action<EntityScope> onClosed)
        {
            Name = name;
            _log = log;
            _onClosed = onClosed;
        }

        public string Name { get; }

        public bool IsClosed { get; private set; }

        public int Count => _items.Count;

        /// <summary>
        /// Takes ownership of <paramref name="item"/>. On a closed scope the item is deleted at once
        /// (a late spawn from a finished scene must not leak).
        /// </summary>
        public T Track<T>(T item) where T : ITracked
        {
            if (IsClosed)
            {
                _log.Warn("Scope '" + Name + "' is closed; deleting an entity tracked after close");
                Release(item, ReleaseMode.Delete);
                return item;
            }
            _items.Add(item);
            return item;
        }

        /// <summary>Releases everything, newest first. Calling it again does nothing.</summary>
        public void Close(ReleaseMode mode)
        {
            if (IsClosed) return;
            IsClosed = true;
            for (int i = _items.Count - 1; i >= 0; i--)
                Release(_items[i], mode);
            _items.Clear();
            _onClosed(this);
        }

        void Release(ITracked item, ReleaseMode mode)
        {
            try { item.Release(mode); }
            catch (Exception ex) { _log.Error("Scope '" + Name + "': releasing an entity failed", ex); }
        }
    }
}
