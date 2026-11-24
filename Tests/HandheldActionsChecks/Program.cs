using ZirconClient.HandheldActions;

var checks = new Checks();
checks.DirectButtonsEmitOnPress();
checks.SemanticButtonsSurviveLongPress();
checks.HoldAndChordSemantics();
checks.PageSwitchAndInputCancellation();
checks.ConfigurationValidationAndPersistence();
checks.ChordWindowBoundaryIsInclusiveAndLatePressDoesNotPair();
checks.SequentialShoulderPressCannotFormChord();
checks.TriggerTapCommitsOnRelease();
checks.TimingConfigurationIsPersistedAndValidated();
checks.SnapshotReportsPendingPressAndIgnoresRepeats();
checks.DueThresholdCommitsBeforeLaterInput();
checks.TakeoverClearsQueuedIntents();
checks.ChordCannotReuseSuppressedPartner();
checks.ChordWinsExactSharedThresholdBoundary();
checks.ReloadCancelsPendingAndPreservesReleaseGates();
checks.MenuActionsAreBoundedAndRoundTrip();
Console.WriteLine($"PASS: {checks.Count} handheld action checks");

sealed class Checks
{
    public int Count { get; private set; }

    public void DirectButtonsEmitOnPress()
    {
        var config = HandheldActionConfiguration.CreateDefault();
        foreach (var button in config.Pages[0].Bindings.Keys.ToArray())
            config.Pages[0].Bindings[button] = ActionBinding.Menu(MenuAction.OpenInventory);
        var engine = new HandheldActionEngine(config);
        double now = 100;
        foreach (var button in config.Pages[0].Bindings.Keys)
        {
            engine.Press(button, now);
            Require(engine.DrainIntents().SingleOrDefault()?.MenuAction == MenuAction.OpenInventory, $"{button} fires on press");
            engine.Press(button, now + 1);
            engine.Tick(now + 600);
            Require(!engine.Snapshot(now + 600).Pending.Contains(button), "direct key has no unresolved action");
            engine.Release(button, now + 610);
            Require(engine.DrainIntents().Count == 0, "repeat, hold and release do not fire a second direct action");
            now += 700;
        }
        Count++;
    }

    public void SemanticButtonsSurviveLongPress()
    {
        var engine = new HandheldActionEngine(HandheldActionConfiguration.CreateDefault(), 600, 80);
        engine.Press(HandheldButton.A, 0);
        engine.Tick(600);
        Require(engine.DrainIntents().Count == 0, "semantic A does not acquire an unrelated configurable hold action");
        engine.Release(HandheldButton.A, 601);
        var intents = engine.DrainIntents();
        Require(intents.Count == 1 && intents[0].MenuAction == MenuAction.Interact, "A release expresses interaction intent");
        Count++;
    }

    public void HoldAndChordSemantics()
    {
        var config = HandheldActionConfiguration.CreateDefault();
        config.FixedSlots["R2hold"] = ActionBinding.Menu(MenuAction.Target);
        config.FixedSlots["L2tap"] = ActionBinding.Menu(MenuAction.OpenSkills);
        config.FixedSlots["L2hold"] = ActionBinding.Menu(MenuAction.OpenSystem);
        config.FixedSlots["L1+R1"] = ActionBinding.Menu(MenuAction.OpenCharacter);
        config.FixedSlots["L2+R2"] = ActionBinding.Menu(MenuAction.OpenQuests);
        var engine = new HandheldActionEngine(config, 600, 80);
        engine.Press(HandheldButton.R2, 0);
        engine.Tick(599);
        Require(engine.DrainIntents().Count == 0, "hold threshold is not crossed early");
        engine.Tick(600);
        Require(engine.DrainIntents().Single().Trigger == HandheldTrigger.Hold, "hold emits once at threshold");
        engine.Release(HandheldButton.R2, 610);
        Require(engine.DrainIntents().Count == 0, "release after hold does not synthesize tap");

        engine.Press(HandheldButton.L2, 700);
        engine.Press(HandheldButton.R2, 750);
        Require(engine.DrainIntents().Single().MenuAction == MenuAction.OpenQuests, "L2+R2 chord emits its fixed intent once");
        engine.Tick(1400);
        Require(engine.DrainIntents().Count == 0, "recognized chord suppresses both trigger holds");
        engine.Release(HandheldButton.L2, 1401);
        engine.Release(HandheldButton.R2, 1402);

        engine.Press(HandheldButton.L1, 1500);
        engine.Press(HandheldButton.R1, 1540);
        engine.Release(HandheldButton.L1, 1550);
        engine.Release(HandheldButton.R1, 1560);
        Require(engine.Snapshot(1560).CurrentPage == 0, "L1+R1 chord does not change pages");
        Require(engine.DrainIntents().Single().MenuAction == MenuAction.OpenCharacter, "L1+R1 emits fixed combo intent once");
        Count++;
    }

