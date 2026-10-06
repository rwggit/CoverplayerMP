namespace CoverPlayerMP.Shared.Player;

public interface IWindowControls
{
    bool IsFullScreen { get; }
    void Restore();
    void Close();
}
