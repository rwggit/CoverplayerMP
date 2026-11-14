namespace CoverPlayerMP.Shared.Player;

public sealed class NativeFirstAudioFingerprintProvider : IAudioFingerprintProvider
{
    private readonly FpcalcFingerprintProvider fallback = new();

    public async Task<AudioFingerprint> CreateAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!FfmpegNativeLibraryLoader.HasChromaprintLibrary(AppContext.BaseDirectory))
        {
            if (OperatingSystem.IsAndroid())
                throw new InvalidOperationException("Die native Chromaprint-Bibliothek libchromaprint.so konnte unter Android nicht geladen werden. Die App muss die Bibliothek für arm64-v8a enthalten.");

            return await fallback.CreateAsync(path, cancellationToken);
        }

        if (OperatingSystem.IsAndroid())
            return await new FfmpegAudioFingerprintProvider().CreateAsync(path, cancellationToken);

        try
        {
            return await new FfmpegAudioFingerprintProvider().CreateAsync(path, cancellationToken);
        }
        catch (DllNotFoundException)
        {
            return await fallback.CreateAsync(path, cancellationToken);
        }
        catch (EntryPointNotFoundException)
        {
            return await fallback.CreateAsync(path, cancellationToken);
        }
        catch (BadImageFormatException)
        {
            return await fallback.CreateAsync(path, cancellationToken);
        }
        catch (InvalidOperationException) when (!cancellationToken.IsCancellationRequested)
        {
            return await fallback.CreateAsync(path, cancellationToken);
        }
    }
}
