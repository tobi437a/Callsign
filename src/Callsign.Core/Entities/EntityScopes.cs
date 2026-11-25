using System.Collections.Generic;
using Callsign.Core.Ports;

namespace Callsign.Core.Entities
{
    /// <summary>
    /// Opens scopes and knows which are still open. The bootstrap closes whatever is left on
    /// reload/unload, so a module that forgets its scope still can't leak entities.
    /// </summary>
    public sealed class EntityScopes
    {
        readonly List<EntityScope> _open = new List<EntityScope>();
        readonly ILog _log;

        public EntityScopes(ILog log) { _log = log; }

        public int OpenCount => _open.Count;

        public EntityScope Open(string name)
        {
            var scope = new EntityScope(name, _log, s => _open.Remove(s));
            _open.Add(scope);
            return scope;
        }

        /// <summary>Closes every open scope. Returns how many were still open.</summary>
        public int CloseAll(ReleaseMode mode)
        {
            int count = _open.Count;
            foreach (EntityScope scope in _open.ToArray())
                scope.Close(mode);
            return count;
        }
    }
}
