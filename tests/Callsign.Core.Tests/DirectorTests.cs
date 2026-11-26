using Callsign.Core.Callouts;
using Callsign.Core.Director;
using Callsign.Core.Events;
using Callsign.Core.Ports;
using Callsign.Core.TrafficStops;
using Callsign.Core.World;
using Xunit;

namespace Callsign.Core.Tests;

public class CallMemoryTests
{
    [Fact]
    public void LastUsedIsPushedDownMostAndRecoversAsItAges()
    {
        var memory = new CallMemory(capacity: 3, strength: 0.9);
        memory.Remember("a");
        double justUsed = memory.Factor("a");
        memory.Remember("b");
        double oneAgo = memory.Factor("a");
        memory.Remember("c");
        memory.Remember("d");   // "a" drops out

        Assert.Equal(1, memory.Factor("x"));
        Assert.True(justUsed < oneAgo && oneAgo < 1);
        Assert.Equal(0.1, justUsed, 6);
        Assert.Equal(1, memory.Factor("a"));
    }

    [Fact]
    public void RepeatedUseStacks()
    {
        var memory = new CallMemory(4, 0.5);
        memory.Remember("a");
        double once = memory.Factor("a");
        memory.Remember("a");
        Assert.True(memory.Factor("a") < once);
    }
}

public class PacingDirectorTests
{
    static SituationPart Situation(string id, double intensity) =>
        new(id, id, "x", [], [], [], []) { Intensity = intensity };

    static readonly OutcomePart Comply = new("comply", SuspectBehaviour.Comply, []);
    static readonly OutcomePart Fight = new("fight", SuspectBehaviour.Fight, []);

    static Incident Incident(SituationPart s, OutcomePart outcome, string location = "Legion Square") =>
        new(s, new LocationPart("loc", location, new Position(0, 0, 0), []),
            new SuspectPart("p", "man", ["m"], null, []), [new SuspectRoll("m", outcome)]);

    readonly EventBus _bus = new(ex => throw ex);
    readonly FakeClock _clock = new();
    readonly PacingDirector _director;

    public PacingDirectorTests() => _director = new PacingDirector(_bus, _clock);

    static readonly SituationPart Calm = Situation("calm", 0.1);
    static readonly SituationPart Big = Situation("big", 0.9);

    [Fact]
    public void AfterAnIntenseCallCalmCallsAreFavoured()
    {
        _bus.Publish(new CalloutEnded(Incident(Big, Fight), CalloutEndReason.Arrested));

        Assert.True(_director.Tension > 0.8);
        Assert.True(_director.Factor(Calm) > 3 * _director.Factor(Situation("other-big", 0.9)));
        Assert.True(_director.GapScale > 1.5);
    }

    [Fact]
    public void ALongQuietSpellFavoursBigCalls()
    {
        _clock.NowMs = 30 * 60_000;
        Assert.True(_director.Factor(Big) > 2 * _director.Factor(Calm));
    }

    [Fact]
    public void TensionDecaysOverTime()
    {
        _bus.Publish(new CalloutEnded(Incident(Big, Fight), CalloutEndReason.Arrested));
        double start = _director.Tension;
        _clock.NowMs += new DirectorSettings().TensionHalfLifeMs;
        Assert.Equal(start / 2, _director.Tension, 6);
    }

    [Fact]
    public void DeclinedCallsAndCancelledStopsAddNoTension()
    {
        _bus.Publish(new CalloutEnded(Incident(Big, Fight), CalloutEndReason.Declined));
        _bus.Publish(new TrafficStopEnded("car", TrafficStopResult.Cancelled));
        Assert.Equal(0, _director.Tension);
    }

    [Fact]
    public void AnEscapedTrafficStopRaisesTension()
    {
        _bus.Publish(new TrafficStopEnded("car", TrafficStopResult.Escaped));
        Assert.True(_director.Tension >= 0.5);
    }

    [Fact]
    public void OfferedPartsAreRememberedEvenIfDeclined()
    {
        var calm = Situation("calm", 0.3);
        double before = _director.Factor(calm);
        _bus.Publish(new CalloutOffered(Incident(calm, Comply, "Del Perro Pier")));

        Assert.True(_director.Factor(calm) < before / 3);
        Assert.True(_director.Factor(new LocationPart("street", "Del Perro Pier", new Position(0, 0, 0), [])) < 0.5);
        Assert.Contains("calm", _director.Describe());
    }
}

public class DirectedGeneratorTests
{
    [Fact]
    public void RecentSituationsRarelyComeUpTwiceInARow()
    {
        var parts = new CalloutParts();
        foreach (string id in new[] { "a", "b", "c" })
            parts.Situations.Add(new SituationPart(id, id, "x", [], [], [], []));
        parts.Locations.Add(new LocationPart("loc", "Somewhere", new Position(500, 0, 0), []));
        parts.Suspects.Add(new SuspectPart("p", "man", ["m"], null, []));
        parts.Outcomes.Add(new OutcomePart("o", SuspectBehaviour.Comply, []));

        var bus = new EventBus(ex => throw ex);
        var director = new PacingDirector(bus, new FakeClock());
        var generator = new IncidentGenerator(parts, new SeededRandom(7), director: director);

        int repeats = 0;
        string? last = null;
        for (int i = 0; i < 300; i++)
        {
            Incident incident = generator.Generate(new Position(0, 0, 0), 0, 1000)!;
            bus.Publish(new CalloutOffered(incident));
            if (incident.Situation.Id == last) repeats++;
            last = incident.Situation.Id;
        }

        Assert.True(repeats < 300 / 3 / 2, "plain weights would repeat about a third of the time; got " + repeats);
    }
}
