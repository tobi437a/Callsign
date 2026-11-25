using Callsign.Core.Callouts;
using Callsign.Core.Ports;
using Callsign.Core.World;
using Xunit;

namespace Callsign.Core.Tests;

public class WeightedTests
{
    [Fact]
    public void PicksProportionallyToWeight()
    {
        var random = new SeededRandom(1);
        var items = new[] { ("a", 1.0), ("b", 3.0), ("never", 0.0) };
        var counts = new Dictionary<string, int> { ["a"] = 0, ["b"] = 0, ["never"] = 0 };

        for (int i = 0; i < 4000; i++)
        {
            Assert.True(Weighted.TryPick(items, x => x.Item2, random, out var picked));
            counts[picked.Item1]++;
        }

        Assert.Equal(0, counts["never"]);
        Assert.InRange(counts["b"] / (double)counts["a"], 2.5, 3.5);
    }

    [Fact]
    public void NothingToPickWhenAllWeightsAreZeroOrEmpty()
    {
        Assert.False(Weighted.TryPick(new[] { 0.0, -1.0 }, x => x, new SeededRandom(1), out _));
        Assert.False(Weighted.TryPick(Array.Empty<double>(), x => x, new SeededRandom(1), out _));
    }
}

public class PartRegistryTests
{
    static OutcomePart Outcome(string id) => new(id, SuspectBehaviour.Comply, []);

    [Fact]
    public void DisposingTheHandleRemovesThePart()
    {
        var registry = new PartRegistry<OutcomePart>();
        var handle = registry.Add(Outcome("a"));
        registry.Add(Outcome("b"));

        handle.Dispose();

        Assert.Equal(["b"], registry.All.Select(p => p.Id));
    }

    [Fact]
    public void RejectsDuplicateAndEmptyIds()
    {
        var registry = new PartRegistry<OutcomePart>();
        registry.Add(Outcome("a"));

        Assert.Throws<ArgumentException>(() => registry.Add(Outcome("A")));
        Assert.Throws<ArgumentException>(() => registry.Add(Outcome(" ")));
    }
}

public class IncidentGeneratorTests
{
    static readonly Position Player = new(0, 0, 0);

    static CalloutParts Parts()
    {
        var parts = new CalloutParts();
        parts.Situations.Add(new SituationPart("shoplifter", "Shoplifter", "A {suspect} stole from {location}.",
            LocationTags: ["store"], SuspectTags: [], OutcomeTags: [], Tags: []));
        parts.Locations.Add(new LocationPart("near-store", "Near Store", new Position(500, 0, 0), ["store"]));
        parts.Locations.Add(new LocationPart("far-store", "Far Store", new Position(5000, 0, 0), ["store"]));
        parts.Locations.Add(new LocationPart("park", "Park", new Position(400, 0, 0), ["park"]));
        parts.Suspects.Add(new SuspectPart("hoodie", "man in a hoodie", ["a_m_y_genstreet_01"], null, []));
        parts.Outcomes.Add(new OutcomePart("runs", SuspectBehaviour.Flee, []));
        return parts;
    }

    [Fact]
    public void BuildsAnIncidentFromFittingPartsInRange()
    {
        var incident = new IncidentGenerator(Parts(), new SeededRandom(1)).Generate(Player, 100, 1500);

        Assert.NotNull(incident);
        Assert.Equal("near-store", incident!.Location.Id);   // park doesn't fit, far-store is out of range
        Assert.Equal("a_m_y_genstreet_01", incident.SuspectModel);
        Assert.Equal("A man in a hoodie stole from Near Store.", incident.DispatchMessage);
    }

    [Fact]
    public void NullWhenNoLocationIsInRange()
    {
        Assert.Null(new IncidentGenerator(Parts(), new SeededRandom(1)).Generate(Player, 100, 300));
    }

