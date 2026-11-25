using System;
using System.Collections.Generic;

namespace Callsign.Core.Events
{
    /// <summary>
    /// Default <see cref="IEventBus"/>. A throwing handler is reported to the error sink and does
    /// not stop delivery to the other handlers. Single-threaded (the game script thread).
    /// </summary>
    public sealed class EventBus : IEventBus
    {
        readonly Dictionary<Type, List<Delegate>> _handlers = new Dictionary<Type, List<Delegate>>();
        readonly Action<Exception> _onHandlerError;

        public EventBus(Action<Exception> onHandlerError)
        {
            _onHandlerError = onHandlerError;
        }

        public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (!_handlers.TryGetValue(typeof(TEvent), out var list))
                _handlers[typeof(TEvent)] = list = new List<Delegate>();
            list.Add(handler);
            return new Subscription(() => list.Remove(handler));
        }

        public void Publish<TEvent>(TEvent evt)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out var list)) return;
            foreach (var handler in list.ToArray())   // snapshot: handlers may (un)subscribe
            {
                try { ((Action<TEvent>)handler)(evt); }
                catch (Exception ex) { _onHandlerError(ex); }
            }
        }

        sealed class Subscription : IDisposable
        {
            Action? _unsubscribe;
            public Subscription(Action unsubscribe) { _unsubscribe = unsubscribe; }
            public void Dispose() { _unsubscribe?.Invoke(); _unsubscribe = null; }
        }
    }
}
