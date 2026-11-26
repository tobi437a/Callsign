using Callsign.Core.Arrest;
using Callsign.Core.Callouts;
using Callsign.Core.Duty;
using Callsign.Core.Entities;
using Callsign.Core.Events;
using Callsign.Core.Interaction;
using Callsign.Core.Ports;
using Callsign.Core.Suspects;
using Callsign.Core.Ui;
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
    public bool PlayerInVehicle { get; set; }
    public bool PlayerDead { get; set; }
    public bool PlayerLightsOn { get; set; }
    public bool HasStreets { get; set; }
    public Position? PlayerVehicle { get; set; }

    public bool TryGetPlayerVehiclePosition(out Position position)
    {
        position = PlayerVehicle ?? default;
        return PlayerVehicle != null;
    }

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

    public bool IsMale { get; set; } = true;
    public bool WeaponInHand { get; private set; }
    public void GiveWeapon(string weapon, bool equip) { Weapon = weapon; WeaponInHand = equip; }
    public void RemoveWeapons() { Weapon = null; WeaponInHand = false; }
    public void Wander() => OnFoot("wander");
    public void FacePlayer() => OnFoot("face");
    public void HandsUp() => OnFoot("handsup");
    public void FleeFromPlayer() => OnFoot("flee");
    public void FightPlayer() => OnFoot("fight");
    void OnFoot(string order) { Order = order; InVehicle = false; }   // on-foot orders get them out first

    public bool InVehicle { get; set; }
    public bool PlayerAimingAt { get; set; }
    public int Seat { get; private set; } = -1;
    public void WarpInto(IVehicle vehicle, int seat) { InVehicle = true; Seat = seat; }
    public void DriveAround() { if (Seat == 0 && InVehicle) Order = "drive"; }
    public void DriveAway() { if (Seat == 0 && InVehicle) Order = "driveaway"; }
    public void PullOver() { if (Seat == 0 && InVehicle) Order = "pullover"; }
    public bool AtPullOverSpot { get; set; }
    public void WaitInVehicle() { if (Seat == 0 && InVehicle) Order = "waiting"; }
    public void ShootFromVehicle() { if (InVehicle) Order = "driveby"; }

    public bool IsSubdued { get; set; }
    public int CuffLoadTicks = 1;   // first call "streams the animation"
    public bool Cuffed { get; private set; }
    public bool HasFreeSeat = true;
    public bool InPlayerVehicle { get; set; }

    public bool TryCuff()
    {
        if (CuffLoadTicks-- > 0) return false;
        Cuffed = true;
        Order = "cuffed";
        return true;
    }

    public void FollowPlayer() => OnFoot("follow");

    public bool EnterPlayerVehicle(bool warp)
    {
        if (!HasFreeSeat) return false;
        Order = warp ? "warp" : "enter";
        if (warp) InPlayerVehicle = true;
        return true;
    }
    public void Release(ReleaseMode mode) => Released = mode;
}

sealed class FakePeds : IPedFactory
{
    public int LoadTicks = 1;   // first call "streams the model"
    public FakePed? Last;
    public readonly List<FakePed> All = [];

    public bool TrySpawn(EntityScope scope, string model, Position at, float heading, out IPed? ped)
    {
        ped = null;
        if (model == "bad_model") throw new ArgumentException("bad model");
        if (LoadTicks-- > 0) return false;
        ped = Last = scope.Track(new FakePed(at));
        All.Add(Last);
        return true;
    }
}

sealed class FakeVehicle(Position at) : IVehicle
{
    public bool Exists { get; set; } = true;
    public Position Position { get; set; } = at;
    public float Speed { get; set; } = 15;
    public string Plate { get; set; } = "12ABC345";
    public string Model { get; set; } = "Futo";
    public bool IsWrecked { get; set; }
    public int BurstTyres { get; set; }
    public float EngineHealth { get; set; } = 1;
    public ReleaseMode? Released { get; private set; }
    public void Release(ReleaseMode mode) => Released = mode;
}

sealed class FakeVehicles : IVehicleFactory
{
    public int LoadTicks = 1;
    public FakeVehicle? Last;