    public void PageSwitchAndInputCancellation()
    {
        var config = HandheldActionConfiguration.CreateDefault();
        config.Pages[0].Bindings[HandheldButton.X] = ActionBinding.Menu(MenuAction.OpenInventory);
        config.Pages[1].Bindings[HandheldButton.X] = ActionBinding.Menu(MenuAction.OpenSystem);
        config.Pages[1].Bindings[HandheldButton.Y] = ActionBinding.Menu(MenuAction.Target);
        var engine = new HandheldActionEngine(config, 600, 80);
        engine.Press(HandheldButton.R1, 0);
        engine.Release(HandheldButton.R1, 20);
        Require(engine.Snapshot(20).CurrentPage == 1, "single R1 release flips page immediately");
        engine.Tick(100);
        Require(engine.Snapshot(100).CurrentPage == 1, "chord window does not delay a committed single page switch");
        engine.Press(HandheldButton.X, 110);
        engine.Release(HandheldButton.X, 120);
        Require(engine.DrainIntents().Single().MenuAction == MenuAction.OpenSystem, "page-specific binding follows page selection");
        engine.Press(HandheldButton.X, 130);
        engine.DrainIntents();
        engine.SetMenuOwnsInput(true, 140);
        engine.Press(HandheldButton.X, 150);
        engine.Release(HandheldButton.X, 160);
        Require(engine.DrainIntents().Count == 0, "menu takeover cancels pending input until physical release");
        engine.SetMenuOwnsInput(false, 165);
        engine.Press(HandheldButton.X, 170);
        engine.Release(HandheldButton.X, 180);
        Require(engine.DrainIntents().Count == 1, "released key rearms after menu takeover");
        engine.SetMenuOwnsInput(true, 190);
        engine.Press(HandheldButton.Y, 200);
        engine.SetMenuOwnsInput(false, 210);
        engine.Release(HandheldButton.Y, 220);
        Require(engine.DrainIntents().Count == 0, "key pressed while menu owns input stays blocked through release");
        Count++;
    }

