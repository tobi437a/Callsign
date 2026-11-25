using Callsign.Core.Duty;
using Callsign.Core.Events;
using Xunit;

namespace Callsign.Core.Tests;

public class EventBusTests
{
    sealed record Ping(int N);

    [Fact]
    public void DeliversToSubscribersOfThatType()
    {
        var bus = new EventBus(ex => throw ex);
        int got = 0;
        bus.Subscribe<Ping>(p => got = p.N);
        bus.Subscribe<string>(_ => got = -1);

        bus.Publish(new Ping(7));

        Assert.Equal(7, got);
    }

    [Fact]
    public void DisposingTheSubscriptionStopsDelivery()
    {
        var bus = new EventBus(ex => throw ex);
        int calls = 0;
        var sub = bus.Subscribe<Ping>(_ => calls++);

        sub.Dispose();
        bus.Publish(new Ping(1));

        Assert.Equal(0, calls);
    }

    [Fact]
    public void AThrowingHandlerDoesNotStopTheOthers()
    {
        var errors = 0;
        var bus = new EventBus(_ => errors++);
        var reached = false;
        bus.Subscribe<Ping>(_ => throw new InvalidOperationException());
        bus.Subscribe<Ping>(_ => reached = true);

        bus.Publish(new Ping(1));

        Assert.True(reached);
        Assert.Equal(1, errors);
    }
}

public class DutyServiceTests
{
    [Fact]
    public void ToggleFlipsStateAndAnnouncesIt()
    {
        var bus = new EventBus(ex => throw ex);
        var duty = new DutyService(bus);
        DutyChanged? seen = null;
        bus.Subscribe<DutyChanged>(e => seen = e);

        duty.Toggle();

        Assert.True(duty.OnDuty);
        Assert.Equal(new DutyChanged(true), seen);
    }

    [Fact]
    public void SettingTheSameStateDoesNotAnnounce()
    {
        var bus = new EventBus(ex => throw ex);
        var duty = new DutyService(bus);
        int events = 0;
        bus.Subscribe<DutyChanged>(_ => events++);

        duty.SetOnDuty(false);

        Assert.Equal(0, events);
    }
}
