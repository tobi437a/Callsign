using Callsign.Core.Arrest;
using Callsign.Core.Callouts;
using Callsign.Core.Duty;
using Callsign.Core.Entities;
using Callsign.Core.Events;
using Callsign.Core.Interaction;
using Callsign.Core.Ports;
using Callsign.Core.Suspects;
using Callsign.Core.TrafficStops;
using Callsign.Core.Ui;
using Callsign.Core.World;
using Xunit;

namespace Callsign.Core.Tests;

sealed class FakeTraffic : ITraffic
{
    public FakeVehicle? Vehicle;
    public readonly List<FakePed> Occupants = [];

    public bool TryTakeVehicleAhead(EntityScope scope, float maxDistance, out IVehicle? vehicle, out IReadOnlyList<IPed> occupants)
    {
        vehicle = null;
        occupants = [];
        if (Vehicle == null) return false;
        vehicle = scope.Track(Vehicle);
        for (int seat = 0; seat < Occupants.Count; seat++)
        {
            Occupants[seat].WarpInto(Vehicle, seat);
            scope.Track(Occupants[seat]);
        }
        occupants = Occupants.Cast<IPed>().ToList();
        return true;
    }
}

public class TrafficStopServiceTests
{
    static readonly Position Road = new(500, 0, 0);

    readonly FakeClock _clock = new();
    readonly FakeWorld _world = new();
    readonly FakeNotifier _notifier = new();
    readonly FakeMenuView _menu = new();
    readonly FakeTraffic _traffic = new();
    readonly NullLog _log = new();
    readonly EntityScopes _scopes;
    readonly EventBus _bus = new(ex => throw ex);
    readonly DutyService _duty;
    readonly CalloutSettings _calloutSettings = new();
    readonly InteractionSettings _interactionSettings = new();
    readonly TrafficStopSettings _settings = new();
    readonly CalloutSceneServices _services;
    readonly List<object> _events = [];
    TrafficStopService _stops;

    public TrafficStopServiceTests()
    {
        _scopes = new EntityScopes(_log);
        _duty = new DutyService(_bus);
        _bus.Subscribe<TrafficStopStarted>(_events.Add);
        _bus.Subscribe<TrafficStopEnded>(_events.Add);

        // 0.5 for every roll: no hidden traits, a clean record, and only sure reactions happen.
        var random = new FixedRandom(0.5);
        var content = new SuspectContent();
        var interaction = new InteractionServices(_world, _notifier, _menu, _clock, random, new Dialogue(content, random), _interactionSettings);
        var custody = new CustodyServices(_world, new FakeBlips(), _notifier, _clock, [new PoliceStation("hq", "HQ", new Position(0, 5000, 0))], new CustodySettings());
        var noTraits = new TraitChances { Warrant = 0, FalseNameOtherwise = 0, NoId = 0, Intoxicated = 0, HadADrink = 0,
            OnDrugs = 0, CarryingDrugs = 0, ConcealedWeapon = 0, NervousOtherwise = 0 };
        _services = new CalloutSceneServices(_scopes, new FakePeds(), new FakeVehicles(), new FakeBlips(), _world, _notifier, _clock,
            random, _log, _calloutSettings, new SuspectProfileGenerator(content, random, noTraits), interaction, custody);
        _stops = new TrafficStopService(_duty, _bus, _traffic, _services, _settings);
    }

    void Tick(bool initiate = false, bool stop = false, bool interact = false, MenuInput menu = default) =>
        _stops.Tick(new TrafficStopInput(initiate, new CalloutInput(stop, interact, menu)));

    void Advance(long ms) { _clock.NowMs += ms; Tick(); }

    /// <summary>On duty, in the car with the lights on, a car ahead with <paramref name="occupants"/> people.</summary>
    FakeVehicle Patrol(int occupants = 1)
    {
        _duty.SetOnDuty(true);
        _world.PlayerOnFoot = false;
        _world.PlayerInVehicle = true;
        _world.PlayerLightsOn = true;
        _world.PlayerPosition = Road with { X = Road.X - 15 };
        _traffic.Vehicle = new FakeVehicle(Road);
        for (int i = 0; i < occupants; i++) _traffic.Occupants.Add(new FakePed(Road));
        return _traffic.Vehicle;
    }

    /// <summary>Pulls the car over and waits until it has properly stopped.</summary>
    FakePed PullOverAndStop(FakeVehicle car)
    {
        Tick(initiate: true);
        FakePed driver = _traffic.Occupants[0];
        Assert.Equal("pullover", driver.Order);
        car.Speed = 0;
        driver.AtPullOverSpot = true;
        Tick();
        Advance(_calloutSettings.PulledOverMs);
        Assert.Equal("waiting", driver.Order);
        return driver;
    }

    void WalkUpAndOpenMenu(FakePed driver)
    {
        _world.PlayerOnFoot = true;
        _world.PlayerInVehicle = false;
        _world.PlayerPosition = driver.Position with { X = driver.Position.X - 2 };
        Tick(interact: true);   // a pulled-over driver is already stopped: no need to hold the key
        Assert.NotNull(_menu.Last);
    }

