using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;
using AcoustID.Native;
using System.Runtime.InteropServices;

namespace CoverPlayerMP.Shared.Player;

public static class FfmpegNativeLibraryLoader
{
    private static readonly object SyncRoot = new();
    private static nint chromaprintHandle;
    private static bool chromaprintResolverRegistered;
    private static bool ffmpegResolverRegistered;
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

            RunNativeStep("FFmpeg-Resolver registrieren", RegisterFfmpegResolver);
            RunNativeStep("Chromaprint-Resolver registrieren", RegisterChromaprintResolver);
            DynamicallyLoadedBindings.LibrariesPath = OperatingSystem.IsAndroid()
                ? string.Empty
                : libraryPath;
            RunNativeStep("FFmpeg-Bindings initialisieren", DynamicallyLoadedBindings.Initialize);
            RunNativeStep("Chromaprint laden", () =>
            {
                if (!TryLoadChromaprint(libraryPath) && OperatingSystem.IsAndroid())
                    throw new InvalidOperationException("libchromaprint.so konnte unter Android nicht geladen werden.");
            });
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

        if (names.Any(name => File.Exists(Path.Combine(root, name))))
            return true;

        if (!OperatingSystem.IsAndroid())
            return false;

        return TryLoadChromaprintByName();
    }

    private static bool TryLoadChromaprint(string root)
    {
        foreach (var name in new[] { "chromaprint.dll", "libchromaprint.dll", "libchromaprint.dylib", "libchromaprint.dylib.1", "libchromaprint.so", "libchromaprint.so.1" })
        {
            var path = Path.Combine(root, name);
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out _))
                return true;
        }

        if (OperatingSystem.IsAndroid())
            return TryLoadChromaprintByName();

        return false;
    }

    private static void RunNativeStep(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Native-Initialisierungsschritt '{name}' fehlgeschlagen: {ex.Message}", ex);
        }
    }

    private static bool TryLoadChromaprintByName()
    {
        if (chromaprintHandle != 0)
            return true;

        foreach (var name in new[] { "libchromaprint.so", "chromaprint" })
        {
            if (NativeLibrary.TryLoad(name, out var handle))
            {
                chromaprintHandle = handle;
                return true;
            }
        }

        return false;
    }

    private static void RegisterChromaprintResolver()
    {
        if (chromaprintResolverRegistered)
            return;

        NativeLibrary.SetDllImportResolver(typeof(NativeChromaContext).Assembly,
            (libraryName, _, searchPath) =>
            {
                if (!libraryName.Contains("chromaprint", StringComparison.OrdinalIgnoreCase))
                    return nint.Zero;

                if (chromaprintHandle != 0)
                    return chromaprintHandle;

                return NativeLibrary.TryLoad("libchromaprint.so", out var handle)
                    ? chromaprintHandle = handle
                    : nint.Zero;
            });
        chromaprintResolverRegistered = true;
    }

    private static void RegisterFfmpegResolver()
    {
        if (!OperatingSystem.IsAndroid() || ffmpegResolverRegistered)
            return;

        NativeLibrary.SetDllImportResolver(typeof(DynamicallyLoadedBindings).Assembly,
            (libraryName, _, _) =>
            {
                if (!string.Equals(libraryName, "libdl.so.2", StringComparison.Ordinal))
                    return nint.Zero;

                return NativeLibrary.TryLoad("libdl.so", out var handle)
                    ? handle
                    : nint.Zero;
            });
        ffmpegResolverRegistered = true;
    }
}