namespace Ct007.Tests.FrontendTests;

/// <summary>FE-009…FE-018, FE-040: drillhole list, offline cache, search and selection (F01–F03).</summary>
public static class DrillholeSelectionTests
{
    private static Task Seed(string databasePath, params string[] ids) =>
        new DrillholeCache(databasePath).ReplaceAsync(ids.Select(id => new DrillholeResponse(id, "Cached", "1999")).ToList());

    [Test("FE-009", "The cached list shows first, then the server list replaces list and cache")]
    public static async Task CacheThenServer()
    {
        using var folder = new TestFolder();
        var db = folder.Path("m.db");
        await Seed(db, "DH-OLD");
        var backend = new StubBackend
        {
            HoldDrillholes = new TaskCompletionSource(),
            DrillholesJson = """[{"holeId":"DH-001","projectCode":"P","campaign":"1"},{"holeId":"DH-002","projectCode":"P","campaign":"2"}]"""
        };

        var form = Make.CoreForm(backend, db, new MemoryRepository(db), out _);
        await Assert.Eventually(() => Make.Ids(form).SequenceEqual(new[] { "DH-OLD" }), "cached list shown before the response");

        backend.HoldDrillholes!.SetResult();
        await form.LoadDrillholesAsync();

        Assert.Sequence(new[] { "DH-001", "DH-002" }, Make.Ids(form), "server list");
        Assert.Sequence(new[] { "DH-001", "DH-002" }, (await new DrillholeCache(db).LoadAsync()).Select(d => d.HoleId).Order(), "cache");
        Assert.Equal("", form.DrillholesMessage, "message");
        Assert.False(form.CanRetryDrillholes, "Retry offered");
    }

    [Test("FE-010", "A failed refresh keeps the list, the pick and the form values")]
    public static async Task FailedRefreshKeepsState()
    {
        using var folder = new TestFolder();
        var db = folder.Path("m.db");
        var backend = new StubBackend();
        var form = Make.CoreForm(backend, db, new MemoryRepository(db), out _);
        await form.LoadDrillholesAsync();
        await Make.Pick(form, "DH-001");
        form.Operator = "A";
        form.Notes = "N";
        var cached = (await new DrillholeCache(db).LoadAsync()).Count;

        backend.DrillholesStatus = HttpStatusCode.ServiceUnavailable;
        await form.LoadDrillholesAsync();
        Assert.True(form.DrillholesMessage.StartsWith("Showing the saved drillhole list. Could not refresh:", StringComparison.Ordinal),
            $"503 message ({form.DrillholesMessage})");
        Assert.Contains("503", form.DrillholesMessage, "503 message");
        Assert.True(form.CanRetryDrillholes, "Retry offered after 503");

        backend.Offline = true;
        await form.LoadDrillholesAsync();
        Assert.Contains("Backend is offline.", form.DrillholesMessage, "offline message");

        Assert.Sequence(new[] { "DH-001", "DH-002", "DH-003" }, Make.Ids(form), "options");
        Assert.Equal("DH-001", form.DrillholeId, "pick");
        Assert.Equal("North Ridge", form.DrillholeName, "Project");
        Assert.Equal("2024", form.ProjectArea, "Campaign");
        Assert.Equal("A", form.Operator, "Operator");
        Assert.Equal("N", form.Notes, "Notes");
        Assert.Equal(cached, (await new DrillholeCache(db).LoadAsync()).Count, "cache unchanged");
    }

    [Test("FE-011", "An empty server list never replaces a non-empty list")]
    public static async Task EmptyListKeepsSaved()
    {
        using var folder = new TestFolder();
        var db = folder.Path("m.db");
        await Seed(db, "A-1", "A-2", "A-3");
        var form = Make.CoreForm(new StubBackend { DrillholesJson = "[]" }, db, new MemoryRepository(db), out _);
        await form.LoadDrillholesAsync();

        Assert.Sequence(new[] { "A-1", "A-2", "A-3" }, Make.Ids(form), "saved list kept");
        Assert.Equal("The backend returned no drillholes; showing the saved list.", form.DrillholesMessage, "message");
        Assert.Equal(3, (await new DrillholeCache(db).LoadAsync()).Count, "cache kept");

        var empty = folder.Path("empty.db");
        var bare = Make.CoreForm(new StubBackend { DrillholesJson = "[]" }, empty, new MemoryRepository(empty), out _);
        await bare.LoadDrillholesAsync();
        Assert.Equal(0, Make.Ids(bare).Length, "no entries without a saved list");
        Assert.Equal("The backend returned no drillholes.", bare.DrillholesMessage, "message without a saved list");
    }