    void Choose(string id)
    {
        for (int i = 0; i < 20 && _menu.Last!.Selected!.Id != id; i++) Tick(menu: new MenuInput(Down: true));
        Assert.Equal(id, _menu.Last!.Selected!.Id);
        Tick(menu: new MenuInput(Select: true));
    }

    TrafficStopEnded Ended() => Assert.IsType<TrafficStopEnded>(_events[^1]);

    [Fact]
    public void HintShowsWithLightsOnAndNothingStartsWithoutTheKey()
    {
        Patrol();
        Tick();
        Assert.Equal(TrafficStopService.LightsHelp, _notifier.HelpShown);
        Assert.False(_stops.Active);
        Assert.False(_duty.Busy);
    }

    [Fact]
    public void NeedsLightsAndDuty()
    {
        Patrol();
        _world.PlayerLightsOn = false;
        Tick(initiate: true);
        Assert.False(_stops.Active);

        _world.PlayerLightsOn = true;
        _duty.SetOnDuty(false);
        Tick(initiate: true);
        Assert.False(_stops.Active);
    }

    [Fact]
    public void NothingAheadLeavesNoTrace()
    {
        Patrol();
        _traffic.Vehicle = null;
        Tick(initiate: true);

        Assert.False(_stops.Active);
        Assert.False(_duty.Busy);
        Assert.Equal(0, _scopes.OpenCount);
        Assert.Empty(_events);
    }

    [Fact]
    public void CalmDriverPullsOverAndGetsACitationAfterAPlateCheck()
    {
        FakeVehicle car = Patrol();
        FakePed driver = PullOverAndStop(car);
        Assert.True(_stops.Active);
        Assert.Equal(TrafficStopService.Activity, _duty.Activity);
        Assert.Equal(new TrafficStopStarted("Futo", "12ABC345", 1), _events[0]);

        WalkUpAndOpenMenu(driver);
        Choose(SuspectInteraction.RunPlate);
        Advance(_interactionSettings.PlateCheckMs);
        Assert.StartsWith("Plate: ~g~valid", _menu.Last!.Items.Single(i => i.Id == SuspectInteraction.RunPlate).Label);
        Assert.Contains(_notifier.Messages, m => m.Contains("12ABC345 comes back to a Futo"));

        Choose(SuspectInteraction.Cite);

        Assert.Equal(TrafficStopResult.Cited, Ended().Result);
        Assert.False(_stops.Active);
        Assert.False(_duty.Busy);
        Assert.Equal(0, _scopes.OpenCount);
        Assert.Equal(ReleaseMode.Dismiss, car.Released);
    }

    [Fact]
    public void ACarThatCantReachTheKerbStillCountsAsStoppedAfterAWhile()
    {
        FakeVehicle car = Patrol();
        Tick(initiate: true);
        FakePed driver = _traffic.Occupants[0];
        car.Speed = 0;
        Tick();
        Advance(_calloutSettings.PulledOverMs);
        Assert.Equal("pullover", driver.Order);
        Advance(_calloutSettings.PulledOverStuckMs);
        Assert.Equal("waiting", driver.Order);
    }

    [Fact]
    public void ThePulledOverDriverWaitsWhileYouGoBackToYourCar()
    {
        FakePed driver = PullOverAndStop(Patrol());
        _world.PlayerPosition = driver.Position with { X = driver.Position.X - 40 };
        Advance(10_000);
        Assert.Equal("waiting", driver.Order);
        Assert.True(_stops.Active);
    }

    [Fact]
    public void WarningLetsThemGo()
    {
        FakePed driver = PullOverAndStop(Patrol());
        WalkUpAndOpenMenu(driver);
        Choose(SuspectInteraction.LetGo);
        Assert.Equal(TrafficStopResult.Warned, Ended().Result);
    }

    [Fact]
    public void CalmPassengersLeaveWithTheDriver()
    {
        FakePed driver = PullOverAndStop(Patrol(occupants: 3));
        WalkUpAndOpenMenu(driver);
        Choose(SuspectInteraction.Cite);

        Assert.Equal(TrafficStopResult.Cited, Ended().Result);
        Assert.Equal(0, _scopes.OpenCount);
    }

    [Fact]
    public void ADriverWithSomethingToLoseRuns()
    {
        _settings.Reactions.FleeAnyway = 1;
        Patrol();
        Tick(initiate: true);

        Assert.Equal("driveaway", _traffic.Occupants[0].Order);   // a pursuit
        Assert.True(_stops.Active);
        Assert.Contains(_notifier.Messages, m => m.Contains("fleeing"));
    }

    [Fact]
    public void DrivingAwayEndsTheStop()
    {
        PullOverAndStop(Patrol());
        _world.PlayerPosition = Road with { X = Road.X + _settings.AbandonDistance + 10 };
        Tick();

        Assert.Equal(TrafficStopResult.Cancelled, Ended().Result);
        Assert.False(_duty.Busy);
        Assert.Equal(0, _scopes.OpenCount);
    }

