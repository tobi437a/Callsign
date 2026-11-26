using Callsign.Core.Events;

namespace Callsign.Core.Duty
{
    /// <summary>
    /// Owns the duty state and what the officer is busy with. The only writer of either;
    /// announces duty changes on the bus.
    /// </summary>
    public sealed class DutyService
    {
        readonly IEventBus _bus;

        public DutyService(IEventBus bus) { _bus = bus; }

        public bool OnDuty { get; private set; }

        /// <summary>What the officer is dealing with ("callout", "traffic stop"), or null when free.</summary>
        public string? Activity { get; private set; }

        public bool Busy => Activity != null;

        public void SetOnDuty(bool onDuty)
        {
            if (onDuty == OnDuty) return;
            OnDuty = onDuty;
            if (!onDuty) Activity = null;
            _bus.Publish(new DutyChanged(onDuty));
        }

        public void Toggle() => SetOnDuty(!OnDuty);

        /// <summary>Claims the officer for <paramref name="activity"/>. False when off duty or busy with something else.</summary>
        public bool TryBegin(string activity)
        {
            if (!OnDuty || (Activity != null && Activity != activity)) return false;
            Activity = activity;
            return true;
        }

        /// <summary>Frees the officer, if <paramref name="activity"/> is what they were busy with.</summary>
        public void End(string activity)
        {
            if (Activity == activity) Activity = null;
        }
    }
}
