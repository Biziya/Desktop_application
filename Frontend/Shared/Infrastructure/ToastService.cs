using System.Collections.ObjectModel;

namespace Frontend.Shared.Infrastructure;

public enum ToastKind
{
    Success,
    Information,
    Warning,
    Error
}

/// <summary>One toast: a short message with its styling kind and how long it stays.</summary>
public sealed class Toast(ToastKind kind, string message, TimeSpan duration)
{
    public ToastKind Kind { get; } = kind;

    public string Message { get; } = message;

    public TimeSpan Duration { get; } = duration;

    internal CancellationTokenSource Lifetime { get; } = new();
}

/// <summary>
/// The app's one toast list (shown top-right over both pages by MainWindow; it never blocks input or moves the
/// layout). Durations: success and information 3 s; warnings 3 s; errors — validation and other failures — 5 s.
/// A toast identical to one already shown is not added again (polling can never stack duplicates); it restarts the
/// visible one's timer instead. Every toast can be dismissed. Publishing happens on the caller's (UI) thread.
/// </summary>
public sealed class ToastService
{
    public static readonly TimeSpan ShortDuration = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan ErrorDuration = TimeSpan.FromSeconds(5);

    // At most this many toasts at once; the oldest goes first.
    private const int MaxVisible = 4;

    private readonly Func<TimeSpan, CancellationToken, Task> delay;

    /// <param name="delay">Waits a toast's duration (tests pass a controllable delay).</param>
    public ToastService(Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.delay = delay ?? Task.Delay;
        DismissCommand = new RelayCommand(parameter =>
        {
            if (parameter is Toast toast)
            {
                Dismiss(toast);
            }
        });
    }

    public ObservableCollection<Toast> Items { get; } = [];

    public RelayCommand DismissCommand { get; }

    public void Success(string message) => Show(ToastKind.Success, message);

    public void Information(string message) => Show(ToastKind.Information, message);

    public void Warning(string message) => Show(ToastKind.Warning, message);

    /// <summary>Validation errors and failed operations (5 s).</summary>
    public void Error(string message) => Show(ToastKind.Error, message);

    public static TimeSpan DurationOf(ToastKind kind) => kind == ToastKind.Error ? ErrorDuration : ShortDuration;

    public void Show(ToastKind kind, string message)
    {
        // The same message is already visible: keep one toast and give it its full time again.
        if (Items.FirstOrDefault(t => t.Kind == kind && t.Message == message) is { } shown)
        {
            Items.Remove(shown);
            shown.Lifetime.Cancel();
        }

        while (Items.Count >= MaxVisible)
        {
            Dismiss(Items[0]);
        }

        var toast = new Toast(kind, message, DurationOf(kind));
        Items.Add(toast);
        _ = ExpireAsync(toast);
    }

    public void Dismiss(Toast toast)
    {
        toast.Lifetime.Cancel();
        Items.Remove(toast);
    }

    private async Task ExpireAsync(Toast toast)
    {
        try
        {
            await delay(toast.Duration, toast.Lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!toast.Lifetime.IsCancellationRequested)
        {
            Items.Remove(toast);
        }
    }
}
