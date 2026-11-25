using Callsign.Core.Ports;
using Callsign.Core.Suspects;
using Callsign.Core.Ui;
using Xunit;

namespace Callsign.Core.Tests;

public class SuspectProfileTests
{
    static SuspectContent Shipped()
    {
        var content = new SuspectContent();
        var log = new ListLog();
        SuspectContentLoader.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "data", "Suspects"), content, log);
        Assert.Empty(log.Problems);
        return content;
    }

    [Fact]
    public void ShippedContentLoadsCompleteAndEveryTopicHasALineForAnyone()
    {
        SuspectContent content = Shipped();

        Assert.Empty(content.Missing());
        foreach (DialogueTopic topic in Enum.GetValues<DialogueTopic>())
            Assert.Contains(content.Lines, l => l.Topic == topic && l.Trait == SuspectTraits.None);
    }

    [Fact]
    public void BadLinesAreSkipped()
    {
        var content = new SuspectContent();
        var log = new ListLog();
        const string xml = """
            <SuspectContent>
              <MaleNames>Al, Bob ,, Cy</MaleNames>
              <Line topic="Purpose">Fine line.</Line>
              <Line topic="Weather">Nice day.</Line>
              <Line topic="Purpose" trait="Grumpy">Hmph.</Line>
              <HiddenWeapon name="a thing" />
            </SuspectContent>
            """;

        SuspectContentLoader.Load(new StringReader(xml), "t.xml", content, log);

        Assert.Equal(["Al", "Bob", "Cy"], content.MaleNames);
        Assert.Single(content.Lines);
        Assert.Equal(3, log.Problems.Count);
    }

    [Fact]
    public void TagsForceTraits()
    {
        var generator = new SuspectProfileGenerator(Shipped(), new SeededRandom(1));

        for (int i = 0; i < 20; i++)
        {
            SuspectProfile p = generator.Generate(true, ["rowdy", "Intoxicated", "falsename"], visiblyArmed: false);
            Assert.True(p.Has(SuspectTraits.Intoxicated));
            Assert.True(p.OverAlcoholLimit);
            Assert.True(p.Has(SuspectTraits.FalseName | SuspectTraits.NoId));
            Assert.NotEqual(p.Real.FullName, p.Stated.FullName);
        }
    }

    [Fact]
    public void TraitsHangTogether()
    {
        var generator = new SuspectProfileGenerator(Shipped(), new SeededRandom(3));

        for (int i = 0; i < 500; i++)
        {
            SuspectProfile p = generator.Generate(i % 2 == 0, [], visiblyArmed: i % 3 == 0);

            Assert.Equal(p.Has(SuspectTraits.Warrant), p.WarrantOffence != null);
            Assert.Equal(p.Has(SuspectTraits.ConcealedWeapon), p.ConcealedWeapon != null);
            if (p.ConcealedWeapon != null) Assert.Contains(p.ConcealedWeaponName!, p.Pockets);
            if (i % 3 == 0) Assert.False(p.Has(SuspectTraits.ConcealedWeapon));
            if (p.Has(SuspectTraits.FalseName)) Assert.True(p.Has(SuspectTraits.NoId));
            else Assert.Equal(p.Real, p.Stated);
            Assert.Equal(p.Has(SuspectTraits.Intoxicated), p.OverAlcoholLimit);
            Assert.InRange(p.Real.Age, 18, 65);
        }
    }

    [Fact]
    public void WithDefaultChancesMostSuspectsAreClean()
    {
        var generator = new SuspectProfileGenerator(Shipped(), new SeededRandom(5));

        int wanted = Enumerable.Range(0, 1000).Count(_ => generator.Generate(true, [], false).Has(SuspectTraits.Warrant));

        Assert.InRange(wanted, 100, 200);
    }

    [Fact]
    public void DialoguePrefersLinesForTheSuspectsTraits()
    {
        var content = new SuspectContent();
        content.Lines.Add(new DialogueLine(DialogueTopic.NoId, SuspectTraits.None, "I'm {name}."));
        content.Lines.Add(new DialogueLine(DialogueTopic.NoId, SuspectTraits.FalseName, "Uh... {name}."));
        var dialogue = new Dialogue(content, new SeededRandom(1));
        var honest = Profile(SuspectTraits.NoId);
        var liar = Profile(SuspectTraits.NoId | SuspectTraits.FalseName);

        Assert.Equal("I'm Al Fake.", dialogue.Say(DialogueTopic.NoId, honest));
        Assert.Equal("Uh... Al Fake.", dialogue.Say(DialogueTopic.NoId, liar));
        Assert.Equal("", dialogue.Say(DialogueTopic.Purpose, honest));
    }

    static SuspectProfile Profile(SuspectTraits traits) =>
        new(new Identity("Al", "Real", 30), new Identity("Al", "Fake", 30), traits, 0, null, null, null, []);
}

public class MenuTests
{
    [Fact]
    public void MovesWrapsAndOnlyPicksEnabledItems()
    {
        var menu = new Menu("Test");
        menu.SetItems([new MenuItem("a", "A"), new MenuItem("b", "B", Enabled: false), new MenuItem("c", "C")]);

        Assert.Null(menu.Handle(new MenuInput(Down: true, Select: true)));   // b is disabled
        Assert.Equal("c", menu.Handle(new MenuInput(Down: true, Select: true)));
        Assert.Equal("a", menu.Handle(new MenuInput(Down: true, Select: true)));
        Assert.Equal("c", menu.Handle(new MenuInput(Up: true, Select: true)));
    }

    [Fact]
    public void SelectionFollowsTheItemWhenItemsChange()
    {
        var menu = new Menu("Test");
        menu.SetItems([new MenuItem("a", "A"), new MenuItem("b", "B")]);
        menu.Handle(new MenuInput(Down: true));

        menu.SetItems([new MenuItem("new", "New"), new MenuItem("a", "A"), new MenuItem("b", "B")]);
        Assert.Equal("b", menu.Selected!.Id);

        menu.SetItems([new MenuItem("a", "A")]);
        Assert.Equal("a", menu.Selected!.Id);

        menu.SetItems([]);
        Assert.Null(menu.Selected);
        Assert.Null(menu.Handle(new MenuInput(Select: true)));
    }
}
