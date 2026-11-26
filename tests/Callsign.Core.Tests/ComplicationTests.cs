using Callsign.Core.Callouts;
using Callsign.Core.Director;
using Callsign.Core.Events;
using Callsign.Core.Ports;
using Callsign.Core.World;
using Xunit;

namespace Callsign.Core.Tests;

public class ComplicationGeneratorTests
{
    static readonly Position Player = new(0, 0, 0);

    static CalloutParts Parts(IReadOnlyList<string> complicationTags, double chance = 1)
    {
        var parts = new CalloutParts();
        parts.Situations.Add(new SituationPart("sit", "Call", "x", [], [], [], [])
            { ComplicationTags = complicationTags, ComplicationChance = chance });
        parts.Locations.Add(new LocationPart("loc", "Somewhere", new Position(500, 0, 0), []));
        parts.Suspects.Add(new SuspectPart("sus", "man", ["suspect_model"], "WEAPON_KNIFE", []));
        parts.Outcomes.Add(new OutcomePart("calm", SuspectBehaviour.Comply, []));
        parts.Complications.Add(new ComplicationPart("friends", ComplicationKind.Accomplice, ComplicationTrigger.Stop, null, ["group"])
            { Count = 2 });
        parts.Complications.Add(new ComplicationPart("bolts", ComplicationKind.Panic, ComplicationTrigger.Delay, null, ["foot"]));
        return parts;
    }

    static Incident Generate(CalloutParts parts, int seed = 1, string? complicationId = null) =>
        new IncidentGenerator(parts, new SeededRandom(seed)).Generate(Player, 100, 1500, complicationId: complicationId)!;

    [Fact]
    public void NoTwistUnlessTheSituationAsksForOne()
    {
        for (int seed = 0; seed < 20; seed++) Assert.Null(Generate(Parts([]), seed).Twist);
    }

    [Fact]
    public void FittingTwistIsRolledWithTheSituationsChance()
    {
        for (int seed = 0; seed < 20; seed++) Assert.Equal("bolts", Generate(Parts(["foot"]), seed).Twist!.Part.Id);
        for (int seed = 0; seed < 20; seed++) Assert.Null(Generate(Parts(["foot"], chance: 0), seed).Twist);

        int twists = Enumerable.Range(0, 400).Count(seed => Generate(Parts(["foot"], chance: 0.3), seed).Twist != null);
        Assert.InRange(twists, 80, 160);
    }

    [Fact]
    public void AccomplicesAreRolledUpFrontLikeTheSuspect()
    {
        Twist twist = Generate(Parts(["group"])).Twist!;

        Assert.Equal(2, twist.Accomplices.Count);
        Assert.All(twist.Accomplices, a => Assert.Equal("suspect_model", a.Model));   // no models given: same kind of person
        Assert.All(twist.Accomplices, a => Assert.Equal(SuspectBehaviour.Comply, a.Outcome.Behaviour));
    }

    [Fact]
    public void DevToolsCanForceOrSuppressATwist()
    {
        Assert.Equal("friends", Generate(Parts([], chance: 0), complicationId: "friends").Twist!.Part.Id);
        Assert.Null(Generate(Parts(["foot"]), complicationId: "none").Twist);
    }

    [Fact]
    public void RejectsBrokenComplications()
    {
        var parts = new CalloutParts();
        Assert.Throws<ArgumentException>(() => parts.Complications.Add(
            new ComplicationPart("a", ComplicationKind.Accomplice, ComplicationTrigger.Stop, null, []) { Count = 0 }));
        Assert.Throws<ArgumentException>(() => parts.Complications.Add(
            new ComplicationPart("b", ComplicationKind.Panic, ComplicationTrigger.Stop, null, []) { Behaviour = SuspectBehaviour.Comply }));
        Assert.Throws<ArgumentException>(() => parts.Situations.Add(
            new SituationPart("s", "T", "x", [], [], [], []) { ComplicationChance = 2 }));
    }

    [Fact]
    public void LoaderReadsComplications()
    {
        const string xml = """
            <CalloutContent>
              <Situation id="s" title="T" complicationTags="foot, group" complicationChance="0.6"><Dispatch>x</Dispatch></Situation>
              <Complication id="rescue" kind="accomplice" trigger="Arrest" behaviour="Fight" count="2" models="m1,m2" weapon="WEAPON_BAT" tags="group">
                <Message> Friends! </Message>
              </Complication>
              <Complication id="late" kind="Panic" trigger="Delay" delay="12" tags="foot" />
              <Complication id="bad" kind="Explode" trigger="Arrival" />
            </CalloutContent>
            """;
        var parts = new CalloutParts();
        var log = new NullLog();

        Assert.Equal(3, CalloutContentLoader.Load(new StringReader(xml), "t.xml", parts, log));

        var situation = Assert.Single(parts.Situations.All);
        Assert.Equal(["foot", "group"], situation.ComplicationTags);
        Assert.Equal(0.6, situation.ComplicationChance);
        var rescue = parts.Complications.All[0];
        Assert.Equal((ComplicationKind.Accomplice, ComplicationTrigger.Arrest, SuspectBehaviour.Fight, 2), (rescue.Kind, rescue.Trigger, rescue.Behaviour!.Value, rescue.Count));
        Assert.Equal(["m1", "m2"], rescue.Models);
        Assert.Equal(("WEAPON_BAT", "Friends!"), (rescue.Weapon, rescue.Message));
        var late = parts.Complications.All[1];
        Assert.Equal((12.0, (SuspectBehaviour?)null, (string?)null), (late.DelaySeconds, late.Behaviour, late.Message));
    }
}

public class ComplicationDirectorTests
{
    [Fact]
    public void FewerTwistsRightAfterChaosMoreAfterAQuietSpell()
    {
        var bus = new EventBus(ex => throw ex);
        var clock = new FakeClock();
        var director = new PacingDirector(bus, clock);
        double normal = director.ComplicationScale;

        var big = new SituationPart("big", "Big", "x", [], [], [], []) { Intensity = 0.9 };
        var incident = new Incident(big, new LocationPart("l", "L", default, []), new SuspectPart("p", "man", ["m"], null, []),
            [new SuspectRoll("m", new OutcomePart("o", SuspectBehaviour.Fight, []))]);
        bus.Publish(new CalloutEnded(incident, CalloutEndReason.Arrested));
        Assert.True(director.ComplicationScale < normal);

        clock.NowMs += 60 * 60_000;
        Assert.True(director.ComplicationScale > normal);
    }

    [Fact]
    public void ACallWithATwistAddsMoreTension()
    {
        var calm = new SituationPart("calm", "Calm", "x", [], [], [], []) { Intensity = 0.2 };
        Incident Make(Twist? twist) => new(calm, new LocationPart("l", "L", default, []), new SuspectPart("p", "man", ["m"], null, []),
            [new SuspectRoll("m", new OutcomePart("o", SuspectBehaviour.Comply, []))], Twist: twist);

        double TensionAfter(Incident incident)
        {
            var bus = new EventBus(ex => throw ex);
            var director = new PacingDirector(bus, new FakeClock());
            bus.Publish(new CalloutEnded(incident, CalloutEndReason.Resolved));
            return director.Tension;
        }

        var panic = new ComplicationPart("bolts", ComplicationKind.Panic, ComplicationTrigger.Delay, null, []);
        Assert.True(TensionAfter(Make(new Twist(panic, []))) > TensionAfter(Make(null)));
    }
}
