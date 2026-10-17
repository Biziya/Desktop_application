using System.Windows;

namespace Frontend.Pages.CoreInformation.DrillholeInformation;

/// <summary>DrillholeInformation.yaml: labels, field sizes and spacing of the Drillhole Information section.</summary>
public sealed class DrillholeInformationSettings
{
    public const string File = "Pages/CoreInformation/DrillholeInformation/DrillholeInformation.yaml";

    public required LabelSettings Labels { get; init; }

    public required FieldSettings Fields { get; init; }

    public required SpacingSettings Spacing { get; init; }

    public required WarningSettings DrillholeWarning { get; init; }

    /// <summary>The resources DrillholeInformationView.xaml (and MeasurementPage.xaml, for the warning) use.</summary>
    public void AddTo(ResourceDictionary resources)
    {
        resources.Add("DrillholeSectionTitle", Labels.SectionTitle);
        resources.Add("DrillholeLabel", Labels.Drillhole);
        resources.Add("ProjectLabel", Labels.Project);
        resources.Add("CampaignLabel", Labels.Campaign);
        resources.Add("OperatorLabel", Labels.Operator);
        resources.Add("NotesLabel", Labels.Notes);
        resources.Add("SaveAndContinueLabel", Labels.SaveAndContinue);
        resources.Add("RetryDrillholesLabel", Labels.Retry);
        resources.Add("AutofilledToolTip", Labels.AutofilledToolTip);
        resources.Add("ClearDrillholeToolTip", Labels.ClearDrillholeToolTip);
        resources.Add("DrillholeLockedToolTip", Labels.DrillholeLockedToolTip);

        resources.Add("DrillholeColumnWidth", Fields.DrillholeWidth);
        resources.Add("DrillholeColumnMinWidth", Fields.DrillholeMinWidth);
        resources.Add("ProjectColumnWidth", Fields.ProjectWidth);
        resources.Add("ProjectColumnMinWidth", Fields.ProjectMinWidth);
        resources.Add("CampaignColumnWidth", Fields.CampaignWidth);
        resources.Add("OperatorColumnWidth", Fields.OperatorWidth);
        resources.Add("OperatorColumnMinWidth", Fields.OperatorMinWidth);
        resources.Add("NotesHeight", Fields.NotesHeight);

        resources.Add("DrillholeFieldMargin", Spacing.Field);
        resources.Add("NotesPadding", Spacing.NotesPadding);
        resources.Add("DrillholeBoxPadding", Spacing.DrillholeBoxPadding);
        resources.Add("ClearDrillholeMargin", Spacing.ClearButton);
        resources.Add("RetryDrillholesMargin", Spacing.RetryButton);
        resources.Add("SaveAndContinueMargin", Spacing.SaveAndContinueButton);

        resources.Add("DrillholeWarningText", DrillholeWarning.Text);
        resources.Add("DrillholeWarningFontSize", DrillholeWarning.FontSize);
        resources.Add("DrillholeWarningMargin", DrillholeWarning.Margin);
    }

    public sealed class LabelSettings
    {
        public required string SectionTitle { get; init; }
        public required string Drillhole { get; init; }
        public required string Project { get; init; }
        public required string Campaign { get; init; }
        public required string Operator { get; init; }
        public required string Notes { get; init; }
        public required string SaveAndContinue { get; init; }
        public required string Retry { get; init; }
        public required string AutofilledToolTip { get; init; }
        public required string ClearDrillholeToolTip { get; init; }
        public required string DrillholeLockedToolTip { get; init; }
    }

    public sealed class FieldSettings
    {
        public required GridLength DrillholeWidth { get; init; }
        public required double DrillholeMinWidth { get; init; }
        public required GridLength ProjectWidth { get; init; }
        public required double ProjectMinWidth { get; init; }
        public required GridLength CampaignWidth { get; init; }
        public required GridLength OperatorWidth { get; init; }
        public required double OperatorMinWidth { get; init; }
        public required double NotesHeight { get; init; }
    }

    public sealed class SpacingSettings
    {
        public required Thickness Field { get; init; }
        public required Thickness NotesPadding { get; init; }
        public required Thickness DrillholeBoxPadding { get; init; }
        public required Thickness ClearButton { get; init; }
        public required Thickness RetryButton { get; init; }
        public required Thickness SaveAndContinueButton { get; init; }
    }

    public sealed class WarningSettings
    {
        public required string Text { get; init; }
        public required double FontSize { get; init; }
        public required Thickness Margin { get; init; }
    }
}
