using System.Windows;

namespace Frontend.Pages.CoreInformation.DeviceStatus;

/// <summary>DeviceStatus.yaml: labels, sizes and spacing of the Device Status section.</summary>
public sealed class DeviceStatusSettings
{
    public const string File = "Pages/CoreInformation/DeviceStatus/DeviceStatus.yaml";

    public required LabelSettings Labels { get; init; }

    public required SizeSettings Sizes { get; init; }

    public required SpacingSettings Spacing { get; init; }

    /// <summary>The resources DeviceStatusView.xaml uses.</summary>
    public void AddTo(ResourceDictionary resources)
    {
        resources.Add("DeviceStatusTitle", Labels.SectionTitle);
        resources.Add("BluetoothLabel", Labels.Bluetooth);
        resources.Add("BluetoothDeviceToolTip", Labels.BluetoothDeviceToolTip);
        resources.Add("MeasurementDatabaseLabel", Labels.MeasurementDatabase);
        resources.Add("GammaGuardDatabaseLabel", Labels.GammaGuardDatabase);
        resources.Add("DeviceRetryLabel", Labels.Retry);
        resources.Add("SelectDatabaseLabel", Labels.SelectDatabase);
        resources.Add("ChangeDatabaseLabel", Labels.ChangeDatabase);
        resources.Add("SelectDatabaseToolTip", Labels.SelectDatabaseToolTip);
        resources.Add("DeviceStatusDot", Labels.StatusDot);

        resources.Add("BluetoothDropdownMaxWidth", Sizes.BluetoothDropdownMaxWidth);

        resources.Add("DeviceStatusTitleMargin", Spacing.Title);
        resources.Add("DeviceRowPadding", Spacing.RowPadding);
        resources.Add("DeviceRowMargin", Spacing.Row);
        resources.Add("DeviceRowLabelMargin", Spacing.LabelColumn);
        resources.Add("DeviceDetailMargin", Spacing.Detail);
        resources.Add("DeviceRetryMargin", Spacing.RetryButton);
    }

    public sealed class LabelSettings
    {
        public required string SectionTitle { get; init; }
        public required string Bluetooth { get; init; }
        public required string BluetoothDeviceToolTip { get; init; }
        public required string MeasurementDatabase { get; init; }
        public required string GammaGuardDatabase { get; init; }
        public required string Retry { get; init; }
        public required string SelectDatabase { get; init; }
        public required string ChangeDatabase { get; init; }
        public required string SelectDatabaseToolTip { get; init; }
        public required string StatusDot { get; init; }
    }

    public sealed class SizeSettings
    {
        public required double BluetoothDropdownMaxWidth { get; init; }
    }

    public sealed class SpacingSettings
    {
        public required Thickness Title { get; init; }
        public required Thickness RowPadding { get; init; }
        public required Thickness Row { get; init; }
        public required Thickness LabelColumn { get; init; }
        public required Thickness Detail { get; init; }
        public required Thickness RetryButton { get; init; }
    }
}
