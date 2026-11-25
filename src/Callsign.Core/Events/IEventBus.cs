using System;

namespace Callsign.Core.Events
{
    /// <summary>
    /// In-process publish/subscribe between modules, so features never reference each other.
    /// Events are immutable records. Delivery is synchronous and by exact type.
    /// </summary>
    public interface IEventBus
    {
        /// <summary>Subscribes; dispose the result to unsubscribe.</summary>
        IDisposable Subscribe<TEvent>(Action<TEvent> handler);

        void Publish<TEvent>(TEvent evt);
    }
}