    [Fact]
    public void CancelAndReloadFreeTheOfficer()
    {
        PullOverAndStop(Patrol());
        Assert.True(_stops.Cancel());
        Assert.Equal(TrafficStopResult.Cancelled, Ended().Result);
        Assert.False(_stops.Cancel());

        _traffic.Occupants.Clear();
        PullOverAndStop(Patrol());
        _stops.Stop();
        Assert.False(_duty.Busy);
        Assert.Equal(0, _scopes.OpenCount);
        Assert.Equal(ReleaseMode.Delete, _traffic.Vehicle!.Released);
    }

    [Fact]
    public void NoStopWhileOnACallout()
    {
        Patrol();
        Assert.True(_duty.TryBegin(CalloutService.Activity));
        Tick(initiate: true);
        Assert.False(_stops.Active);
        Assert.Null(_notifier.HelpShown);   // no hint either
    }
}

public class OfficerActivityTests
{
    [Fact]
    public void OneActivityAtATimeAndOnlyOnDuty()
    {
        var duty = new DutyService(new EventBus(ex => throw ex));
        Assert.False(duty.TryBegin("callout"));

        duty.SetOnDuty(true);
        Assert.True(duty.TryBegin("callout"));
        Assert.True(duty.TryBegin("callout"));    // already ours
        Assert.False(duty.TryBegin("traffic stop"));

        duty.End("traffic stop");                  // not what we're busy with
        Assert.Equal("callout", duty.Activity);
        duty.End("callout");
        Assert.False(duty.Busy);

        duty.TryBegin("traffic stop");
        duty.SetOnDuty(false);
        Assert.False(duty.Busy);
    }
}

public class StopReactionTests
{
    static SuspectProfile Profile(SuspectTraits traits, double bac = 0, string? weapon = null) =>
        new(new Identity("A", "B", 30), new Identity("A", "B", 30), traits, bac, null, weapon, weapon, []);

    readonly StopReactionChances _c = new();

    [Fact]
    public void CleanDriversRarelyRun()
    {
        Assert.Equal(_c.FleeAnyway, StopReaction.FleeChance(Profile(SuspectTraits.None), false, _c), 6);
    }

    [Fact]
    public void MoreToLoseMeansLikelierToRun()
    {
        double wanted = StopReaction.FleeChance(Profile(SuspectTraits.Warrant), false, _c);
        double wantedNervous = StopReaction.FleeChance(Profile(SuspectTraits.Warrant | SuspectTraits.Nervous), false, _c);
        double wantedNervousStolen = StopReaction.FleeChance(Profile(SuspectTraits.Warrant | SuspectTraits.Nervous), true, _c);
        double drunk = StopReaction.FleeChance(Profile(SuspectTraits.Intoxicated, bac: 0.12), false, _c);

        Assert.True(wanted > _c.FleeAnyway);
        Assert.True(drunk > _c.FleeAnyway);
        Assert.True(wantedNervous > wanted);
        Assert.True(wantedNervousStolen > wantedNervous);
        Assert.True(wantedNervousStolen <= _c.FleeMax);
    }

    [Fact]
    public void OnlyTheArmedAndWantedFight()
    {
        var always = new FixedRandom(0);
        Assert.Equal(SuspectBehaviour.Fight, StopReaction.Pick(Profile(SuspectTraits.Warrant | SuspectTraits.ConcealedWeapon, weapon: "WEAPON_KNIFE"), false, always, _c));
        Assert.Equal(SuspectBehaviour.Flee, StopReaction.Pick(Profile(SuspectTraits.Warrant), false, always, _c));
        Assert.Equal(SuspectBehaviour.Comply, StopReaction.Pick(Profile(SuspectTraits.Warrant), false, new FixedRandom(0.99), _c));
    }
}

public class VehicleRecordTests
{
    static SuspectProfile Driver(SuspectTraits traits = SuspectTraits.None) =>
        new(new Identity("Dan", "Driver", 40), new Identity("Dan", "Driver", 40), traits, 0, null, null, null, []);

    [Fact]
    public void StolenTagMeansStolenAndSomeoneElsesCar()
    {
        var random = new FixedRandom(0.99);
        var generator = new VehicleRecordGenerator(new SuspectProfileGenerator(new SuspectContent(), random), random);

        VehicleRecord record = generator.Generate("ABC", "Futo", Driver(), [VehicleRecordGenerator.StolenTag]);

        Assert.True(record.Stolen);
        Assert.True(record.Flagged);
        Assert.NotEqual("Dan Driver", record.Owner.FullName);
    }

    [Fact]
    public void UsuallyTheDriversOwnCleanCar()
    {
        var random = new FixedRandom(0.99);
        var generator = new VehicleRecordGenerator(new SuspectProfileGenerator(new SuspectContent(), random), random);

        VehicleRecord record = generator.Generate("ABC", "Futo", Driver(SuspectTraits.Warrant), []);

        Assert.False(record.Flagged);
        Assert.Equal("Dan Driver", record.Owner.FullName);
    }
}
