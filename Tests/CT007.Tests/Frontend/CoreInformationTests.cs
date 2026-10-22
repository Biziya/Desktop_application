namespace Ct007.Tests.FrontendTests;

/// <summary>FE-019…FE-023: Core Information form, Save &amp; Continue, cross-page wiring, navigation (F04, F10).</summary>
public static class CoreInformationTests
{
    private static async Task<(MainViewModel Main, MemoryRepository Repository, ControlledMeasurements Device, StubBackend Backend)> Start(
        TestFolder folder, TimeSpan? statusInterval = null)
    {
        var repository = new MemoryRepository(folder.Path("m.db"));
        var device = new ControlledMeasurements();
        var backend = new StubBackend();
        var main = Make.Shell(repository, backend, device, folder.Path("gg.sqlite"), statusInterval);
        await main.Core.LoadDrillholesAsync();
        return (main, repository, device, backend);
    }

    [Test("FE-019", "Save without a picked drillhole is refused with one error toast")]
    public static async Task SaveWithoutPick()
    {
        using var folder = new TestFolder();
        var (main, repository, _, _) = await Start(folder);
        using var _main = main;

        main.Core.SaveCommand.Execute(null);
        Assert.True(main.Core.HasDrillholeError, "red border");
        Assert.Equal(1, main.Toasts.Items.Count(t => t is { Kind: ToastKind.Error, Message: "Select a drillhole from the list." }), "one error toast");
        Assert.Equal(ToastService.ErrorDuration, main.Toasts.Items.First(t => t.Kind == ToastKind.Error).Duration, "error toast lasts 5 s");
        Assert.True(main.IsCoreSelected, "stays on Core Information");

        main.Core.DrillholeSearch = "DH-001";
        await main.Core.WhenOptionsUpdated();
        main.Core.SaveCommand.Execute(null);
        Assert.True(main.IsCoreSelected, "typed but unpicked text is not saved");
        Assert.Equal(0, repository.SessionCount, "no session resolved");
    }

    [Test("FE-020", "Save & Continue trims the values, autofills an empty Operator and opens Measurement")]
    public static async Task SaveAndContinue()
    {
        using var folder = new TestFolder();
        var (main, repository, _, _) = await Start(folder);
        using var _main = main;

        await Make.Pick(main.Core, "DH-001");
        main.Core.Operator = "  ";
        main.Core.Notes = "  note  ";
        main.Core.SaveCommand.Execute(null);

        Assert.True(main.IsMeasurementSelected, "navigated to Measurement");
        await Assert.Eventually(() => repository.Session("DH-001") is not null, "session resolved");
        var session = repository.Session("DH-001")!;
        Assert.Equal(WindowsAccount.Current(), session.Operator, "Operator autofilled with the Windows account");
        Assert.Equal("note", session.Comment, "Notes trimmed");
        Assert.Equal("North Ridge", session.DrillholeName, "Project");
        Assert.Equal("2024", session.Project, "Campaign");
        Assert.Equal(1, repository.SessionCount, "one session");

        main.ShowCoreCommand.Execute(null);
        await Make.Pick(main.Core, "DH-002");
        main.Core.Operator = " Bob ";
        main.Core.SaveCommand.Execute(null);
        await Assert.Eventually(() => repository.Session("DH-002") is not null, "second session resolved");
        Assert.Equal("Bob", repository.Session("DH-002")!.Operator, "typed Operator trimmed");
    }

    [Test("FE-021", "Editing Notes after saving disarms measurement (UR-09)")]
    public static async Task EditDisarms()
    {
        using var folder = new TestFolder();
        var (main, _, device, _) = await Start(folder);
        using var _main = main;
        await Make.Pick(main.Core, "DH-001");
        main.Core.SaveCommand.Execute(null);
        await Assert.Eventually(() => main.Measurement.SessionId is not null && main.Measurement.ToggleMeasurementCommand.CanExecute(null), "session open");
        main.Measurement.NextFrom = "10.00";
        main.Measurement.Step = "25";
        main.Measurement.ToggleMeasurementCommand.Execute(null);
        main.Measurement.TriggerCommand.Execute(DateTimeOffset.Now);
        Assert.Equal(MeasurementState.Capturing, main.Measurement.State, "capturing");

        main.Core.Notes = "x";

        Assert.Equal(MeasurementState.Idle, main.Measurement.State, "disarmed");
        Assert.True(main.Measurement.NeedsCore, "Save & Continue needed again");
        Assert.False(main.Measurement.ToggleMeasurementCommand.CanExecute(null), "Start unavailable");
        device.Complete();
        await Assert.Remains(() => main.Measurement.Results.Count == 0, "the cancelled capture adds no row");
    }

