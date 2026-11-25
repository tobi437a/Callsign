using Callsign.Core.Callouts;
using Callsign.Core.Duty;
using Callsign.Core.Entities;
using Callsign.Core.Events;
using Callsign.Core.Ports;
using Callsign.Core.World;
using Xunit;

namespace Callsign.Core.Tests;

sealed class FakeClock : IClock
{
    public long NowMs { get; set; }
    public TimeSpan TimeOfDay => TimeSpan.FromHours(12);
}

sealed class FakeWorld : IWorld
{
    public Position PlayerPosition { get; set; }
    public bool PlayerOnFoot { get; set; } = true;
    public bool PlayerDead { get; set; }
    public bool HasStreets { get; set; }

    public bool TryFindStreetNear(Position near, out Position position, out float heading)
    {
        position = near; heading = 0;
        return HasStreets;
    }

    public bool TryFindPedSpotNear(Position near, out Position position) { position = near; return true; }

    public string DescribeLocation(Position position) => "Some St";
}

sealed class FakePed(Position at) : IPed
{
    public bool Exists { get; set; } = true;
    public bool IsDead { get; set; }
    public Position Position { get; set; } = at;
    public string? Order { get; private set; }
    public string? Weapon { get; private set; }
    public ReleaseMode? Released { get; private set; }

    public void GiveWeapon(string weapon) => Weapon = weapon;
    public void Wander() => Order = "wander";
    public void FacePlayer() => Order = "face";
    public void HandsUp() => Order = "handsup";
    public void FleeFromPlayer() => Order = "flee";
    public void FightPlayer() => Order = "fight";
    public void Release(ReleaseMode mode) => Released = mode;
}

sealed class FakePeds : IPedFactory
{
    public int LoadTicks = 1;   // first call "streams the model"
    public FakePed? Last;

    public bool TrySpawn(EntityScope scope, string model, Position at, float heading, out IPed? ped)
    {
        ped = null;
        if (model == "bad_model") throw new ArgumentException("bad model");
        if (LoadTicks-- > 0) return false;
        ped = Last = scope.Track(new FakePed(at));
        return true;
    }
}

sealed class FakeBlips : IBlips
{
    public readonly List<FakeBlip> All = [];
    public IBlip At(EntityScope scope, Position position) => Add(scope);
    public IBlip On(EntityScope scope, IPed ped) => Add(scope);
    IBlip Add(EntityScope scope) { var b = new FakeBlip(); All.Add(b); return scope.Track(b); }
}

sealed class FakeBlip : IBlip
{
    public bool Exists { get; private set; } = true;
    public string Name { set { } }
    public bool ShowRoute { set { } }
    public MarkerColor Color { get; set; }
    MarkerColor IBlip.Color { set => Color = value; }
    public void Release(ReleaseMode mode) => Exists = false;
}

sealed class FakeNotifier : INotifier
{
    public readonly List<string> Messages = [];
    public string? HelpShown;
    public void Notify(string text) => Messages.Add(text);
    public void Subtitle(string text, int durationMs) => Messages.Add(text);
    public void Help(string text) => HelpShown = text;
}

sealed class NullLog : ILog
{
    public int Errors;
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? exception = null) => Errors++;
}

public class CalloutServiceTests
{
    static readonly Position Scene = new(1000, 0, 0);

    readonly FakeClock _clock = new();
    readonly FakeWorld _world = new();
    readonly FakePeds _peds = new();
    readonly FakeBlips _blips = new();
    readonly FakeNotifier _notifier = new();
    readonly NullLog _log = new();
    readonly EntityScopes _scopes;
    readonly EventBus _bus = new(ex => throw ex);
    readonly DutyService _duty;
    readonly CalloutParts _parts = new();
    readonly List<object> _events = [];
    readonly CalloutService _service;
    readonly CalloutSettings _settings = new();

    public CalloutServiceTests()
    {
        _scopes = new EntityScopes(_log);
        _duty = new DutyService(_bus);
        _bus.Subscribe<CalloutOffered>(_events.Add);
        _bus.Subscribe<CalloutAccepted>(_events.Add);
        _bus.Subscribe<CalloutEnded>(_events.Add);

        _parts.Situations.Add(new SituationPart("sit", "Test call", "{suspect} at {location}", [], [], [], []));
        _parts.Locations.Add(new LocationPart("loc", "Test St", Scene, []));
        _parts.Suspects.Add(new SuspectPart("sus", "man", ["model"], "WEAPON_KNIFE", []));

        var random = new SeededRandom(7);
        var services = new CalloutSceneServices(_scopes, _peds, _blips, _world, _notifier, _log, _settings);
        _service = new CalloutService(new IncidentGenerator(_parts, random), _duty, _bus, _clock, random, services);
    }