    [Test("FE-012", "Overlapping load requests share one request")]
    public static async Task LoadIsReused()
    {
        using var folder = new TestFolder();
        var db = folder.Path("m.db");
        var backend = new StubBackend { HoldDrillholes = new TaskCompletionSource() };
        var form = Make.CoreForm(backend, db, new MemoryRepository(db), out _);
        await Assert.Eventually(() => backend.Calls(DrillholesClient.Path) == 1, "first request sent");

        var a = form.LoadDrillholesAsync();
        form.RefreshDrillholesCommand.Execute(null);
        var b = form.LoadDrillholesAsync();
        Assert.True(ReferenceEquals(a, b), "the running load is returned");

        backend.HoldDrillholes!.SetResult();
        await a;
        Assert.Equal(1, backend.Calls(DrillholesClient.Path), "drillhole requests");
    }

    [Test("FE-013", "Catalog building drops empty IDs, trims and de-duplicates case-insensitively")]
    public static void CatalogCleaning()
    {
        var catalog = DrillholeCatalog.Build(
        [
            new DrillholeResponse("", "x", "x"),
            new DrillholeResponse("   ", "x", "x"),
            new DrillholeResponse(null, "x", "x"),
            new DrillholeResponse(" DH-1 ", null, null),
            new DrillholeResponse("dh-2", "first", "1"),
            new DrillholeResponse("DH-2", "second", "2")
        ], null);

        Assert.Sequence(new[] { "DH-1", "dh-2" }, catalog.All.Select(d => d.HoleId), "usable entries");
        Assert.Equal("first", catalog.Find("DH-2")!.ProjectCode, "first duplicate kept, found case-insensitively");
        Assert.Equal("DH-1", catalog.Find(" DH-1 ")!.HoleId, "trimmed lookup");
    }

