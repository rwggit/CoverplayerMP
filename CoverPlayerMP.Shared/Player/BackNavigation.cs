namespace CoverPlayerMP.Shared.Player;

public static class BackNavigation
{
    public static Func<bool>? Handler { get; set; }

    public static bool TryHandle() => Handler?.Invoke() ?? false;
}
