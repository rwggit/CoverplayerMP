namespace CoverPlayerMP.Shared.Player;

public sealed record Track(
    string Source,
    string Title,
    string Artist,
    string? CoverSource = null,
    bool IsRelativePath = false);
