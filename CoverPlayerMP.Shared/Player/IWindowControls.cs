namespace CoverPlayerMP.Shared.Player;

public interface IWindowControls
{
    bool IsFullScreen { get; }
    void Restore();
    void Close();
}

public interface IExternalBrowser
{
    Task OpenAsync(string url);
}