    [Test("FE-014", "Search ranks exact, prefix, partial; Measured group first and newest first")]
    public static void RankingAndGroups()
    {
        var catalog = DrillholeCatalog.Build(new[] { "AB1", "XAB", "AB", "ab2", "ZZ" }.Select(id => new DrillholeResponse(id, "P", "C")), null);
        var history = new[]
        {
            new MeasuredDrillhole("ZZ", new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero)),
            new MeasuredDrillhole("XAB", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)),
            new MeasuredDrillhole("GONE", new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero))
        };

        var search = catalog.Options(history, "ab", "");
        Assert.Sequence(new[] { "Measured drillholes", "XAB", "All drillholes", "AB", "AB1", "ab2" },
            search.Entries.Select(o => o.IsHeader ? o.Header : o.HoleId), "search 'ab'");
        Assert.Equal("AB", search.Closest?.HoleId, "closest match");

        // Several equally good matches: the closest is the one listed first ("AB" before "AB1" and "ab2").
        Assert.Equal("AB", catalog.Options(history, "a", "").Closest?.HoleId, "tie between prefix matches");

        var all = catalog.Options(history, "", "");
        Assert.Sequence(new[] { "Measured drillholes", "ZZ", "XAB", "All drillholes", "AB", "AB1", "ab2" },
            all.Entries.Select(o => o.IsHeader ? o.Header : o.HoleId), "empty query");
        Assert.True(all.Closest is null, "no closest match without a query");
    }

    [Test("FE-015", "Typing never picks; a mismatch clears the pick; the rebuild echo is ignored")]
    public static async Task NoImplicitPick()
    {
        using var folder = new TestFolder();
        var db = folder.Path("m.db");
        var backend = new StubBackend();
        var form = Make.CoreForm(backend, db, new MemoryRepository(db), out _);
        await form.LoadDrillholesAsync();

        form.DrillholeSearch = "DH-001";
        await form.WhenOptionsUpdated();
        Assert.Equal("", form.DrillholeId, "exact text alone does not pick");

        await Make.Pick(form, "DH-001");
        Assert.Equal("DH-001", form.DrillholeId, "Enter picks");
        form.DrillholeSearch = "DH-0019";
        await form.WhenOptionsUpdated();
        Assert.Equal("", form.DrillholeId, "pick cleared by non-matching text");
        Assert.Equal("", form.DrillholeName, "Project cleared");
        Assert.Equal("", form.ProjectArea, "Campaign cleared");

        // The ComboBox writes "" into its Text while its items are replaced; that echo must not clear the pick.
        await Make.Pick(form, "DH-002");
        form.DrillholeOptions.CollectionChanged += (_, _) => form.DrillholeSearch = "";
        backend.DrillholesJson = """[{"holeId":"DH-002","projectCode":"North Ridge","campaign":"2025"},{"holeId":"DH-009","projectCode":"X","campaign":"Y"}]""";
        await form.LoadDrillholesAsync();
        Assert.Equal("DH-002", form.DrillholeId, "pick kept through the list rebuild");
        Assert.Equal("DH-002", form.DrillholeSearch, "text kept through the list rebuild");
    }

    [Test("FE-016", "Debounced search applies only the latest text")]
    public static async Task DebounceAndStaleResults()
    {
        using var folder = new TestFolder();
        var db = folder.Path("m.db");
        var ids = Enumerable.Range(1, 20_000).Select(i => $"DH-{i:D5}").ToList();
        var backend = new StubBackend { DrillholesJson = System.Text.Json.JsonSerializer.Serialize(ids.Select(id => new { holeId = id, projectCode = "P", campaign = "C" })) };
        var form = Make.CoreForm(backend, db, new MemoryRepository(db), out _, TimeSpan.FromMilliseconds(150));
        await form.LoadDrillholesAsync();

        foreach (var text in new[] { "D", "DH", "DH-1", "DH-19" })
        {
            form.DrillholeSearch = text;
            await Task.Delay(30);
        }

        await form.WhenOptionsUpdated();
        var expected = ids.Where(id => id.Contains("DH-19", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Sequence(expected, Make.Ids(form), "options for the final text");
        var snapshot = Make.Ids(form);
        await Assert.Remains(() => Make.Ids(form).SequenceEqual(snapshot), "no older result applied afterwards", 500);
    }

    [Test("FE-018", "Unicode and special characters: searchable, pickable, cached unchanged")]
    public static async Task UnicodeIds()
    {
        using var folder = new TestFolder();
        var db = folder.Path("m.db");
        var ids = new[] { "ДН-001", "孔-7", "A/B 01", "O'Brien-1", "DH\"Q\"", "Ä-1" };
        var backend = new StubBackend
        {
            DrillholesJson = System.Text.Json.JsonSerializer.Serialize(ids.Select(id => new { holeId = id, projectCode = "Проект", campaign = "2024" }))
        };
        var form = Make.CoreForm(backend, db, new MemoryRepository(db), out _);
        await form.LoadDrillholesAsync();

        form.DrillholeSearch = "дн";
        await form.WhenOptionsUpdated();
        Assert.Sequence(new[] { "ДН-001" }, Make.Ids(form), "case-insensitive Cyrillic substring");

        foreach (var id in ids)
        {
            await Make.Pick(form, id);
            Assert.Equal(id, form.DrillholeId, $"picked {id}");
        }

        Assert.Sequence(ids.Order(StringComparer.Ordinal), (await new DrillholeCache(db).LoadAsync()).Select(d => d.HoleId!).Order(StringComparer.Ordinal),
            "cache round-trip");
        Assert.Equal("Проект", form.DrillholeName, "Unicode project");
    }

    [Test("FE-040", "A saved measurement moves its drillhole to the top of Measured drillholes")]
    public static async Task MeasuredGroupRefresh()
    {
        using var folder = new TestFolder();
        var repository = new MemoryRepository(folder.Path("m.db"));
        var device = new ControlledMeasurements();
        using var main = Make.Shell(repository, new StubBackend(), device, folder.Path("gg.sqlite"));
        await main.Core.LoadDrillholesAsync();

        await Make.Pick(main.Core, "DH-003");
        main.Core.SaveCommand.Execute(null);
        await Make.Arm(main.Measurement, CoreFrom(main.Core));
        await Make.Capture(main.Measurement, device);
        await Assert.Eventually(() => repository.Rows(main.Measurement.SessionId!.Value).Count == 1, "row saved");

        // No explicit refresh: MainViewModel wires MeasurementsSaved to the Core page. The search text equals the
        // picked ID, so the whole list (both groups) is shown.
        string[] Leading() => main.Core.DrillholeOptions.Take(2).Select(o => o.IsHeader ? o.Header : o.HoleId).ToArray();
        await Assert.Eventually(() => Leading().SequenceEqual(new[] { "Measured drillholes", "DH-003" }),
            "DH-003 heads Measured drillholes after its first saved capture");
    }

    private static CoreSnapshot CoreFrom(CoreInformationViewModel form) =>
        new(form.DrillholeId, form.DrillholeName, form.ProjectArea, form.Operator.Trim(), form.Notes.Trim());
}