    [Fact]
    public void ExtraLocationsCountForThisDrawOnly()
    {
        var generator = new IncidentGenerator(Parts(), new SeededRandom(1));
        var street = new LocationPart("street", "Main St", new Position(200, 0, 0), ["store"]);

        Assert.Equal("street", generator.Generate(Player, 100, 300, [street])!.Location.Id);
        Assert.Null(generator.Generate(Player, 100, 300));
    }

    [Fact]
    public void SkipsSituationsWithNoFittingOutcome()
    {
        var parts = Parts();
        parts.Situations.Add(new SituationPart("armed", "Armed robbery", "x", [], [], OutcomeTags: ["fight"], Tags: [], Weight: 100));

        for (int seed = 0; seed < 20; seed++)
            Assert.Equal("shoplifter", new IncidentGenerator(parts, new SeededRandom(seed)).Generate(Player, 100, 1500)!.Situation.Id);
    }

    [Fact]
    public void SameSeedSameIncident()
    {
        var parts = Parts();
        parts.Suspects.Add(new SuspectPart("drunk", "drunk man", ["a_m_m_tramp_01", "a_m_o_tramp_01"], null, []));
        parts.Outcomes.Add(new OutcomePart("fights", SuspectBehaviour.Fight, []));

        var a = new IncidentGenerator(parts, new SeededRandom(42)).Generate(Player, 100, 1500);
        var b = new IncidentGenerator(parts, new SeededRandom(42)).Generate(Player, 100, 1500);

        Assert.Equal(a, b);
    }
}

public class CalloutContentLoaderTests
{
    sealed class ListLog : ILog
    {
        public readonly List<string> Warnings = [];
        public readonly List<string> Errors = [];
        public void Info(string message) { }
        public void Warn(string message) => Warnings.Add(message);
        public void Error(string message, Exception? exception = null) => Errors.Add(message);
    }

    const string Xml = """
        <CalloutContent>
          <Situation id="suspicious" title="Suspicious person" locationTags="store, county" weight="2">
            <Dispatch>
              A {suspect} near {location}.
            </Dispatch>
          </Situation>
          <Location id="liquor" name="Liquor store" x="1392.5" y="3604" z="34" tags="store" />
          <Suspect id="hoodie" description="man in a hoodie" models="a_m_y_genstreet_01, a_m_y_genstreet_02" weapon="WEAPON_KNIFE" />
          <Outcome id="runs" behaviour="flee" />
          <Outcome id="bad" behaviour="Dance" />
          <Suspect id="nomodel" description="ghost" models="" />
          <Location name="No id" x="0" y="0" z="0" />
        </CalloutContent>
        """;

    [Fact]
    public void LoadsValidPartsAndSkipsBadOnes()
    {
        var parts = new CalloutParts();
        var log = new ListLog();

        int added = CalloutContentLoader.Load(new StringReader(Xml), "test.xml", parts, log);

        Assert.Equal(4, added);
        Assert.Equal(3, log.Warnings.Count);

        var situation = Assert.Single(parts.Situations.All);
        Assert.Equal(["store", "county"], situation.LocationTags);
        Assert.Equal("A {suspect} near {location}.", situation.Dispatch);
        Assert.Equal(2, situation.Weight);

        var location = Assert.Single(parts.Locations.All);
        Assert.Equal(new Position(1392.5f, 3604, 34), location.Position);
        Assert.Equal(1, location.Weight);

        var suspect = Assert.Single(parts.Suspects.All);
        Assert.Equal(["a_m_y_genstreet_01", "a_m_y_genstreet_02"], suspect.Models);
        Assert.Equal("WEAPON_KNIFE", suspect.Weapon);

        Assert.Equal(SuspectBehaviour.Flee, Assert.Single(parts.Outcomes.All).Behaviour);
    }

    [Fact]
    public void BrokenXmlIsLoggedNotThrown()
    {
        var log = new ListLog();

        Assert.Equal(0, CalloutContentLoader.Load(new StringReader("<CalloutContent><oops"), "bad.xml", new CalloutParts(), log));
        Assert.Single(log.Errors);
    }
}
