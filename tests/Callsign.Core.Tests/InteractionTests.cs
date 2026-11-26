using Callsign.Core.Entities;
using Callsign.Core.Interaction;
using Callsign.Core.Ports;
using Callsign.Core.Suspects;
using Callsign.Core.Ui;
using Callsign.Core.World;
using Xunit;

namespace Callsign.Core.Tests;

/// <summary>Always rolls the same value: 0 = every chance hits, 0.99 = none do.</summary>
sealed class FixedRandom(double value) : IRandom
{
    public int Next(int maxExclusive) => 0;
    public int Next(int minInclusive, int maxExclusive) => minInclusive;
    public double NextDouble() => value;
}

public class InteractionTests
{
    static readonly Identity Real = new("Dale", "Cooper", 34);
    static readonly Identity Fake = new("Bob", "Briggs", 33);

    readonly FakeClock _clock = new();
    readonly FakeWorld _world = new() { PlayerPosition = new Position(2, 0, 0) };
    readonly FakeNotifier _notifier = new();
    readonly FakeMenuView _view = new();
    readonly FakePed _suspect = new(new Position(0, 0, 0));
    readonly InteractionSettings _settings = new();
    SuspectInteraction _interaction = null!;

    void Create(SuspectTraits traits, double roll = 0.99, double bac = 0, IReadOnlyList<string>? pockets = null,
        string? hiddenWeapon = null, Drug drugs = Drug.None)
    {
        bool lies = (traits & SuspectTraits.FalseName) != 0;
        var profile = new SuspectProfile(Real, lies ? Fake : Real, traits, bac,
            (traits & SuspectTraits.Warrant) != 0 ? "burglary" : null,
            hiddenWeapon, hiddenWeapon != null ? "a switchblade" : null, pockets ?? [], drugs);
        var content = new SuspectContent();
        content.Lines.Add(new DialogueLine(DialogueTopic.NoId, SuspectTraits.None, "I'm {name}."));
        var random = new FixedRandom(roll);
        _interaction = new SuspectInteraction(_suspect, profile,
            new InteractionServices(_world, _notifier, _view, _clock, random, new Dialogue(content, random), _settings));
    }

    InteractionOutcome? Tick(bool stop = false, bool interact = false, MenuInput menu = default) =>
        _interaction.Tick(new InteractionInput(stop, interact, menu));

    void StopAndOpen()
    {
        Assert.Null(Tick(stop: true));
        Assert.True(_interaction.Stopped);
        Assert.Null(Tick(interact: true));
        Assert.True(_interaction.MenuOpen);
    }

    MenuItem Item(string id) => _view.Last!.Items.Single(i => i.Id == id);

    InteractionOutcome? Choose(string id)
    {
        for (int i = 0; i < 20 && _view.Last!.Selected!.Id != id; i++) Tick(menu: new MenuInput(Down: true));
        return Tick(menu: new MenuInput(Select: true));
    }

    [Fact]
    public void StoppingNeedsTheHoldOnFootAndClose()
    {
        Create(SuspectTraits.None);
        _world.PlayerPosition = new Position(_settings.StopDistance + 1, 0, 0);
        Tick(stop: true);
        Assert.False(_interaction.Stopped);

        _world.PlayerPosition = new Position(2, 0, 0);
        _world.PlayerOnFoot = false;
        Tick(stop: true);
        Assert.False(_interaction.Stopped);

        _world.PlayerOnFoot = true;
        Tick();
        Assert.Equal(SuspectInteraction.StopHelp, _notifier.HelpShown);
        Tick(stop: true);
        Assert.True(_interaction.Stopped);
        Assert.Equal("face", _suspect.Order);
    }

    [Fact]
    public void NervousSuspectWithSomethingToHideMayRunWhenStopped()
    {
        Create(SuspectTraits.Nervous | SuspectTraits.CarryingDrugs, roll: 0);

        Assert.Equal(InteractionOutcome.Fled, Tick(stop: true));
        Assert.False(_interaction.Stopped);
    }

    [Fact]
    public void IdThenRecordsCheckRevealsTheWarrantAfterDispatchAnswers()
    {
        Create(SuspectTraits.Warrant);
        StopAndOpen();
        Assert.False(Item(SuspectInteraction.RunName).Enabled);   // no name yet

        Choose(SuspectInteraction.AskId);
        Assert.Contains(_notifier.Messages, m => m.Contains("Dale Cooper"));
        Assert.True(Item(SuspectInteraction.RunName).Enabled);

        Choose(SuspectInteraction.RunName);
        Assert.False(Item(SuspectInteraction.BreathTest).Enabled);   // busy with the radio
        _clock.NowMs += _settings.RecordsCheckMs;
        Tick();

        Assert.Contains("WANTED", Item(SuspectInteraction.RunName).Label);
        Assert.Contains("burglary", Item(SuspectInteraction.RunName).Label);
        Assert.False(Item(SuspectInteraction.RunName).Enabled);
        Assert.True(Item(SuspectInteraction.BreathTest).Enabled);
    }

    [Fact]
    public void AFalseNameComesBackAsNoMatch()
    {
        Create(SuspectTraits.Warrant | SuspectTraits.FalseName | SuspectTraits.NoId);
        StopAndOpen();

        Choose(SuspectInteraction.AskId);
        Assert.Contains(_notifier.Messages, m => m.Contains("I'm Bob Briggs."));
        Choose(SuspectInteraction.RunName);
        _clock.NowMs += _settings.RecordsCheckMs;
        Tick();

        Assert.Contains("no match", Item(SuspectInteraction.RunName).Label);
    }

