using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CoverPlayerMP.Shared.Player;

public sealed class FpcalcFingerprintProvider : IAudioFingerprintProvider
{
    public async Task<AudioFingerprint> CreateAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Die Audiodatei wurde nicht gefunden.", path);

        var executable = ResolveExecutable();
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            ArgumentList = { "-json", path },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("fpcalc konnte nicht gestartet werden.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? "fpcalc konnte keinen Fingerprint erzeugen."
                : error.Trim());

        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        var fingerprint = root.GetProperty("fingerprint").GetString();
        var duration = root.GetProperty("duration").GetInt32();
        if (string.IsNullOrWhiteSpace(fingerprint) || duration <= 0)
            throw new InvalidOperationException("fpcalc hat keinen gültigen Fingerprint geliefert.");

        return new AudioFingerprint(fingerprint, duration);
    }

    private static string ResolveExecutable()
    {
        var fileName = OperatingSystem.IsWindows() ? "fpcalc.exe" : "fpcalc";
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", fileName),
            Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", fileName)
        };

        return candidates.FirstOrDefault(File.Exists) ?? fileName;
    }
}