    void UseOutcome(SuspectBehaviour behaviour) => _parts.Outcomes.Add(new OutcomePart("out", behaviour, []));

    void Tick(bool interact = false) => _service.Tick(interact);

    void Advance(long ms) { _clock.NowMs += ms; Tick(); }

    /// <summary>On duty, wait for the offer, accept, drive to the scene, spawn, arrive.</summary>
    FakePed StartAndArrive()
    {
        _duty.SetOnDuty(true);
        Tick();
        Advance(_settings.FirstOfferMaxMs);
        Assert.Equal(CalloutService.Phase.Offered, _service.Current);
        Assert.True(_service.Accept());

        _world.PlayerPosition = new Position(Scene.X - 100, 0, 0);   // within spawn distance
        Tick();   // -> spawning
        Tick();   // model streaming
        Tick();   // spawned
        FakePed suspect = _peds.Last!;
        Assert.Equal("wander", suspect.Order);

        _world.PlayerPosition = new Position(Scene.X - 10, 0, 0);    // on scene
        Tick();
        return suspect;
    }

    CalloutEnded Ended() => Assert.IsType<CalloutEnded>(_events[^1]);

    [Fact]
    public void NoOffersWhileOffDuty()
    {
        UseOutcome(SuspectBehaviour.Comply);
        Advance(10 * 60_000);
        Assert.Empty(_events);
    }

    [Fact]
    public void RequestNowOffersAtOnceButOnlyOnDutyAndWhenFree()
    {
        UseOutcome(SuspectBehaviour.Comply);
        Assert.False(_service.RequestNow());   // off duty

        _duty.SetOnDuty(true);
        Tick();
        Assert.True(_service.RequestNow());
        Assert.Equal(CalloutService.Phase.Offered, _service.Current);
        Assert.False(_service.RequestNow());   // already offered
    }

    [Fact]
    public void RequestNowSaysSoWhenNothingFits()
    {
        UseOutcome(SuspectBehaviour.Comply);
        _world.PlayerPosition = new Position(50_000, 0, 0);
        _duty.SetOnDuty(true);
        Tick();

        Assert.False(_service.RequestNow());
        Assert.Contains(_notifier.Messages, m => m.Contains("no calls"));
    }

    [Fact]
    public void UnansweredOfferExpires()
    {
        UseOutcome(SuspectBehaviour.Comply);
        _duty.SetOnDuty(true);
        Tick();
        Advance(_settings.FirstOfferMaxMs);
        Assert.IsType<CalloutOffered>(Assert.Single(_events));

        Advance(_settings.OfferTimeoutMs);

        Assert.Equal(CalloutEndReason.OfferExpired, Ended().Reason);
        Assert.Equal(CalloutService.Phase.Waiting, _service.Current);
    }

    [Fact]
    public void FallsBackToANearbyStreetWhenNoLocationIsInRange()
    {
        UseOutcome(SuspectBehaviour.Comply);
        _world.PlayerPosition = new Position(50_000, 0, 0);
        _world.HasStreets = true;
        _duty.SetOnDuty(true);
        Tick();
        Advance(_settings.FirstOfferMaxMs);

        var offered = Assert.IsType<CalloutOffered>(Assert.Single(_events));
        Assert.Equal("street", offered.Incident.Location.Id);
        Assert.Equal("Some St", offered.Incident.Location.Name);
    }

    [Fact]
    public void CompliantSuspectIsResolvedByInteracting()
    {
        UseOutcome(SuspectBehaviour.Comply);
        FakePed suspect = StartAndArrive();
        Assert.Equal("face", suspect.Order);
        Assert.Equal("WEAPON_KNIFE", suspect.Weapon);

        _world.PlayerPosition = new Position(Scene.X - 2, 0, 0);
        Tick();
        Assert.Equal(CalloutScene.InteractHelp, _notifier.HelpShown);
        Tick(interact: true);

        Assert.Equal(CalloutEndReason.Resolved, Ended().Reason);
        Assert.Equal(ReleaseMode.Dismiss, suspect.Released);
        Assert.All(_blips.All, b => Assert.False(b.Exists));
        Assert.Equal(0, _scopes.OpenCount);
    }

