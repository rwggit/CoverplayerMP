namespace CoverPlayerMP.Shared.Player;

/// <summary>
/// Optional für Player-Implementierungen: gibt die aktuell wiedergegebene Datei kurzzeitig frei,
/// damit sie verändert werden kann (z. B. Cover ersetzen).
/// </summary>
public interface ICoverFileAccess
{
    /// <summary>
    /// Führt <paramref name="action"/> aus. Ist <paramref name="source"/> die aktuell wiedergegebene Datei,
    /// wird die Wiedergabe vorher ohne Ausblendung gestoppt und die Datei freigegeben; danach wird die
    /// Wiedergabe an derselben Position ohne Einblendung fortgesetzt.
    /// </summary>
    Task RunWithReleasedFileAsync(string source, Func<Task> action, CancellationToken cancellationToken = default);
}