    public void ConfigurationValidationAndPersistence()
    {
        var config = HandheldActionConfiguration.CreateDefault();
        config.Pages[0].Bindings[HandheldButton.X] = ActionBinding.Skill("magic:stable-42", SkillActionMode.Toggle);
        config.Pages[1].Bindings[HandheldButton.Y] = ActionBinding.Item("item:stable-9");
        config.LongPressThresholdMs = 750;
        config.ChordWindowMs = 90;
        string directory = Path.Combine(Path.GetTempPath(), "handheld-actions-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "actions.json");
        try
        {
            var saved = HandheldActionConfigurationStore.Save(path, config);
            var loaded = HandheldActionConfigurationStore.Load(path);
            string validContents = File.ReadAllText(path);
            Require(saved.Success && loaded.Pages[0].Bindings[HandheldButton.X].StableId == "magic:stable-42"
                && loaded.LongPressThresholdMs == 750 && loaded.ChordWindowMs == 90, "stable IDs and timing settings round trip");
            config.Pages[0].Bindings[HandheldButton.X].SkillMode = null;
            var rejected = HandheldActionConfigurationStore.Save(path, config);
            Require(!rejected.Success && File.ReadAllText(path) == validContents, "invalid configuration is reported without overwriting valid file");
            config.Pages[0].Bindings[HandheldButton.X].SkillMode = SkillActionMode.Toggle;
            string badPath = Path.Combine(directory, "bad.json");
            File.WriteAllText(badPath, "{ invalid json");
            try { _ = HandheldActionConfigurationStore.Load(badPath); throw new InvalidOperationException("bad config unexpectedly loaded"); }
            catch (System.Text.Json.JsonException) { }
            config.Pages[0].Bindings[HandheldButton.X].StableId = "";
            Require(config.Validate().Count > 0, "blank stable IDs fail validation");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        Count++;
    }

    public void ChordWindowBoundaryIsInclusiveAndLatePressDoesNotPair()
    {
        var config = HandheldActionConfiguration.CreateDefault();
        config.FixedSlots["L2hold"] = ActionBinding.Menu(MenuAction.OpenSystem);
        config.FixedSlots["L2tap"] = ActionBinding.Menu(MenuAction.OpenInventory);
        config.FixedSlots["L2+R2"] = ActionBinding.Menu(MenuAction.OpenQuests);
        var engine = new HandheldActionEngine(config, 600, 80);
        engine.Press(HandheldButton.L2, 0);
        engine.Press(HandheldButton.R2, 80);
        Require(engine.DrainIntents().Single().MenuAction == MenuAction.OpenQuests, "chord window includes exact boundary");
        engine.Release(HandheldButton.L2, 90);
        engine.Release(HandheldButton.R2, 91);

        engine.Press(HandheldButton.L2, 200);
        engine.Press(HandheldButton.R2, 281);
        engine.Tick(800);
        Require(engine.DrainIntents().Count == 1, "late second trigger does not suppress the first trigger hold");
        Require(engine.Snapshot(800).LastTrigger?.MenuAction == MenuAction.OpenSystem, "late second trigger cannot retroactively become a chord");
        Count++;
    }

    public void SequentialShoulderPressCannotFormChord()
    {
        var config = HandheldActionConfiguration.CreateDefault();
        config.FixedSlots["L1+R1"] = ActionBinding.Menu(MenuAction.OpenCharacter);
        var engine = new HandheldActionEngine(config, 600, 80);
        engine.Press(HandheldButton.L1, 0);
        engine.Release(HandheldButton.L1, 10);
        Require(engine.Snapshot(10).CurrentPage == 1, "L1 release switches page immediately");
        engine.Press(HandheldButton.R1, 80);
        Require(engine.DrainIntents().Count == 0, "released L1 cannot pair with a later R1 press");
        engine.Release(HandheldButton.R1, 90);
        Require(engine.Snapshot(90).CurrentPage == 0, "later standalone R1 release switches page independently");
        Count++;
    }

    public void TriggerTapCommitsOnRelease()
    {
        var config = HandheldActionConfiguration.CreateDefault();
        config.FixedSlots["L2tap"] = ActionBinding.Menu(MenuAction.OpenInventory);
        config.FixedSlots["L2hold"] = ActionBinding.Menu(MenuAction.OpenSystem);
        var engine = new HandheldActionEngine(config, 600, 80);
        engine.Press(HandheldButton.L2, 0);
        engine.Release(HandheldButton.L2, 20);
        Require(engine.DrainIntents().Single().MenuAction == MenuAction.OpenInventory, "L2 tap intent commits immediately on release");
        engine.Press(HandheldButton.L2, 100);
        engine.Tick(700);
        Require(engine.DrainIntents().Single().Trigger == HandheldTrigger.Hold, "L2 hold emits once at threshold");
        engine.Release(HandheldButton.L2, 701);
        Require(engine.DrainIntents().Count == 0, "released L2 hold does not add a tap");
        Count++;
    }

    public void TimingConfigurationIsPersistedAndValidated()
    {
        var config = HandheldActionConfiguration.CreateDefault();
        config.LongPressThresholdMs = 150;
        config.ChordWindowMs = 40;
        config.FixedSlots["L2hold"] = ActionBinding.Menu(MenuAction.OpenInventory);
        var engine = new HandheldActionEngine(config);
        engine.Press(HandheldButton.L2, 0);
        engine.Tick(149);
        Require(engine.DrainIntents().Count == 0, "configured long threshold has not fired before boundary");
        engine.Tick(150);
        Require(engine.DrainIntents().Single().Trigger == HandheldTrigger.Hold, "configured long threshold fires at exact boundary");
        config.LongPressThresholdMs = HandheldActionEngine.MinThresholdMs - 1;
        Require(config.Validate().Count > 0, "out-of-range timing setting is rejected by configuration validation");
        config.LongPressThresholdMs = 150;
        config.ChordWindowMs = 151;
        Require(config.Validate().Count > 0, "chord window cannot exceed long-press threshold");
        Count++;
    }

    public void SnapshotReportsPendingPressAndIgnoresRepeats()
    {
        var config = HandheldActionConfiguration.CreateDefault();
        config.FixedSlots["L2tap"] = ActionBinding.Menu(MenuAction.OpenInventory);
        var engine = new HandheldActionEngine(config);
        engine.Press(HandheldButton.L2, 10);
        engine.Press(HandheldButton.L2, 20); // OS repeat
        var snapshot = engine.Snapshot(310);
        Require(snapshot.Pressed.Contains(HandheldButton.L2), "snapshot reports physical pressed state");
        Require(snapshot.Pending.Contains(HandheldButton.L2), "snapshot reports unresolved tap or hold decision");
        Require(Math.Abs(snapshot.LongPressProgress - 0.5) < 0.0001, "snapshot exposes normalized long-press progress");
        engine.Release(HandheldButton.L2, 320);
        Require(engine.DrainIntents().Count == 1, "repeated press event does not duplicate action");
        bool monotonicRejected = false;
        try { engine.Tick(319); } catch (ArgumentOutOfRangeException) { monotonicRejected = true; }
        Require(monotonicRejected, "non-monotonic caller time is rejected");
        Count++;
    }

    public void DueThresholdCommitsBeforeLaterInput()
    {
        var config = HandheldActionConfiguration.CreateDefault();
        config.LongPressThresholdMs = 100;
        config.ChordWindowMs = 100;
        config.FixedSlots["L2hold"] = ActionBinding.Menu(MenuAction.OpenSystem);
        config.FixedSlots["R2hold"] = ActionBinding.Menu(MenuAction.OpenInventory);
        config.FixedSlots["L2+R2"] = ActionBinding.Menu(MenuAction.OpenQuests);
        var engine = new HandheldActionEngine(config);
        engine.Press(HandheldButton.L2, 0);
        engine.Release(HandheldButton.L2, 100);
        Require(engine.DrainIntents().Single().Trigger == HandheldTrigger.Hold, "release after threshold commits hold even without an intervening tick");
        engine.Press(HandheldButton.L2, 200);
        engine.Tick(300);
        Require(engine.DrainIntents().Single().MenuAction == MenuAction.OpenSystem, "single trigger commits its hold at the threshold");
        engine.Press(HandheldButton.R2, 300);
        Require(engine.DrainIntents().Count == 0, "later partner press cannot add a chord after hold commitment");
        engine.Tick(400);
        Require(engine.DrainIntents().Single().MenuAction == MenuAction.OpenInventory, "unpaired late R2 can still commit its own hold");
        engine.Release(HandheldButton.L2, 410);
        engine.Release(HandheldButton.R2, 411);
        Count++;
    }

    public void TakeoverClearsQueuedIntents()
    {
        var config = HandheldActionConfiguration.CreateDefault();
        config.Pages[0].Bindings[HandheldButton.Up] = ActionBinding.Menu(MenuAction.OpenInventory);
        var engine = new HandheldActionEngine(config);
        engine.Press(HandheldButton.Up, 0);
        engine.SetMenuOwnsInput(true, 1);
        Require(engine.DrainIntents().Count == 0, "menu drops already queued intents");
        engine.SetMenuOwnsInput(false, 2);
        engine.Press(HandheldButton.Up, 3);
        Require(engine.DrainIntents().Count == 0, "held direction remains disarmed after closing menu");
        engine.Release(HandheldButton.Up, 4);
        engine.Press(HandheldButton.Up, 5);
        Require(engine.DrainIntents().Count == 1, "direction rearms on release");
        engine.Release(HandheldButton.Up, 6);
        engine.Press(HandheldButton.Up, 7);
        engine.FocusLost(8);
        Require(engine.DrainIntents().Count == 0, "focus loss drops already queued intents");
        Count++;
    }

    public void ChordCannotReuseSuppressedPartner()
    {
        var config = HandheldActionConfiguration.CreateDefault();
        config.FixedSlots["L1+R1"] = ActionBinding.Menu(MenuAction.OpenSystem);
        var engine = new HandheldActionEngine(config);
        engine.Press(HandheldButton.L1, 0);
        engine.Press(HandheldButton.R1, 10);
        Require(engine.DrainIntents().Count == 1, "first overlap emits chord");
        engine.Release(HandheldButton.L1, 20);
        engine.Press(HandheldButton.L1, 30);
        Require(engine.DrainIntents().Count == 0, "held shoulder already used by a chord cannot pair again");
        engine.Release(HandheldButton.L1, 31);
        engine.Release(HandheldButton.R1, 32);
        Require(engine.Snapshot(32).CurrentPage == 0, "re-pressing a chord member cannot leak a suppressed single page switch");
        Count++;
    }

    public void ChordWinsExactSharedThresholdBoundary()
    {
        var config = HandheldActionConfiguration.CreateDefault();
        config.LongPressThresholdMs = 100;
        config.ChordWindowMs = 100;
        config.FixedSlots["L2hold"] = ActionBinding.Menu(MenuAction.OpenSystem);
        config.FixedSlots["L2+R2"] = ActionBinding.Menu(MenuAction.OpenMap);
        var engine = new HandheldActionEngine(config);
        engine.Press(HandheldButton.L2, 0);
        engine.Press(HandheldButton.R2, 100);
        var intents = engine.DrainIntents();
        Require(intents.Count == 1 && intents[0].MenuAction == MenuAction.OpenMap,
            "simultaneous chord window and hold threshold give the inclusive chord precedence");
        engine.Release(HandheldButton.L2, 110);
        engine.Release(HandheldButton.R2, 111);
        Require(engine.DrainIntents().Count == 0, "exact-boundary chord suppresses both trigger releases");
        Count++;
    }

    public void ReloadCancelsPendingAndPreservesReleaseGates()
    {
        var config = HandheldActionConfiguration.CreateDefault();
        config.Pages[0].Bindings[HandheldButton.Up] = ActionBinding.Menu(MenuAction.OpenInventory);
        var engine = new HandheldActionEngine(config);
        engine.Press(HandheldButton.Up, 0);
        engine.Press(HandheldButton.L2, 1);
        engine.Reload(config);
        Require(engine.DrainIntents().Count == 0, "reloading discards queued intents");
        engine.Press(HandheldButton.Up, 10);
        Require(engine.DrainIntents().Count == 0, "reloading preserves blocked held directions");
        engine.Release(HandheldButton.Up, 11);
        engine.Release(HandheldButton.L2, 12);
        engine.Press(HandheldButton.Up, 13);
        Require(engine.DrainIntents().Count == 1, "released keys use reloaded bindings");
        Count++;
    }

    public void MenuActionsAreBoundedAndRoundTrip()
    {
        var config = HandheldActionConfiguration.CreateDefault();
        config.FixedSlots["L1+R1"] = ActionBinding.Menu(MenuAction.OpenMap);
        config.Pages[1].Bindings[HandheldButton.X] = ActionBinding.Menu(MenuAction.FirstPage);
        string path = Path.Combine(Path.GetTempPath(), "handheld-actions-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Require(HandheldActionConfigurationStore.Save(path, config).Success, "bounded map and first page actions save");
            var loaded = HandheldActionConfigurationStore.Load(path);
            Require(loaded.FixedSlots["L1+R1"].MenuAction == MenuAction.OpenMap, "map binding round-trips");
            var engine = new HandheldActionEngine(loaded);
            engine.Press(HandheldButton.L1, 0);
            engine.Release(HandheldButton.L1, 1);
            engine.Press(HandheldButton.X, 2);
            Require(engine.DrainIntents().Single().MenuAction == MenuAction.FirstPage, "binding emits first page intent");
            engine.FirstPage();
            Require(engine.Snapshot(2).CurrentPage == 0, "first page intent can reset current page");
            config.FixedSlots["L1+R1"] = new() { Kind = ActionKind.Menu, MenuAction = MenuAction.OpenMap, StableId = "arbitrary command" };
            Require(!HandheldActionConfigurationStore.Save(path, config).Success, "menu may not carry an arbitrary payload");
            File.WriteAllText(path, "{}");
            try { _ = HandheldActionConfigurationStore.Load(path); throw new InvalidOperationException("missing schema unexpectedly loaded"); }
            catch (System.Text.Json.JsonException) { }
        }
        finally { File.Delete(path); }
        Count++;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
