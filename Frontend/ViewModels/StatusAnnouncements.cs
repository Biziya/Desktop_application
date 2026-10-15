using System.ComponentModel;
using Frontend.Api;
using Frontend.Calculation.CoreInformation;
using Frontend.Shared.Infrastructure;

namespace Frontend.ViewModels;

/// <summary>
/// Turns connection changes into toasts — once per transition, never per status check (a check that finds the same
/// state says nothing). The backend (sidebar Online/Offline) and the CT-007 Bluetooth device are announced separately
/// and named differently, so the two are never confused. The first result after start-up is not announced, except a
/// backend that is offline from the start.
/// </summary>
public sealed class StatusAnnouncements : IDisposable
{
    private readonly BackendConnection backend;
    private readonly DeviceStatusMonitor devices;
    private readonly ToastService toasts;
    private BackendState backendState;
    private readonly Dictionary<string, LocalState> deviceStates = new();

    public StatusAnnouncements(BackendConnection backend, DeviceStatusMonitor devices, ToastService toasts)
    {
        this.backend = backend;
        this.devices = devices;
        this.toasts = toasts;
        backendState = backend.State;
        backend.PropertyChanged += OnBackendChanged;
        devices.PropertyChanged += OnDevicesChanged;
    }

    public void Dispose()
    {
        backend.PropertyChanged -= OnBackendChanged;
        devices.PropertyChanged -= OnDevicesChanged;
    }

    private void OnBackendChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BackendConnection.State) || backend.State == backendState)
        {
            return;
        }

        var previous = backendState;
        backendState = backend.State;

        if (backendState == BackendState.Offline)
        {
            toasts.Warning("Backend offline. The saved drillhole list stays available.");
        }
        else if (backendState == BackendState.Online && previous == BackendState.Offline)
        {
            toasts.Success("Backend online again.");
        }
    }

    private void OnDevicesChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DeviceStatusMonitor.Bluetooth):
                Announce("CT-007 device (Bluetooth)", devices.Bluetooth);
                break;
            case nameof(DeviceStatusMonitor.MeasurementDatabase):
                Announce("Measurement database", devices.MeasurementDatabase);
                break;
            case nameof(DeviceStatusMonitor.GammaGuardDatabase):
                Announce("Gamma Guard database", devices.GammaGuardDatabase);
                break;
        }
    }

    private void Announce(string name, LocalStatus status)
    {
        if (status.State == LocalState.Checking)
        {
            return;
        }

        var known = deviceStates.TryGetValue(name, out var previous);
        deviceStates[name] = status.State;

        if (!known || previous == status.State)
        {
            return;
        }

        switch (status.State)
        {
            case LocalState.Connected:
                toasts.Success($"{name} connected: {status.Detail}");
                break;
            case LocalState.Available:
                toasts.Information($"{name} available.");
                break;
            case LocalState.Disconnected:
                toasts.Warning($"{name} disconnected.");
                break;
            default:
                toasts.Warning($"{name}: {status.Text.ToLowerInvariant()}.");
                break;
        }
    }
}