    [Fact]
    public void NervousWantedSuspectMayRunWhenTheirNameGoesOverTheRadio()
    {
        Create(SuspectTraits.Warrant | SuspectTraits.Nervous, roll: 0.3);   // 0.3 < FleeOnRecordsCheck, > FleeWhenStopped
        StopAndOpen();
        Choose(SuspectInteraction.AskId);

        Assert.Equal(InteractionOutcome.Fled, Choose(SuspectInteraction.RunName));
        Assert.False(_interaction.MenuOpen);
    }

    [Fact]
    public void BreathTestShowsTheReadingAfterTheDelay()
    {
        Create(SuspectTraits.Intoxicated, bac: 0.124);
        StopAndOpen();

        Choose(SuspectInteraction.BreathTest);
        _clock.NowMs += _settings.BreathTestMs - 1;
        Tick();
        Assert.Contains("blowing", Item(SuspectInteraction.BreathTest).Label);
        _clock.NowMs += 1;
        Tick();

        Assert.Contains("0.124%", Item(SuspectInteraction.BreathTest).Label);
        Assert.Contains("over the limit", Item(SuspectInteraction.BreathTest).Label);
    }

    [Fact]
    public void DrugSwabNeedsTheVehicleNearby()
    {
        Create(SuspectTraits.None, drugs: Drug.Cocaine | Drug.Cannabis);
        StopAndOpen();
        Assert.False(Item(SuspectInteraction.DrugSwab).Enabled);

        _world.PlayerVehicle = new Position(10, 0, 0);
        Tick();
        Assert.True(Item(SuspectInteraction.DrugSwab).Enabled);
        Choose(SuspectInteraction.DrugSwab);
        _clock.NowMs += _settings.DrugSwabMs;
        Tick();

        string label = Item(SuspectInteraction.DrugSwab).Label;
        Assert.Contains("THC", label);
        Assert.Contains("Cocaine", label);
        Assert.DoesNotContain("Opiates", label);
        string panel = _notifier.Messages.Last(m => m.Contains("Drug swab"));
        Assert.Contains("Cocaine: ~r~POSITIVE", panel);
        Assert.Contains("THC: ~r~POSITIVE", panel);
        Assert.Contains("Opiates: ~g~negative", panel);
        Assert.Contains("Benzodiazepines: ~g~negative", panel);
    }

    [Fact]
    public void CleanSuspectTestsNegativeOnTheWholePanel()
    {
        Create(SuspectTraits.None);
        _world.PlayerVehicle = new Position(10, 0, 0);
        StopAndOpen();
        Choose(SuspectInteraction.DrugSwab);
        _clock.NowMs += _settings.DrugSwabMs;
        Tick();

        Assert.Contains("all negative", Item(SuspectInteraction.DrugSwab).Label);
        Assert.DoesNotContain(_notifier.Messages, m => m.Contains("POSITIVE"));
    }

    [Fact]
    public void PatDownFindsAndTakesTheHiddenWeapon()
    {
        Create(SuspectTraits.ConcealedWeapon | SuspectTraits.CarryingDrugs, pockets: ["a lighter", "a baggie of weed", "a switchblade"],
            hiddenWeapon: "WEAPON_SWITCHBLADE");
        _suspect.GiveWeapon("WEAPON_SWITCHBLADE", equip: false);
        StopAndOpen();

        Assert.Null(Choose(SuspectInteraction.Search));

        Assert.Contains(_notifier.Messages, m => m.Contains("a baggie of weed") && m.Contains("a switchblade"));
        Assert.Null(_suspect.Weapon);
    }

    [Fact]
    public void NervousSuspectMayPullTheHiddenWeaponWhenSearched()
    {
        Create(SuspectTraits.ConcealedWeapon | SuspectTraits.Nervous, roll: 0.3, hiddenWeapon: "WEAPON_SWITCHBLADE");
        StopAndOpen();

        Assert.Equal(InteractionOutcome.Attacked, Choose(SuspectInteraction.Search));
        Assert.True(_suspect.WeaponInHand);
    }

    [Fact]
    public void WantedSuspectMayRunOnArrestButNotTwice()
    {
        Create(SuspectTraits.Warrant, roll: 0);
        StopAndOpen();

        Assert.Equal(InteractionOutcome.Fled, Choose(SuspectInteraction.Arrest));

        _interaction.Surrendered();   // caught
        Tick(interact: true);
        Assert.Equal(InteractionOutcome.Arrest, Choose(SuspectInteraction.Arrest));
    }

    [Fact]
    public void LeavingAStoppedSuspectLetsThemWalkOn()
    {
        Create(SuspectTraits.None);
        StopAndOpen();

        _world.PlayerPosition = new Position(_settings.WalkAwayDistance + 1, 0, 0);
        Tick();

        Assert.False(_interaction.Stopped);
        Assert.False(_interaction.MenuOpen);
        Assert.Equal("wander", _suspect.Order);
    }

    [Fact]
    public void MenuClosesWithBackOrTheInteractKey()
    {
        Create(SuspectTraits.None);
        StopAndOpen();

        Tick(menu: new MenuInput(Back: true));
        Assert.False(_interaction.MenuOpen);
        Tick(interact: true);
        Assert.True(_interaction.MenuOpen);
        Tick(interact: true);
        Assert.False(_interaction.MenuOpen);
    }
}
