using Callsign.Core.Arrest;
using Callsign.Core.Callouts;
using Callsign.Core.Duty;
using Callsign.Core.Entities;
using Callsign.Core.Events;
using Callsign.Core.Interaction;
using Callsign.Core.PedStops;
using Callsign.Core.Ports;
using Callsign.Core.Suspects;
using Callsign.Core.Ui;
using Callsign.Core.World;
using Xunit;

namespace Callsign.Core.Tests;

sealed class FakePedestrians : IPedestrians
{
    public FakePed? Ped;

    public bool AnyoneToStop(float maxDistance) => Ped != null;

    public bool TryTakePedNearby(EntityScope scope, float maxDistance, out IPed? ped)
    {
        ped = Ped == null ? null : scope.Track(Ped);
        return Ped != null;
    }
}

public class PedStopServiceTests
{
    static readonly Position Street = new(100, 0, 0);

    readonly FakeClock _clock = new();
    readonly FakeWorld _world = new() { PlayerPosition = Street with { X = Street.X - 2 } };
    readonly FakeNotifier _notifier = new();
    readonly FakeMenuView _menu = new();
    readonly FakePedestrians _pedestrians = new();
    readonly EntityScopes _scopes = new(new NullLog());
    readonly EventBus _bus = new(ex => throw ex);
    readonly DutyService _duty;
    readonly PedStopSettings _settings = new();
    readonly List<object> _events = [];
    readonly PedStopService _stops;

    public PedStopServiceTests()
    {
        _duty = new DutyService(_bus);
        _bus.Subscribe<PedStopStarted>(_events.Add);
        _bus.Subscribe<PedStopEnded>(_events.Add);

        // 0.5 for every roll: no hidden traits, a clean record, and only sure reactions happen.
        var random = new FixedRandom(0.5);
        var content = new SuspectContent();
        var interaction = new InteractionServices(_world, _notifier, _menu, _clock, random, new Dialogue(content, random), new InteractionSettings());
        var custody = new CustodyServices(_world, new FakeBlips(), _notifier, _clock, [new PoliceStation("hq", "HQ", new Position(0, 5000, 0))], new CustodySettings());
        var noTraits = new TraitChances { Warrant = 0, FalseNameOtherwise = 0, NoId = 0, Intoxicated = 0, HadADrink = 0,
            OnDrugs = 0, CarryingDrugs = 0, ConcealedWeapon = 0, NervousOtherwise = 0 };
        var services = new CalloutSceneServices(_scopes, new FakePeds(), new FakeVehicles(), new FakeBlips(), _world, _notifier, _clock,
            random, new NullLog(), new CalloutSettings(), new SuspectProfileGenerator(content, random, noTraits), interaction, custody);
        _stops = new PedStopService(_duty, _bus, _pedestrians, services, _settings);
    }

    void Tick(bool stop = false, bool interact = false, MenuInput menu = default) =>
        _stops.Tick(new CalloutInput(stop, interact, menu));

    /// <summary>On duty, on foot, someone standing right in front.</summary>
    FakePed Patrol()
    {
        _duty.SetOnDuty(true);
        _pedestrians.Ped = new FakePed(Street);
        return _pedestrians.Ped;
    }

    void Choose(string id)
    {
        for (int i = 0; i < 20 && _menu.Last!.Selected!.Id != id; i++) Tick(menu: new MenuInput(Down: true));
        Assert.Equal(id, _menu.Last!.Selected!.Id);
        Tick(menu: new MenuInput(Select: true));
    }

    PedStopEnded Ended() => Assert.IsType<PedStopEnded>(_events[^1]);

    [Fact]
    public void HintShowsNextToSomeoneAndNothingStartsWithoutTheKey()
    {
        Patrol();
        Tick();
        Assert.Equal(PedStopService.StopHelp, _notifier.HelpShown);
        Assert.False(_stops.Active);
    }

    [Fact]
    public void NeedsDutyAndOnFoot()
    {
        Patrol();
        _duty.SetOnDuty(false);
        Tick(stop: true);
        Assert.False(_stops.Active);

        _duty.SetOnDuty(true);
        _world.PlayerOnFoot = false;
        Tick(stop: true);
        Assert.False(_stops.Active);
        Assert.Null(_notifier.HelpShown);
    }

    [Fact]
    public void NobodyThereLeavesNoTrace()
    {
        _duty.SetOnDuty(true);
        Tick(stop: true);
        Assert.False(_stops.Active);
        Assert.False(_duty.Busy);
        Assert.Equal(0, _scopes.OpenCount);
        Assert.Empty(_events);
    }

    [Fact]
    public void ACalmPedestrianStopsTalksAndCanBeLetGo()
    {
        FakePed ped = Patrol();
        Tick(stop: true);

        Assert.True(_stops.Active);
        Assert.Equal(PedStopService.Activity, _duty.Activity);
        Assert.IsType<PedStopStarted>(_events[0]);
        Assert.Equal("face", ped.Order);

        Tick(interact: true);   // already stopped by the hold: a tap opens the menu
        Assert.NotNull(_menu.Last);
        Choose(SuspectInteraction.AskId);
        Assert.Contains(_notifier.Messages, m => m.Contains("ID card"));
        Assert.DoesNotContain(_menu.Last!.Items, i => i.Id == SuspectInteraction.Cite || i.Id == SuspectInteraction.RunPlate);

        Choose(SuspectInteraction.LetGo);
        Assert.Equal(PedStopResult.Released, Ended().Result);
        Assert.False(_stops.Active);
        Assert.False(_duty.Busy);
        Assert.Equal(0, _scopes.OpenCount);
        Assert.Equal(ReleaseMode.Dismiss, ped.Released);
    }

    [Fact]
    public void SomeoneWithSomethingToLoseRuns()
    {
        _settings.Reactions.FleeAnyway = 1;
        FakePed ped = Patrol();
        Tick(stop: true);

        Assert.Equal("flee", ped.Order);
        Assert.True(_stops.Active);
        Assert.Contains(_notifier.Messages, m => m.Contains("fleeing"));
    }

    [Fact]
    public void ArrestTakesThemIntoCustody()
    {
        FakePed ped = Patrol();
        Tick(stop: true);
        Tick(interact: true);
        Choose(SuspectInteraction.Arrest);
        Tick();
        Tick();
        Assert.True(ped.Cuffed);
        Assert.True(_stops.Active);   // until booked
    }

    [Fact]
    public void WalkingAwayEndsTheStop()
    {
        Patrol();
        Tick(stop: true);
        _world.PlayerPosition = Street with { X = Street.X + _settings.AbandonDistance + 10 };
        Tick();

        Assert.Equal(PedStopResult.Cancelled, Ended().Result);
        Assert.False(_duty.Busy);
        Assert.Equal(0, _scopes.OpenCount);
    }

    [Fact]
    public void CancelAndReloadFreeTheOfficer()
    {
        Patrol();
        Tick(stop: true);
        Assert.True(_stops.Cancel());
        Assert.Equal(PedStopResult.Cancelled, Ended().Result);
        Assert.False(_stops.Cancel());

        FakePed ped = Patrol();
        Tick(stop: true);
        _stops.Stop();
        Assert.False(_duty.Busy);
        Assert.Equal(0, _scopes.OpenCount);
        Assert.Equal(ReleaseMode.Delete, ped.Released);
    }

    [Fact]
    public void NoStopWhileBusyWithSomethingElse()
    {
        Patrol();
        Assert.True(_duty.TryBegin(CalloutService.Activity));
        Tick(stop: true);
        Assert.False(_stops.Active);
        Assert.Null(_notifier.HelpShown);
    }
}
