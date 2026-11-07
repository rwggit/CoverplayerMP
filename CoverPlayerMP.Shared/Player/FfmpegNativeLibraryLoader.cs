using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;
using System.Runtime.InteropServices;

namespace CoverPlayerMP.Shared.Player;

public static class FfmpegNativeLibraryLoader
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
            TryLoadChromaprint(libraryPath);
            initialized = true;
        }
    }

    public static bool HasChromaprintLibrary(string root)
    {
        var names = OperatingSystem.IsWindows()
            ? new[] { "chromaprint.dll", "libchromaprint.dll" }
            : OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst()
                ? new[] { "libchromaprint.dylib", "libchromaprint.dylib.1" }
                : new[] { "libchromaprint.so", "libchromaprint.so.1" };

        return names.Any(name => File.Exists(Path.Combine(root, name)));
    }

    private static void TryLoadChromaprint(string root)
    {
        foreach (var name in new[] { "chromaprint.dll", "libchromaprint.dll", "libchromaprint.dylib", "libchromaprint.dylib.1", "libchromaprint.so", "libchromaprint.so.1" })
        {
            var path = Path.Combine(root, name);
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out _))
                return;
        }
    }
}