namespace CoverPlayerMP.Shared.Player;

public enum RemoteDeviceKind
{
    Audio,
    Cast,
    Dlna,
    Screen
}

public sealed record RemoteDevice(string Id, string Name, RemoteDeviceKind Kind, string Connection = "");
