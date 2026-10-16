using System.Diagnostics;

namespace Ct007.Tests.Fixtures;

public sealed class AssertionException(string message) : Exception(message);

/// <summary>Thrown by <see cref="Assert.Skip"/>: the test could not run here (missing opt-in environment).</summary>
public sealed class SkipException(string message) : Exception(message);

public static class Assert
{
    public static void True(bool condition, string what)
    {
        if (!condition)
        {
            throw new AssertionException(what);
        }
    }

    public static void False(bool condition, string what) => True(!condition, what);

    public static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new AssertionException($"{what}: expected <{expected}>, actual <{actual}>");
        }
    }

    public static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual, string what)
    {
        var e = expected.ToList();
        var a = actual.ToList();

        if (!e.SequenceEqual(a))
        {
            throw new AssertionException($"{what}: expected [{string.Join(", ", e)}], actual [{string.Join(", ", a)}]");
        }
    }

    public static void Near(double expected, double? actual, double tolerance, string what)
    {
        if (actual is not { } value || Math.Abs(value - expected) > tolerance)
        {
            throw new AssertionException($"{what}: expected {expected} ± {tolerance}, actual {actual?.ToString("R", CultureInfo.InvariantCulture) ?? "null"}");
        }
    }

    public static void Contains(string part, string? actual, string what)
    {
        if (actual is null || !actual.Contains(part, StringComparison.Ordinal))
        {
            throw new AssertionException($"{what}: expected text containing <{part}>, actual <{actual}>");
        }
    }

    public static T Throws<T>(Action action, string what) where T : Exception
    {
        try
        {
            action();
        }
        catch (T expected)
        {
            return expected;
        }
        catch (Exception other)
        {
            throw new AssertionException($"{what}: expected {typeof(T).Name}, got {other.GetType().Name}: {other.Message}");
        }

        throw new AssertionException($"{what}: expected {typeof(T).Name}, nothing was thrown");
    }

    public static async Task<T> ThrowsAsync<T>(Func<Task> action, string what) where T : Exception
    {
        try
        {
            await action();
        }
        catch (T expected)
        {
            return expected;
        }
        catch (Exception other)
        {
            throw new AssertionException($"{what}: expected {typeof(T).Name}, got {other.GetType().Name}: {other.Message}");
        }

        throw new AssertionException($"{what}: expected {typeof(T).Name}, nothing was thrown");
    }

    /// <summary>
    /// Waits (yielding to the dispatcher, so UI continuations run) until <paramref name="condition"/> holds.
    /// Used for the app's fire-and-forget async work; the timeout only bounds a failing test.
    /// </summary>
    public static async Task Eventually(Func<bool> condition, string what, int timeoutMs = 3000)
    {
        var watch = Stopwatch.StartNew();

        while (!condition())
        {
            if (watch.ElapsedMilliseconds > timeoutMs)
            {
                throw new AssertionException($"Not reached within {timeoutMs} ms: {what}");
            }

            await Task.Delay(5);
        }
    }

    /// <summary>Checks that <paramref name="condition"/> stays true for <paramref name="durationMs"/> (no late change).</summary>
    public static async Task Remains(Func<bool> condition, string what, int durationMs = 300)
    {
        var watch = Stopwatch.StartNew();

        while (watch.ElapsedMilliseconds < durationMs)
        {
            True(condition(), what);
            await Task.Delay(10);
        }

        True(condition(), what);
    }

    public static void Skip(string reason) => throw new SkipException(reason);
}
