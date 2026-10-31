namespace CoverPlayerMP.Shared.Player;

public interface IAudioFingerprintProvider
{
    Task<AudioFingerprint> CreateAsync(string path, CancellationToken cancellationToken = default);
}
