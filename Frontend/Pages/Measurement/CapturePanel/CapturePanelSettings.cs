using System.Windows;

namespace Frontend.Pages.Measurement.CapturePanel;

/// <summary>
/// CapturePanel.yaml: labels, sizes and spacing of Starting Depth, Step, Start / Stop, the countdown and the bar chart.
/// A size with a multiplier is used as size × multiplier.
/// </summary>
public sealed class CapturePanelSettings
{
    public const string File = "Pages/Measurement/CapturePanel/CapturePanel.yaml";

    public required LabelSettings Labels { get; init; }

    public required MultiplierSettings Multipliers { get; init; }

    public required SizeSettings Sizes { get; init; }

    public required SpacingSettings Spacing { get; init; }

    public required ChartSettings Chart { get; init; }

    /// <summary>The resources CapturePanelView.xaml uses (final sizes, multipliers applied).</summary>
    public void AddTo(ResourceDictionary resources)
    {
        resources.Add("StartingDepthLabel", Labels.StartingDepth);
        resources.Add("StepLabel", Labels.Step);
        resources.Add("CountdownLabel", Labels.Countdown);
        resources.Add("DurationEditorLabel", Labels.DurationEditor);
        resources.Add("StartLabel", Labels.Start);
        resources.Add("StopLabel", Labels.Stop);
        resources.Add("StartGlyph", Labels.StartGlyph);
        resources.Add("StopGlyph", Labels.StopGlyph);

        resources.Add("CountdownFontSize", Sizes.CountdownFontSize * Multipliers.CountdownFontSize);
        resources.Add("CountdownRingSize", Sizes.CountdownRingSize * Multipliers.CountdownRingSize);
        resources.Add("CountdownRingThickness", Sizes.CountdownRingThickness * Multipliers.CountdownRingThickness);
        resources.Add("CountdownLabelFontSize", Sizes.CountdownLabelFontSize * Multipliers.CountdownLabelFontSize);
        resources.Add("StartStopButtonWidth", Sizes.StartStopButtonWidth * Multipliers.StartStopButtonWidth);
        resources.Add("StartStopButtonFontSize", Sizes.StartStopButtonFontSize * Multipliers.StartStopButtonFontSize);
        resources.Add("StartStopButtonIconFontSize", Sizes.StartStopButtonIconFontSize * Multipliers.StartStopButtonIconFontSize);
        resources.Add("CaptureLabelFontSize", Sizes.LabelFontSize * Multipliers.LabelFontSize);
        resources.Add("StartStopButtonPadding", Sizes.StartStopButtonPadding);
        resources.Add("DurationEditorWidth", Sizes.DurationEditorWidth);

        resources.Add("StepLabelMargin", Spacing.StepLabel);
        resources.Add("StartStopButtonMargin", Spacing.StartStopButton);
        resources.Add("CountdownColumnMargin", Spacing.CountdownColumn);
        resources.Add("CountdownLabelMargin", Spacing.CountdownLabel);

        resources.Add("ChartXAxisLabel", Chart.XAxisLabel);
        resources.Add("ChartYAxisLabel", Chart.YAxisLabel);
        resources.Add("ChartEmptyText", Chart.EmptyText);
        resources.Add("ChartMinHeight", Chart.MinHeight);
        resources.Add("ChartAxisLabelFontSize", Chart.AxisLabelFontSize);
        resources.Add("ChartTickFontSize", Chart.TickFontSize);
    }

    public sealed class LabelSettings
    {
        public required string StartingDepth { get; init; }
        public required string Step { get; init; }
        public required string Countdown { get; init; }
        public required string DurationEditor { get; init; }
        public required string Start { get; init; }
        public required string Stop { get; init; }
        public required string StartGlyph { get; init; }
        public required string StopGlyph { get; init; }
    }

    public sealed class MultiplierSettings
    {
        public required double CountdownFontSize { get; init; }
        public required double CountdownRingSize { get; init; }
        public required double CountdownRingThickness { get; init; }
        public required double CountdownLabelFontSize { get; init; }
        public required double StartStopButtonWidth { get; init; }
        public required double StartStopButtonFontSize { get; init; }
        public required double StartStopButtonIconFontSize { get; init; }
        public required double LabelFontSize { get; init; }
    }

    public sealed class SizeSettings
    {
        public required double CountdownFontSize { get; init; }
        public required double CountdownRingSize { get; init; }
        public required double CountdownRingThickness { get; init; }
        public required double CountdownLabelFontSize { get; init; }
        public required double StartStopButtonWidth { get; init; }
        public required double StartStopButtonFontSize { get; init; }
        public required double StartStopButtonIconFontSize { get; init; }
        public required double LabelFontSize { get; init; }
        public required Thickness StartStopButtonPadding { get; init; }
        public required double DurationEditorWidth { get; init; }
    }

    public sealed class SpacingSettings
    {
        public required Thickness StepLabel { get; init; }
        public required Thickness StartStopButton { get; init; }
        public required Thickness CountdownColumn { get; init; }
        public required Thickness CountdownLabel { get; init; }
    }

    public sealed class ChartSettings
    {
        public required string XAxisLabel { get; init; }
        public required string YAxisLabel { get; init; }
        public required string EmptyText { get; init; }
        public required double MinHeight { get; init; }
        public required double AxisLabelFontSize { get; init; }
        public required double TickFontSize { get; init; }
    }
}