    [Fact]
    public void FleeingSuspectGivesUpWhenCaughtOnFoot()
    {
        UseOutcome(SuspectBehaviour.Flee);
        FakePed suspect = StartAndArrive();
        Assert.Equal("flee", suspect.Order);

        _world.PlayerOnFoot = false;
        _world.PlayerPosition = new Position(Scene.X - 1, 0, 0);
        Tick();
        Assert.Equal("flee", suspect.Order);   // not from inside a car

        _world.PlayerOnFoot = true;
        Tick();
        Assert.Equal("handsup", suspect.Order);
        Tick(interact: true);

        Assert.Equal(CalloutEndReason.Resolved, Ended().Reason);
    }

    [Fact]
    public void FleeingSuspectEscapesWhenFarEnough()
    {
        UseOutcome(SuspectBehaviour.Flee);
        FakePed suspect = StartAndArrive();

        suspect.Position = new Position(Scene.X + _settings.EscapeDistance + 10, 0, 0);
        Tick();

        Assert.Equal(CalloutEndReason.SuspectEscaped, Ended().Reason);
    }

    [Fact]
    public void FightingSuspectEndsWhenDead()
    {
        UseOutcome(SuspectBehaviour.Fight);
        FakePed suspect = StartAndArrive();
        Assert.Equal("fight", suspect.Order);

        suspect.IsDead = true;
        Tick();

        Assert.Equal(CalloutEndReason.SuspectDied, Ended().Reason);
    }

    [Fact]
    public void GoingOffDutyCancelsTheRunningCallout()
    {
        UseOutcome(SuspectBehaviour.Comply);
        StartAndArrive();

        _duty.SetOnDuty(false);
        Tick();

        Assert.Equal(CalloutEndReason.Cancelled, Ended().Reason);
        Assert.Equal(CalloutService.Phase.OffDuty, _service.Current);
        Assert.Equal(0, _scopes.OpenCount);
    }

    [Fact]
    public void ABadSuspectModelFailsTheCallNotTheMod()
    {
        _parts.Suspects.Add(new SuspectPart("bad", "ghost", ["bad_model"], null, [], Weight: 1000));
        _parts.Suspects.Add(new SuspectPart("bad2", "ghost", ["bad_model"], null, [], Weight: 1000));
        UseOutcome(SuspectBehaviour.Comply);
        _duty.SetOnDuty(true);
        Tick();
        Advance(_settings.FirstOfferMaxMs);
        Assume(((CalloutOffered)_events[0]).Incident.SuspectModel == "bad_model");
        _service.Accept();
        _world.PlayerPosition = Scene;
        Tick();
        Tick();

        Assert.Equal(CalloutEndReason.Failed, Ended().Reason);
        Assert.Equal(1, _log.Errors);
    }

    [Fact]
    public void StopDeletesTheSceneWithoutEvents()
    {
        UseOutcome(SuspectBehaviour.Comply);
        FakePed suspect = StartAndArrive();
        int events = _events.Count;

        _service.Stop();

        Assert.Equal(ReleaseMode.Delete, suspect.Released);
        Assert.Equal(events, _events.Count);
        Assert.Equal(0, _scopes.OpenCount);
    }

    static void Assume(bool condition) => Assert.True(condition, "test setup: seed did not pick the expected part");
}

public class BuiltInContentTests
{
    sealed class ListLog : ILog
    {
        public readonly List<string> Problems = [];
        public void Info(string message) { }
        public void Warn(string message) => Problems.Add(message);
        public void Error(string message, Exception? exception = null) => Problems.Add(message);
    }

    [Fact]
    public void ShippedDataLoadsWithoutProblemsAndEverySituationIsPlayable()
    {
        var parts = new CalloutParts();
        var log = new ListLog();

        int added = CalloutContentLoader.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "data", "Callouts"), parts, log);

        Assert.Empty(log.Problems);
        Assert.True(added > 0);
        foreach (var s in parts.Situations.All)
        {
            Assert.Contains(parts.Locations.All, s.Fits);
            Assert.Contains(parts.Suspects.All, s.Fits);
            Assert.Contains(parts.Outcomes.All, s.Fits);
        }
    }
}
