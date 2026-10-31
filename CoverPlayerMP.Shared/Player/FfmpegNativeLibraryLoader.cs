using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;

namespace CoverPlayerMP.Shared.Player;

internal static class FfmpegNativeLibraryLoader
{
    private static readonly object SyncRoot = new();
    private static bool initialized;

    public static void EnsureInitialized()
    {
        if (initialized)
            return;

        lock (SyncRoot)
        {
            if (initialized)
                return;

            var libraryPath = AppContext.BaseDirectory;
            if (!Directory.Exists(libraryPath))
                throw new InvalidOperationException($"Der FFmpeg-Ladepfad '{libraryPath}' wurde nicht gefunden.");

            DynamicallyLoadedBindings.LibrariesPath = libraryPath;
            DynamicallyLoadedBindings.Initialize();
            initialized = true;
        }
    }
}