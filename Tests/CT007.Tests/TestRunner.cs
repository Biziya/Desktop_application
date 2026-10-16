using System.Diagnostics;
using System.Reflection;
using System.Windows.Threading;

namespace Ct007.Tests;

/// <summary>Marks a test. <paramref name="id"/> is the scenario ID in qa/planning/test-inventory.md.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute(string id, string title) : Attribute
{
    public string Id { get; } = id;

    public string Title { get; } = title;

    /// <summary>Opt-in group: Timing (real-time waits), Sql (needs CT007_TEST_SQL), E2E (needs a dedicated Windows
    /// account). Such tests run only when named with --include.</summary>
    public string? Category { get; set; }

    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// Runs every [Test] method on an STA thread with a WPF dispatcher (ViewModels continue on the UI context, as in the
/// app), each independently: one PASS/FAIL/SKIP line per test, exit code 1 when any test failed.
/// Arguments: ID prefixes to select (e.g. "FE-0" "BE-020"), --include=Timing,Sql,E2E, --results=file.csv.
/// </summary>
public static class TestRunner
{
    [STAThread]
    public static int Main(string[] args)
    {
        var prefixes = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        var included = Option(args, "--include")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        var resultsPath = Option(args, "--results");

        var tests = typeof(TestRunner).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Select(m => (Method: m, Info: m.GetCustomAttribute<TestAttribute>()!))
            .Where(t => t.Info is not null)
            .Where(t => prefixes.Length == 0 || prefixes.Any(p => t.Info.Id.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(t => t.Info.Id, StringComparer.Ordinal)
            .ThenBy(t => t.Method.Name, StringComparer.Ordinal)
            .ToList();

        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        var results = new List<(string Id, string Name, string Outcome, long Ms, string Message)>();

        dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                foreach (var (method, info) in tests)
                {
                    results.Add(await RunAsync(method, info, included));
                }
            }
            finally
            {
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
            }
        }));
        Dispatcher.Run();

        var leftovers = Fixtures.TestFolder.DeleteLeftovers();
        var failed = results.Count(r => r.Outcome == "FAIL");
        Console.WriteLine($"{results.Count} tests: {results.Count(r => r.Outcome == "PASS")} passed, {failed} failed, " +
                          $"{results.Count(r => r.Outcome == "SKIP")} skipped." +
                          (leftovers > 0 ? $" {leftovers} temp folder(s) could not be deleted." : ""));

        if (resultsPath is not null)
        {
            File.WriteAllLines(resultsPath, results.Select(r => string.Join(",",
                new[] { r.Id, r.Name, r.Outcome, r.Ms.ToString(CultureInfo.InvariantCulture), r.Message }
                    .Select(v => "\"" + v.Replace("\"", "\"\"").ReplaceLineEndings(" ") + "\""))).Prepend("id,test,outcome,ms,message"));
        }

        return failed == 0 ? 0 : 1;
    }

    private static async Task<(string, string, string, long, string)> RunAsync(MethodInfo method, TestAttribute info, string[] included)
    {
        var name = $"{method.DeclaringType!.Name}.{method.Name}";

        if (info.Category is { } category && !included.Contains(category, StringComparer.OrdinalIgnoreCase))
        {
            return Report(info.Id, name, "SKIP", 0, $"category {category} not included");
        }

        // Every test starts from the same culture; tests that change it restore it themselves, this is the safety net.
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        var watch = Stopwatch.StartNew();

        try
        {
            if (method.Invoke(null, null) is Task task)
            {
                if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(info.TimeoutSeconds))) != task)
                {
                    return Report(info.Id, name, "FAIL", watch.ElapsedMilliseconds, $"Timed out after {info.TimeoutSeconds} s");
                }

                await task;
            }

            return Report(info.Id, name, "PASS", watch.ElapsedMilliseconds, "");
        }
        catch (Exception error)
        {
            var actual = error is TargetInvocationException { InnerException: { } inner } ? inner : error;
            return actual is SkipException skip
                ? Report(info.Id, name, "SKIP", watch.ElapsedMilliseconds, skip.Message)
                : Report(info.Id, name, "FAIL", watch.ElapsedMilliseconds, $"{actual.GetType().Name}: {actual.Message}");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
    }

    private static (string, string, string, long, string) Report(string id, string name, string outcome, long ms, string message)
    {
        Console.WriteLine($"{outcome} {id} {name} ({ms} ms){(message.Length > 0 ? " — " + message : "")}");
        return (id, name, outcome, ms, message);
    }

    private static string? Option(string[] args, string name) =>
        args.FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))?[(name.Length + 1)..];
}
