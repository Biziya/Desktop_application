namespace Frontend.Pages.Measurement;

public interface IDialogService
{
    bool ConfirmDelete();

    /// <summary>Positions are 1-based table positions (No) before and after the move.</summary>
    bool ConfirmMove(int fromPosition, int toPosition);
}
