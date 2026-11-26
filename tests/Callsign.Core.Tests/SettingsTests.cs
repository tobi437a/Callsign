using Callsign.Core.Callouts;
using Callsign.Core.Events;
using Callsign.Core.Ports;
using Callsign.Core.Settings;
using Callsign.Core.Ui;
using Callsign.Core.World;
using Xunit;

namespace Callsign.Core.Tests;

public class SettingsTests
{
    sealed class MemoryStore : ISettingsStore
    {
        public ModSettings Saved = new();
        public int Saves;
        public ModSettings Load() => Saved;
        public void Save(ModSettings settings) { Saved = settings; Saves++; }
    }

    sealed class NullLog : ILog
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? ex = null) { }
    }

    static CalloutParts Parts()
    {
        var parts = new CalloutParts();
        parts.Situations.Add(new SituationPart("theft", "Shoplifting", "Theft at {location}", [], [], [], []));
        parts.Situations.Add(new SituationPart("fight", "Disturbance", "Fight at {location}", [], [], [], []));
        parts.Locations.Add(new LocationPart("store", "Store", new Position(500, 0, 0), []));
        parts.Suspects.Add(new SuspectPart("guy", "a man", ["a_m_y_genstreet_01"], null, []));
        parts.Outcomes.Add(new OutcomePart("calm", SuspectBehaviour.Comply, []));
        return parts;
    }

    static readonly MenuInput Down = new(Down: true), Select = new(Select: true), Back = new(Back: true);

    [Fact]
    public void DisabledSituationsAreNeverGenerated()
    {
        var prefs = new CalloutPreferences();
        prefs.SetEnabled("THEFT", false);
        var generator = new IncidentGenerator(Parts(), new SeededRandom(3), s => prefs.IsEnabled(s.Id));

        for (int i = 0; i < 50; i++)
            Assert.Equal("fight", generator.Generate(new Position(0, 0, 0), 0, 1000)!.Situation.Id);

        prefs.SetEnabled("fight", false);
        Assert.Null(generator.Generate(new Position(0, 0, 0), 0, 1000));
        // Dev tools can still force one by id.
        Assert.Equal("theft", generator.Generate(new Position(0, 0, 0), 0, 1000, situationId: "theft")!.Situation.Id);
    }

    [Fact]
    public void TogglingACalloutInTheMenuSavesAndPublishes()
    {
        var store = new MemoryStore();
        var bus = new EventBus(ex => throw ex);
        var changes = 0;
        bus.Subscribe<SettingsChanged>(_ => changes++);
        var settings = new SettingsService(store, bus);
        var menu = new SettingsMenu([new CalloutsSettingsPage(Parts(), settings)]);

        menu.Open();
        Assert.Equal(["0", SettingsMenu.CloseId], menu.Menu.Items.Select(i => i.Id));
        menu.Tick(Select);                      // open "Callouts"
        Assert.Equal("Callouts", menu.Menu.Title);
        // Sorted by title: Disturbance, Shoplifting, then "Switch all on" (disabled while all are on).
        Assert.Equal([CalloutsSettingsPage.ItemId("fight"), CalloutsSettingsPage.ItemId("theft"), CalloutsSettingsPage.AllOnId],
            menu.Menu.Items.Select(i => i.Id));
        Assert.False(menu.Menu.Items[2].Enabled);

        menu.Tick(Down);
        menu.Tick(Select);                      // Shoplifting off
        Assert.Equal(["theft"], store.Saved.Callouts.Disabled);
        Assert.Equal("~r~Off", menu.Menu.Items[1].Value);
        Assert.Equal(1, store.Saves);
        Assert.Equal(1, changes);

        menu.Tick(Down);
        menu.Tick(Select);                      // Switch all on
        Assert.Empty(store.Saved.Callouts.Disabled);

        menu.Tick(Back);
        Assert.Equal("Callsign settings", menu.Menu.Title);
        menu.Tick(Back);
        Assert.False(menu.IsOpen);
    }

    [Fact]
    public void SettingsFileRoundTripsAndSurvivesABrokenFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "callsign-settings-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.xml");
            var file = new SettingsFile(path, new NullLog());
            Assert.Empty(file.Load().Callouts.Disabled);   // no file yet: defaults

            var settings = new ModSettings();
            settings.Callouts.SetEnabled("theft", false);
            file.Save(settings);
            Assert.Equal(["theft"], file.Load().Callouts.Disabled);

            File.WriteAllText(path, "<CallsignSettings><Callouts>");
            Assert.Empty(file.Load().Callouts.Disabled);
        }
        finally { Directory.Delete(dir, true); }
    }
}
