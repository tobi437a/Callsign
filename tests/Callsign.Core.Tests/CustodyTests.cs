using Callsign.Core.Arrest;
using Callsign.Core.Entities;
using Callsign.Core.Ports;
using Callsign.Core.World;
using Xunit;

namespace Callsign.Core.Tests;

public class CustodyTests
{
    static readonly PoliceStation Near = new("near", "Near PD", new Position(0, 1000, 0));
    static readonly PoliceStation Far = new("far", "Far PD", new Position(0, -9000, 0));

    readonly FakeClock _clock = new();
    readonly FakeWorld _world = new();
    readonly FakeBlips _blips = new();
    readonly FakeNotifier _notifier = new();
    readonly CustodySettings _settings = new();
    readonly EntityScope _scope;
    readonly FakePed _prisoner;

    public CustodyTests()
    {
        _scope = new EntityScopes(new NullLog()).Open("test");
        _prisoner = _scope.Track(new FakePed(new Position(1, 0, 0)) { CuffLoadTicks = 0 });
    }

    Custody Create(params PoliceStation[] stations) =>
        new(_prisoner, _scope, new CustodyServices(_world, _blips, _notifier, _clock, stations, _settings));

    /// <summary>Cuff, get in the car, prisoner seated.</summary>
    Custody Seated(params PoliceStation[] stations)
    {
        Custody custody = Create(stations);
        custody.Tick();
        _world.PlayerInVehicle = true;
        custody.Tick();
        _prisoner.InPlayerVehicle = true;
        custody.Tick();
        Assert.Equal(Custody.Stage.Transport, custody.Current);
        return custody;
    }

    [Fact]
    public void TakesThePrisonerToTheNearestStation()
    {
        Custody custody = Seated(Far, Near);

        Assert.Equal(Near, custody.Station);
        _world.PlayerPosition = new Position(0, 1000 - _settings.BookDistance + 1, 0);
        Assert.Equal(CustodyOutcome.Booked, custody.Tick());
        Assert.Equal(Custody.Stage.Done, custody.Current);
        Assert.Null(custody.Tick());
    }

    [Fact]
    public void WithoutStationsThePrisonerIsBookedOnceSeated()
    {
        Custody custody = Seated();

        Assert.Equal(CustodyOutcome.Booked, custody.Tick());
    }

    [Fact]
    public void PrisonerFarAwayDoesNotBoard()
    {
        Custody custody = Create(Near);
        custody.Tick();
        _prisoner.Position = new Position(_settings.BoardDistance + 5, 0, 0);
        _world.PlayerInVehicle = true;

        custody.Tick();

        Assert.Equal(Custody.Stage.Escorting, custody.Current);
        Assert.Equal(Custody.LeftBehindHelp, _notifier.HelpShown);
    }

    [Fact]
    public void NoFreeBackSeatIsExplained()
    {
        Custody custody = Create(Near);
        custody.Tick();
        _prisoner.HasFreeSeat = false;
        _world.PlayerInVehicle = true;

        custody.Tick();

        Assert.Equal(Custody.Stage.Escorting, custody.Current);
        Assert.Equal(Custody.NoSeatHelp, _notifier.HelpShown);
    }

    [Fact]
    public void GettingOutBeforeThePrisonerIsSeatedGoesBackToEscorting()
    {
        Custody custody = Create(Near);
        custody.Tick();
        _world.PlayerInVehicle = true;
        custody.Tick();
        Assert.Equal(Custody.Stage.Boarding, custody.Current);

        _world.PlayerInVehicle = false;
        custody.Tick();

        Assert.Equal(Custody.Stage.Escorting, custody.Current);
        Assert.Equal("follow", _prisoner.Order);
    }

    [Fact]
    public void StuckPrisonerIsPutInTheSeatAfterTheTimeout()
    {
        Custody custody = Create(Near);
        custody.Tick();
        _world.PlayerInVehicle = true;
        custody.Tick();

        _clock.NowMs += _settings.BoardTimeoutMs;
        custody.Tick();
        Assert.Equal("warp", _prisoner.Order);
        custody.Tick();

        Assert.Equal(Custody.Stage.Transport, custody.Current);
    }

    [Fact]
    public void SwitchingVehiclesMidTransportMakesThePrisonerFollowAgain()
    {
        Custody custody = Seated(Near);

        _prisoner.InPlayerVehicle = false;
        custody.Tick();

        Assert.Equal(Custody.Stage.Escorting, custody.Current);
        Assert.Equal("follow", _prisoner.Order);
        Assert.All(_blips.All, b => Assert.False(b.Exists));
    }

    [Fact]
    public void LosingThePrisonerEndsCustody()
    {
        Custody custody = Seated(Near);

        _prisoner.Exists = false;

        Assert.Equal(CustodyOutcome.PrisonerLost, custody.Tick());
        Assert.All(_blips.All, b => Assert.False(b.Exists));
    }
}

public class StationLoaderTests
{
    [Fact]
    public void ShippedStationsLoadWithoutProblems()
    {
        var log = new ListLog();

        var stations = StationLoader.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "data", "Arrest"), log);

        Assert.Empty(log.Problems);
        Assert.NotEmpty(stations);
    }

    [Fact]
    public void BadEntriesAreSkipped()
    {
        var log = new ListLog();
        const string xml = """
            <Stations>
              <Station id="ok" name="Good PD" x="1" y="2" z="3" />
              <Station id="nameless" x="1" y="2" z="3" />
            </Stations>
            """;

        var stations = StationLoader.Load(new StringReader(xml), "test.xml", log);

        PoliceStation station = Assert.Single(stations);
        Assert.Equal(new Position(1, 2, 3), station.Position);
        Assert.Single(log.Problems);
    }
}

sealed class ListLog : ILog
{
    public readonly List<string> Problems = [];
    public void Info(string message) { }
    public void Warn(string message) => Problems.Add(message);
    public void Error(string message, Exception? exception = null) => Problems.Add(message);
}
