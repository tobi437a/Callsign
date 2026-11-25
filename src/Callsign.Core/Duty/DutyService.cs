using Callsign.Core.Events;

namespace Callsign.Core.Duty
{
    /// <summary>Owns the duty state. The only writer of it; announces changes on the bus.</summary>
    public sealed class DutyService
    {
        readonly IEventBus _bus;

        public DutyService(IEventBus bus) { _bus = bus; }

        public bool OnDuty { get; private set; }

        public void SetOnDuty(bool onDuty)
        {
            if (onDuty == OnDuty) return;
            OnDuty = onDuty;
            _bus.Publish(new DutyChanged(onDuty));
        }

        public void Toggle() => SetOnDuty(!OnDuty);
    }
}
