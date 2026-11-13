namespace Frontend.Shared.Infrastructure;

/// <summary>A page that does work only while it is on screen (polling, refreshing). MainViewModel calls these.</summary>
public interface INavigationAware
{
    void OnShown();

    void OnHidden();
}