    [Test("FE-022", "The drillhole cannot change while measuring")]
    public static async Task SelectionLock()
    {
        using var folder = new TestFolder();
        var (main, _, _, _) = await Start(folder);
        using var _main = main;
        await Make.Pick(main.Core, "DH-001");
        main.Core.SaveCommand.Execute(null);
        await Assert.Eventually(() => main.Measurement.ToggleMeasurementCommand.CanExecute(null), "session open");
        main.Measurement.NextFrom = "10.00";
        main.Measurement.Step = "25";
        main.Measurement.ToggleMeasurementCommand.Execute(null);
        Assert.True(main.Core.IsSelectionLocked, "selection locked while armed");

        main.Core.DrillholeSearch = "DH-002";
        Assert.Equal("DH-001", main.Core.DrillholeSearch, "typing another ID is rejected");
        Assert.True(Make.Toasted(main.Toasts, ToastKind.Error, "Stop the measurement before changing the drillhole."), "error toast for typing");
        main.Toasts.Items.Clear();

        // The search text equals the pick, so the dropdown lists every drillhole; picking another one is rejected too.
        main.Core.PickedDrillhole = main.Core.DrillholeOptions.First(o => o.HoleId == "DH-002");

        Assert.True(Make.Toasted(main.Toasts, ToastKind.Error, "Stop the measurement before changing the drillhole."), "error toast for picking");
        Assert.Equal("DH-001", main.Core.DrillholeId, "drillhole unchanged");
        Assert.Equal(MeasurementState.Ready, main.Measurement.State, "still armed");

        main.Measurement.ToggleMeasurementCommand.Execute(null);
        Assert.False(main.Core.IsSelectionLocked, "unlocked after Stop");
        await Make.Pick(main.Core, "DH-002");
        Assert.Equal("DH-002", main.Core.DrillholeId, "change accepted after Stop");
    }

    [Test("FE-023", "Navigation keeps state and does not multiply background polling", Category = "Timing")]
    public static async Task NavigationKeepsState()
    {
        using var folder = new TestFolder();
        var (main, _, _, backend) = await Start(folder, TimeSpan.FromMilliseconds(50));
        using var _main = main;
        await Make.Pick(main.Core, "DH-001");
        main.Core.Notes = "kept";

        var before = backend.Calls("/");
        await Task.Delay(500);
        var baseline = backend.Calls("/") - before;

        for (var i = 0; i < 5; i++)
        {
            main.ShowMeasurementCommand.Execute(null);
            Assert.True(main.IsMeasurementSelected && !main.IsCoreSelected, "Measurement selected");
            main.ShowCoreCommand.Execute(null);
            Assert.True(main.IsCoreSelected && !main.IsMeasurementSelected, "Core selected");
        }

        main.ToggleSidebarCommand.Execute(null);
        Assert.Equal("Expand sidebar", main.SidebarToggleLabel, "collapsed");
        main.ToggleSidebarCommand.Execute(null);
        Assert.Equal("Collapse sidebar", main.SidebarToggleLabel, "expanded");

        var start = backend.Calls("/");
        await Task.Delay(500);
        var after = backend.Calls("/") - start;
        Assert.True(after <= baseline * 1.5 + 2, $"probe rate unchanged by navigation ({baseline} → {after} per 500 ms)");
        Assert.Equal("DH-001", main.Core.DrillholeId, "pick kept");
        Assert.Equal("kept", main.Core.Notes, "Notes kept");
    }
}