    public bool TrySpawn(EntityScope scope, string model, Position at, float heading, out IVehicle? vehicle)
    {
        vehicle = null;
        if (LoadTicks-- > 0) return false;
        vehicle = Last = scope.Track(new FakeVehicle(at));
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

sealed class FakeMenuView : IMenuView
{
    public Menu? Last;
    public void Draw(Menu menu) => Last = menu;
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
    static readonly PoliceStation Station = new("hq", "HQ", new Position(0, 5000, 0));

    readonly FakeClock _clock = new();
    readonly FakeWorld _world = new();
    readonly FakePeds _peds = new();
    readonly FakeVehicles _vehicles = new();
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
    readonly FakeMenuView _menu = new();
    IDisposable _footCall;

    public CalloutServiceTests()
    {
        _scopes = new EntityScopes(_log);
        _duty = new DutyService(_bus);
        _bus.Subscribe<CalloutOffered>(_events.Add);
        _bus.Subscribe<CalloutAccepted>(_events.Add);
        _bus.Subscribe<CalloutEnded>(_events.Add);

        _footCall = _parts.Situations.Add(new SituationPart("sit", "Test call", "{suspect} at {location}", [], [], [], []));
        _parts.Locations.Add(new LocationPart("loc", "Test St", Scene, []));
        _parts.Suspects.Add(new SuspectPart("sus", "man", ["model"], "WEAPON_KNIFE", []));

        var random = new SeededRandom(7);
        var custody = new CustodyServices(_world, _blips, _notifier, _clock, [Station], new CustodySettings());
        // No hidden traits and no surprises: these tests are about the scene's flow.
        var noTraits = new TraitChances { Warrant = 0, FalseNameOtherwise = 0, NoId = 0, Intoxicated = 0, HadADrink = 0,
            OnDrugs = 0, CarryingDrugs = 0, ConcealedWeapon = 0, NervousOtherwise = 0 };
        var content = new SuspectContent();
        var interaction = new InteractionServices(_world, _notifier, _menu, _clock, random, new Dialogue(content, random),
            new InteractionSettings());
        var services = new CalloutSceneServices(_scopes, _peds, _vehicles, _blips, _world, _notifier, _clock, random, _log, _settings,
            new SuspectProfileGenerator(content, random, noTraits), interaction, custody);
        _service = new CalloutService(new IncidentGenerator(_parts, random), _duty, _bus, _clock, random, services);
    }

    void UseOutcome(SuspectBehaviour behaviour) => _parts.Outcomes.Add(new OutcomePart("out", behaviour, []));

    void Tick(bool stop = false, bool interact = false, MenuInput menu = default) =>
        _service.Tick(new CalloutInput(stop, interact, menu));

    /// <summary>Walks up to the suspect, stops them if needed and opens the menu.</summary>
    void OpenMenu(FakePed suspect)
    {
        _world.PlayerPosition = suspect.Position with { X = suspect.Position.X - 2 };
        Tick(stop: true);
        Tick(interact: true);
        Assert.NotNull(_menu.Last);
    }

    /// <summary>Moves to the menu item <paramref name="id"/> and picks it.</summary>
    void Choose(string id)
    {
        for (int i = 0; i < 20 && _menu.Last!.Selected!.Id != id; i++) Tick(menu: new MenuInput(Down: true));
        Assert.Equal(id, _menu.Last!.Selected!.Id);
        Tick(menu: new MenuInput(Select: true));
    }

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

    /// <summary>Replaces the test call with one of <paramref name="count"/> suspects, in a vehicle or on foot.</summary>
    void UseCall(SuspectBehaviour behaviour, int count = 1, bool vehicle = false)
    {
        _footCall.Dispose();
        _parts.Situations.Add(new SituationPart("multi", "Test call", "{suspect} at {location}", [], [], [], [])
            { InVehicle = vehicle, MinSuspects = count, MaxSuspects = count });
        if (vehicle) _parts.Suspects.Add(new SuspectPart("car", "Karin Sultan", ["model"], "WEAPON_PISTOL", []) { Vehicles = ["sultan"] });
        UseOutcome(behaviour);
    }

    /// <summary>Accepts the call and drives close enough for everyone to spawn (not yet close enough to be noticed).</summary>
    List<FakePed> StartAndSpawn(int count)
    {
        _duty.SetOnDuty(true);
        Tick();
        Assert.True(_service.RequestNow());
        Assert.True(_service.Accept());
        _world.PlayerPosition = new Position(Scene.X - 100, 0, 0);
        for (int i = 0; i < 10 && _peds.All.Count < count; i++) Tick();
        Assert.Equal(count, _peds.All.Count);
        return _peds.All;
    }

    void PlayerInCarAt(Position at, float behind)
    {
        _world.PlayerOnFoot = false;
        _world.PlayerInVehicle = true;
        _world.PlayerPosition = at with { X = at.X - behind };
    }

    void PlayerOnFootAt(Position at, float away)
    {
        _world.PlayerOnFoot = true;
        _world.PlayerInVehicle = false;
        _world.PlayerPosition = at with { X = at.X - away };
    }

    [Fact]
    public void DispatchWaitsWhileTheOfficerIsBusy()
    {
        UseOutcome(SuspectBehaviour.Comply);
        _duty.SetOnDuty(true);
        Assert.True(_duty.TryBegin("traffic stop"));
        Tick();
        Advance(_settings.FirstOfferMaxMs);
        Assert.Equal(CalloutService.Phase.Waiting, _service.Current);
        Assert.False(_service.RequestNow());

        _duty.End("traffic stop");
        Tick();
        Assert.Equal(CalloutService.Phase.Waiting, _service.Current);   // a breather first, as after a call
        Advance(_settings.NextOfferMinMs - 1);
        Assert.Equal(CalloutService.Phase.Waiting, _service.Current);
        Advance(_settings.NextOfferMaxMs);
        Assert.Equal(CalloutService.Phase.Offered, _service.Current);

        // An offer that came in before the officer got busy can't be taken until they're free.
        Assert.True(_duty.TryBegin("traffic stop"));
        Assert.False(_service.Accept());
        _duty.End("traffic stop");
        Assert.True(_service.Accept());
        Assert.Equal(CalloutService.Activity, _duty.Activity);

        Assert.True(_service.Cancel());
        Assert.False(_duty.Busy);
    }

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
    public void CompliantSuspectKeepsWalkingUntilStoppedThenCanBeLetGo()
    {
        UseOutcome(SuspectBehaviour.Comply);
        FakePed suspect = StartAndArrive();
        Assert.Equal("wander", suspect.Order);
        Assert.Equal("WEAPON_KNIFE", suspect.Weapon);

        _world.PlayerPosition = new Position(Scene.X - 2, 0, 0);
        Tick();
        Assert.Equal(SuspectInteraction.StopHelp, _notifier.HelpShown);
        Assert.Equal("wander", suspect.Order);

        OpenMenu(suspect);
        Assert.Equal("face", suspect.Order);
        Choose(SuspectInteraction.LetGo);

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

        Tick(interact: true);   // already stopped: straight to the menu
        Choose(SuspectInteraction.LetGo);
        Assert.Equal(CalloutEndReason.Resolved, Ended().Reason);
    }

    [Fact]
    public void ArrestedSuspectIsBookedAtTheStation()
    {
        UseOutcome(SuspectBehaviour.Comply);
        FakePed suspect = StartAndArrive();
        OpenMenu(suspect);

        Choose(SuspectInteraction.Arrest);
        Tick();                 // cuff animation streaming
        Tick();                 // cuffed
        Assert.True(suspect.Cuffed);
        Assert.Equal("follow", suspect.Order);

        _world.PlayerInVehicle = true;
        Tick();
        Assert.Equal("enter", suspect.Order);
        suspect.InPlayerVehicle = true;
        Tick();   // seated: transport starts, route to the station
        Assert.Contains(_blips.All, b => b.Exists && b.Color == MarkerColor.Blue);

        _world.PlayerPosition = Station.Position;
        Tick();

        CalloutEnded ended = Ended();
        Assert.Equal(CalloutEndReason.Arrested, ended.Reason);
        Assert.Equal(ReleaseMode.Delete, suspect.Released);
        Assert.Contains(_notifier.Messages, m => m.Contains("HQ"));
        Assert.All(_blips.All, b => Assert.False(b.Exists));
        Assert.Equal(0, _scopes.OpenCount);
    }

    [Fact]
    public void FightingSuspectSurrendersOnceSubdued()
    {
        UseOutcome(SuspectBehaviour.Fight);
        FakePed suspect = StartAndArrive();
        Tick();
        Assert.Equal("fight", suspect.Order);

        suspect.IsSubdued = true;
        Tick();

        Assert.Equal("handsup", suspect.Order);
        OpenMenu(suspect);
        Choose(SuspectInteraction.Arrest);
        Tick();
        Tick();
        Assert.True(suspect.Cuffed);
    }

    [Fact]
    public void PrisonerDyingEndsTheCall()
    {
        UseOutcome(SuspectBehaviour.Comply);
        FakePed suspect = StartAndArrive();
        OpenMenu(suspect);
        Choose(SuspectInteraction.Arrest);

        suspect.IsDead = true;
        Tick();

        Assert.Equal(CalloutEndReason.SuspectDied, Ended().Reason);
        Assert.Equal(0, _scopes.OpenCount);
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

    [Fact]
    public void CalmDriverPullsOverForLightsThenIsDealtWithOnFoot()
    {
        UseCall(SuspectBehaviour.Comply, vehicle: true);
        FakePed driver = StartAndSpawn(1)[0];
        FakeVehicle car = _vehicles.Last!;
        Assert.True(driver.InVehicle);
        Assert.Equal(0, driver.Seat);
        Assert.Equal("drive", driver.Order);
        Assert.False(driver.WeaponInHand);

        PlayerInCarAt(car.Position, 20);
        Tick();
        Assert.Equal(CalloutScene.PullOverHelp, _notifier.HelpShown);
        Assert.Equal("drive", driver.Order);   // no lights, no reaction

        _world.PlayerLightsOn = true;
        Tick();
        Assert.Equal("pullover", driver.Order);
        Tick();
        Assert.Equal("pullover", driver.Order);   // still rolling

        car.Speed = 0;
        Tick();
        Assert.Equal("pullover", driver.Order);   // a moment's pause isn't a stop yet
        Advance(_settings.PulledOverMs);
        Assert.Equal("pullover", driver.Order);   // nor is waiting in traffic short of the kerb
        driver.AtPullOverSpot = true;
        Advance(_settings.PulledOverMs);
        Assert.Equal("waiting", driver.Order);    // engine off, waits in the car for the officer
        Assert.True(driver.InVehicle);

        PlayerOnFootAt(driver.Position, 2);
        OpenMenu(driver);
        Assert.True(driver.InVehicle);   // talks through the window
        Assert.False(_menu.Last!.Items.Single(i => i.Id == SuspectInteraction.Search).Enabled);
        Choose(SuspectInteraction.StepOut);
        Assert.Equal("face", driver.Order);
        Assert.False(driver.InVehicle);
        Choose(SuspectInteraction.LetGo);

        Assert.Equal(CalloutEndReason.Resolved, Ended().Reason);
        Assert.Equal(ReleaseMode.Dismiss, car.Released);
        Assert.Equal(0, _scopes.OpenCount);
    }

    [Fact]
    public void FleeingDriverBailsOutOnceTheCarStopsAndCanBeCaught()
    {
        UseCall(SuspectBehaviour.Flee, vehicle: true);
        FakePed driver = StartAndSpawn(1)[0];
        FakeVehicle car = _vehicles.Last!;

        PlayerInCarAt(car.Position, 30);
        Tick();
        Assert.Equal("driveaway", driver.Order);   // bolts without waiting for lights

        car.Speed = 0;
        Tick();
        Assert.Equal("driveaway", driver.Order);   // a moment stuck in traffic is not the end
        Advance(_settings.BailAfterStoppedMs);
        Assert.Equal("flee", driver.Order);
        Assert.False(driver.InVehicle);

        PlayerOnFootAt(driver.Position, 1);
        Tick();
        Assert.Equal("handsup", driver.Order);
    }

    [Fact]
    public void FleeingDriverWearsDownUnderPressureAndStops()
    {
        UseCall(SuspectBehaviour.Flee, vehicle: true);
        FakePed driver = StartAndSpawn(1)[0];
        FakeVehicle car = _vehicles.Last!;
        PlayerInCarAt(car.Position, 20);
        Tick();
        Assert.Equal("driveaway", driver.Order);

        // All four tyres shot out, engine smoking, a gun on them: even the most determined driver breaks.
        car.BurstTyres = 4;
        car.EngineHealth = 0.1f;
        driver.PlayerAimingAt = true;
        for (int i = 0; i < 60 && driver.Order == "driveaway"; i++) Advance(1000);
        Assert.Equal("pullover", driver.Order);

        car.Speed = 1;
        Tick();
        Assert.False(driver.InVehicle);
        Assert.Contains(driver.Order, new[] { "handsup", "flee" });   // gave up, or bailed and ran
    }

    [Fact]
    public void PursuitEndsWhenTheCarGetsAway()
    {
        UseCall(SuspectBehaviour.Flee, vehicle: true);
        StartAndSpawn(1);
        FakeVehicle car = _vehicles.Last!;
        PlayerInCarAt(car.Position, 30);
        Tick();

        car.Position = car.Position with { X = car.Position.X + _settings.VehicleEscapeDistance + 50 };
        _peds.All[0].Position = car.Position;
        Tick();

        Assert.Equal(CalloutEndReason.SuspectEscaped, Ended().Reason);
    }

    [Fact]
    public void ArmedPassengersShootFromTheCarAndFightWhenItIsWrecked()
    {
        UseCall(SuspectBehaviour.Fight, count: 2, vehicle: true);
        List<FakePed> suspects = StartAndSpawn(2);
        FakeVehicle car = _vehicles.Last!;
        Assert.Equal(1, suspects[1].Seat);

        PlayerInCarAt(car.Position, 30);
        Tick();
        Assert.Equal("driveaway", suspects[0].Order);
        Assert.Equal("driveby", suspects[1].Order);

        car.IsWrecked = true;
        Tick();
        Assert.All(suspects, x => Assert.Equal("fight", x.Order));

        suspects[0].IsDead = true;
        Tick();
        Assert.Equal(CalloutService.Phase.Active, _service.Current);   // one still fighting
        suspects[1].IsDead = true;
        Tick();
        Assert.Equal(CalloutEndReason.SuspectDied, Ended().Reason);
    }

    [Fact]
    public void CallWithSeveralSuspectsEndsOnceEveryoneIsAccountedFor()
    {
        UseCall(SuspectBehaviour.Comply, count: 2);
        List<FakePed> suspects = StartAndSpawn(2);
        Assert.All(suspects, x => Assert.Equal("wander", x.Order));
        Assert.NotEqual(suspects[0].Position, suspects[1].Position);

        PlayerOnFootAt(suspects[0].Position, 2);
        Tick();   // they notice the officer
        OpenMenu(suspects[0]);
        Choose(SuspectInteraction.LetGo);
        Assert.Equal(CalloutService.Phase.Active, _service.Current);
        Assert.Contains(_notifier.Messages, m => m.Contains("let go"));

        suspects[1].IsDead = true;
        Tick();
        CalloutEnded ended = Ended();
        Assert.Equal(CalloutEndReason.Resolved, ended.Reason);   // someone was dealt with: not "suspect died"
        Assert.Contains("Code 4", _notifier.Messages[^1]);
        Assert.Equal(0, _scopes.OpenCount);
    }

    /// <summary>The test call always gets <paramref name="twist"/>.</summary>
    void UseTwist(ComplicationPart twist)
    {
        _footCall.Dispose();
        _parts.Situations.Add(new SituationPart("twisty", "Test call", "{suspect} at {location}", [], [], [], [])
            { ComplicationTags = ["t"], ComplicationChance = 1 });
        _parts.Complications.Add(twist with { Tags = ["t"] });
        UseOutcome(SuspectBehaviour.Comply);
    }

    [Fact]
    public void AccompliceShowsUpOnArrivalAndKeepsTheCallOpen()
    {
        UseTwist(new ComplicationPart("second", ComplicationKind.Accomplice, ComplicationTrigger.Arrival, "Two of them!", [])
            { Behaviour = SuspectBehaviour.Flee });
        FakePed suspect = StartAndArrive();
        Tick();   // spawns the accomplice

        Assert.Equal(2, _peds.All.Count);
        FakePed accomplice = _peds.All[1];
        Assert.Equal("flee", accomplice.Order);
        Assert.Equal("WEAPON_KNIFE", accomplice.Weapon);   // the call's suspect's weapon
        Assert.Contains("Two of them!", _notifier.Messages);

        OpenMenu(suspect);
        Choose(SuspectInteraction.LetGo);
        Assert.Equal(CalloutService.Phase.Active, _service.Current);   // the runner is still out there

        accomplice.Position = new Position(Scene.X + _settings.EscapeDistance + 50, 0, 0);
        Tick();
        Assert.Equal(CalloutEndReason.Resolved, Ended().Reason);
        Assert.Equal(0, _scopes.OpenCount);
    }

    [Fact]
    public void CalmSuspectBoltsMidConversationAfterTheDelay()
    {
        UseTwist(new ComplicationPart("bolts", ComplicationKind.Panic, ComplicationTrigger.Delay, null, []) { DelaySeconds = 10 });
        FakePed suspect = StartAndArrive();
        OpenMenu(suspect);
        Assert.Equal("face", suspect.Order);

        Advance(10_000);

        Assert.Equal("flee", suspect.Order);
        Assert.Contains(_notifier.Messages, m => m.Contains("fleeing"));
        Tick(interact: true);   // no menu on a runner
        Assert.Equal(CalloutService.Phase.Active, _service.Current);
    }

    [Fact]
    public void FriendsTryToFreeTheArrestedSuspect()
    {
        UseTwist(new ComplicationPart("rescue", ComplicationKind.Accomplice, ComplicationTrigger.Arrest, null, [])
            { Behaviour = SuspectBehaviour.Fight, Count = 2, Models = ["friend"], Weapon = "WEAPON_BAT" });
        FakePed suspect = StartAndArrive();
        OpenMenu(suspect);
        Assert.Single(_peds.All);

        Choose(SuspectInteraction.Arrest);
        Tick();
        Tick();

        Assert.Equal(3, _peds.All.Count);
        Assert.All(_peds.All.Skip(1), p => Assert.Equal(("fight", "WEAPON_BAT"), (p.Order, p.Weapon)));
    }

    [Fact]
    public void PulledOverDriverFloorsItWhenTheTwistHits()
    {
        _footCall.Dispose();
        _parts.Situations.Add(new SituationPart("car", "Test call", "{suspect} at {location}", [], [], [], [])
            { InVehicle = true, ComplicationTags = ["t"], ComplicationChance = 1 });
        _parts.Suspects.Add(new SuspectPart("car", "Karin Sultan", ["model"], null, []) { Vehicles = ["sultan"] });
        _parts.Complications.Add(new ComplicationPart("floors-it", ComplicationKind.Panic, ComplicationTrigger.Stop, null, ["t"]));
        UseOutcome(SuspectBehaviour.Comply);
        _world.HasStreets = true;
        _duty.SetOnDuty(true);
        Tick();
        Advance(_settings.FirstOfferMaxMs);
        Assert.True(_service.Accept());
        _world.PlayerPosition = new Position(Scene.X - 100, 0, 0);
        for (int i = 0; i < 6; i++) Tick();   // spawn the car, then the driver
        FakeVehicle car = _vehicles.Last!;
        FakePed driver = _peds.Last!;

        _world.PlayerPosition = new Position(car.Position.X - 20, 0, 0);
        _world.PlayerLightsOn = true;
        Tick();
        Assert.Equal("pullover", driver.Order);
        car.Speed = 0;
        driver.AtPullOverSpot = true;
        Tick();
        Advance(_settings.PulledOverMs);   // stopped: the twist fires

        Assert.Equal("driveaway", driver.Order);
        Assert.Contains(_notifier.Messages, m => m.Contains("fleeing"));
    }

    static void Assume(bool condition) => Assert.True(condition, "test setup: seed did not pick the expected part");
}

public class BuiltInContentTests
{
    [Fact]
    public void ShippedDataLoadsWithoutProblemsAndEverySituationIsPlayable()
    {
        var parts = new CalloutParts();
        var log = new ListLog();

        int added = CalloutContentLoader.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "data", "Callouts"), parts, log);

        Assert.Empty(log.Problems);
        Assert.True(added > 0);
        // At runtime a street near the player is always a candidate too.
        var street = new LocationPart("street", "Some St", default, ["street", "public"]);
        foreach (var s in parts.Situations.All)
        {
            Assert.Contains(parts.Locations.All.Append(street), s.Fits);
            Assert.Contains(parts.Suspects.All, s.Fits);
            Assert.Contains(parts.Outcomes.All, s.Fits);
        }
    }
}
