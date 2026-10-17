using System.Globalization;

namespace CoverPlayerMP.Shared.Localization;

public enum AppLanguage { Auto, German, English, French }

public sealed class AppText
{
    private AppLanguage language = AppLanguage.Auto;

    public event EventHandler? LanguageChanged;

    public AppLanguage Language
    {
        get => language;
        set
        {
            if (language == value)
                return;
            language = value;
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private AppLanguage Effective => language switch
    {
        AppLanguage.German => AppLanguage.German,
        AppLanguage.English => AppLanguage.English,
        AppLanguage.French => AppLanguage.French,
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
        {
            "de" => AppLanguage.German,
            "fr" => AppLanguage.French,
            _ => AppLanguage.English
        }
    };

    public bool IsGerman => Effective == AppLanguage.German;

    public string Code => Effective switch
    {
        AppLanguage.German => "de",
        AppLanguage.French => "fr",
        _ => "en"
    };

    private string T(string de, string en, string fr) => Effective switch
    {
        AppLanguage.German => de,
        AppLanguage.French => fr,
        _ => en
    };

    // Allgemein
    public string Close => T("Schließen", "Close", "Fermer");
    public string Cancel => T("Abbrechen", "Cancel", "Annuler");
    public string All => T("Alle", "All", "Tous");
    public string None => T("Keine", "None", "Aucun");
    public string Apply => T("Übernehmen", "Apply", "Appliquer");
    public string Of => T("von", "of", "sur");
    public string Artist => T("Interpret", "Artist", "Artiste");
    public string Title => T("Titel", "Title", "Titre");
    public string Duration => T("Dauer", "Duration", "Durée");
    public string Cover => "Cover";
    public string No => T("Nr.", "No.", "N°");

    // Hauptansicht
    public string SelectPlaylist => T("Playlist auswählen", "Select playlist", "Choisir la playlist");
    public string TrackDetails => T("Details zum Lied", "Track details", "Détails du morceau");
    public string PlayerControls => T("Player-Steuerung", "Player controls", "Commandes du lecteur");
    public string Settings => T("Einstellungen", "Settings", "Paramètres");
    public string HideControls => T("Steuerung ausblenden", "Hide controls", "Masquer les commandes");
    public string OpenPlaylist => T("Playlist öffnen", "Open playlist", "Ouvrir la playlist");
    public string EditPlaylist => T("Playlist bearbeiten", "Edit playlist", "Modifier la playlist");
    public string SearchAudioFiles => T("Audiodateien suchen", "Search audio files", "Rechercher des fichiers audio");
    public string Previous => T("Vorheriges", "Previous", "Précédent");
    public string Restart => T("Anfang", "Restart", "Début");
    public string Pause => "Pause";
    public string Play => T("Wiedergabe", "Play", "Lecture");
    public string Next => T("Nächstes", "Next", "Suivant");
    public string Volume => T("Lautstärke", "Volume", "Volume");
    public string VolumePercent(double percent) => T($"Lautstärke: {percent} Prozent", $"Volume: {percent} percent", $"Volume : {percent} pour cent");

    // Mediathek
    public string AudioFiles => T("Audiodateien", "Audio files", "Fichiers audio");
    public string ReadingAudioFiles => T("Audiodateien werden gelesen …", "Reading audio files …", "Lecture des fichiers audio …");
    public string DurationFilterPlaceholder => T("z.B. >3:00", "e.g. >3:00", "p. ex. >3:00");

    // Playlist bearbeiten
    public string Search => T("Suchen …", "Search …", "Rechercher …");
    public string RemoveTrack => T("Lied entfernen", "Remove track", "Supprimer le morceau");
    public string AddTrack => T("Lied hinzufügen", "Add track", "Ajouter un morceau");
    public string SelectAll => T("Alle auswählen", "Select all", "Tout sélectionner");
    public string EndSelection => T("Auswahl beenden", "End selection", "Terminer la sélection");
    public string Selected(int count) => T($"{count} ausgewählt", $"{count} selected", $"{count} sélectionné(s)");
    public string Move => T("Verschieben", "Move", "Déplacer");
    public string Select => T("Auswählen", "Select", "Sélectionner");
    public string PlayTrack => T("Abspielen", "Play", "Lire");
    public string RemovingOne(string name) => T($"„{name}“ wird entfernt …", $"Removing “{name}” …", $"Suppression de « {name} » …");
    public string RemovingMany(int count) => T($"{count} Lieder werden entfernt …", $"Removing {count} tracks …", $"Suppression de {count} morceaux …");

    // Einfügen
    public string InsertTrack(string title) => T($"„{title}“ einfügen", $"Insert “{title}”", $"Insérer « {title} »");
    public string InsertAtStart => T("Am Anfang der Playlist", "At the beginning of the playlist", "Au début de la playlist");
    public string InsertBeforeCurrent => T("Vor dem aktuellen Lied", "Before the current track", "Avant le morceau actuel");
    public string InsertAfterCurrent => T("Hinter dem aktuellen Lied", "After the current track", "Après le morceau actuel");
    public string InsertAfterCurrentAndPlay => T("Hinter dem aktuellen Lied und sofort abspielen", "After the current track and play now", "Après le morceau actuel et lire immédiatement");
    public string InsertAtEnd => T("Am Ende der Playlist", "At the end of the playlist", "À la fin de la playlist");

    // Details
    public string TagContents => T("Inhalt der Tags", "Tag contents", "Contenu des balises");
    public string ShowCoverPictures => T("Cover-Bilder anzeigen", "Show cover pictures", "Afficher les pochettes");
    public string NoTags => T("Keine Tags vorhanden.", "No tags available.", "Aucune balise disponible.");
    public string CoverPictures => T("Cover-Bilder", "Cover pictures", "Pochettes");
    public string NoCoverPictures => T("Keine Cover-Bilder vorhanden.", "No cover pictures available.", "Aucune pochette disponible.");

    // Einstellungen
    public string Crossfade => T("Überblendung Lautstärke", "Volume crossfade", "Fondu enchaîné du volume");
    public string CrossfadeUnsupported => T("Das aktuelle Ausgabegerät erlaubt keine Überblendung.", "The current output device does not support crossfading.", "Le périphérique de sortie actuel ne permet pas le fondu enchaîné.");
    public string CoverFade => T("Überblendung Cover", "Cover crossfade", "Fondu enchaîné de la pochette");
    public string CrossfadeTooltip(string seconds) => T($"Dauer der Überblendung: {seconds} Sekunden", $"Crossfade duration: {seconds} seconds", $"Durée du fondu enchaîné : {seconds} secondes");
    public string CoverSize => T("Cover-Größe", "Cover size", "Taille des pochettes");
    public string CoverSizeTooltip(int size) => T($"Größe der Cover in der Playlistbearbeitung: {size} rem", $"Cover size in playlist editor: {size} rem", $"Taille des pochettes dans l’éditeur de playlist : {size} rem");
    public string LanguageLabel => T("Sprache", "Language", "Langue");
    public string LanguageAuto => T("Automatisch", "Automatic", "Automatique");
    public string Animations => T("Animationen", "Animations", "Animations");
    public string RemotePlayback => T("Wiedergabe auf anderen Geräten", "Playback on other devices", "Lecture sur d'autres appareils");
    public string RemoteSearching => T("Suche Geräte …", "Searching for devices …", "Recherche d'appareils …");
    public string RemoteNoDevices => T("Keine Geräte gefunden.", "No devices found.", "Aucun appareil trouvé.");
    public string ErrorRemote(string message) => T($"Wiedergabe auf dem Gerät fehlgeschlagen: {message}", $"Playback on the device failed: {message}", $"Échec de la lecture sur l'appareil : {message}");
    public string RemoteThisDevice => T("Dieses Gerät", "This device", "Cet appareil");
    public string RemoteScreen => T("Bildschirm übertragen …", "Cast screen …", "Caster l'écran …");
    public string QuitApp => T("CoverplayerMP beenden", "Quit CoverPlayerMP", "Quitter CoverPlayerMP");
    public string SavePlaylist => T("Playlist speichern", "Save playlist", "Enregistrer la playlist");
    public string SaveBeforeQuit => T("Die Playlist wurde verändert. Soll sie vor dem Beenden gespeichert werden?",
        "The playlist has been modified. Save it before quitting?",
        "La playlist a été modifiée. L’enregistrer avant de quitter ?");
    public string SaveAndQuit => T("Speichern und beenden", "Save and quit", "Enregistrer et quitter");
    public string QuitWithoutSaving => T("Ohne Speichern beenden", "Quit without saving", "Quitter sans enregistrer");

    // Metadaten
    public string MetaFile => T("Datei", "File", "Fichier");
    public string MetaError => T("Fehler", "Error", "Erreur");
    public string MetaFileSize => T("Dateigröße", "File size", "Taille du fichier");
    public string MetaModified => T("Geändert", "Modified", "Modifié");
    public string MetaAlbumArtist => T("Album-Interpret", "Album artist", "Artiste de l’album");
    public string MetaAlbum => "Album";
    public string MetaYear => T("Jahr", "Year", "Année");
    public string MetaTrackNumber => T("Titelnummer", "Track number", "Numéro de piste");
    public string MetaDisc => "CD";
    public string MetaGenre => "Genre";
    public string MetaComposer => T("Komponist", "Composer", "Compositeur");
    public string MetaConductor => T("Dirigent", "Conductor", "Chef d’orchestre");
    public string MetaGrouping => T("Gruppierung", "Grouping", "Regroupement");
    public string MetaComment => T("Kommentar", "Comment", "Commentaire");
    public string MetaLyrics => T("Liedtext", "Lyrics", "Paroles");
    public string MetaTagTypes => T("Tag-Typen", "Tag types", "Types de balises");
    public string MetaDuration => T("Spieldauer", "Duration", "Durée");
    public string MetaSampleRate => T("Abtastrate", "Sample rate", "Fréquence d’échantillonnage");
    public string MetaChannels => T("Kanäle", "Channels", "Canaux");
    public string MetaBitsPerSample => T("Bits pro Sample", "Bits per sample", "Bits par échantillon");
    public string MetaMetadata => T("Metadaten", "Metadata", "Métadonnées");
    public string MetaUnreadable(string message) => T($"Nicht lesbar: {message}", $"Not readable: {message}", $"Illisible : {message}");

    // Fehlerseiten
    public string NotFoundTitle => T("Nicht gefunden", "Not Found", "Introuvable");
    public string NotFoundMessage => T("Der gesuchte Inhalt existiert leider nicht.", "Sorry, the content you are looking for does not exist.", "Désolé, le contenu recherché n’existe pas.");
    public string PageErrorTitle => T("Fehler", "Error", "Erreur");
    public string PageErrorHeading => T("Fehler.", "Error.", "Erreur.");
    public string PageErrorMessage => T("Bei der Verarbeitung der Anforderung ist ein Fehler aufgetreten.", "An error occurred while processing your request.", "Une erreur s’est produite lors du traitement de votre demande.");
    public string RequestId => T("Anforderungs-ID:", "Request ID:", "ID de la requête :");

    // Fehlermeldungen
    public string ErrorNoAudioPermission => T("Ohne Berechtigung für Audiodateien können die Titel nicht gelesen werden.",
        "Tracks cannot be read without permission to access audio files.",
        "Les morceaux ne peuvent pas être lus sans l’autorisation d’accéder aux fichiers audio.");
    public string ErrorPlaylistLocation(string uri) => T($"Der Speicherort der Playlist konnte nicht ermittelt werden: {uri}",
        $"The playlist location could not be determined: {uri}",
        $"L’emplacement de la playlist n’a pas pu être déterminé : {uri}");
    public string ErrorOpenPlaylist(string message) => T($"Playlist konnte nicht geöffnet werden: {message}", $"Playlist could not be opened: {message}", $"Impossible d’ouvrir la playlist : {message}");
    public string MissingTracksHint(int count) => T($"{count} Audiodateien der Playlist wurden nicht gefunden. Wähle den Musikordner oder die Audiodateien.", $"{count} audio files of the playlist were not found. Select the music folder or the audio files.", $"{count} fichiers audio de la playlist sont introuvables. Choisissez le dossier de musique ou les fichiers audio.");
    public string MissingFilesLabel => T("Nicht gefundene Dateien", "Files not found", "Fichiers introuvables");
    public string OpenPlaylistFromServer => T("Playlist vom Server öffnen (WebDAV)", "Open playlist from server (WebDAV)", "Ouvrir la playlist depuis un serveur (WebDAV)");
    public string SelectAudioFiles => T("Audiodateien wählen", "Select audio files", "Choisir les fichiers audio");
    public string ServerButton => T("Server (WebDAV)", "Server (WebDAV)", "Serveur (WebDAV)");
    public string ServerDialogTitle => T("Playlist vom Server öffnen", "Open playlist from server", "Ouvrir la playlist depuis un serveur");
    public string ServerUrl => T("Server, z. B. nas:5006 (zeigt die Freigaben)", "Server, e.g. nas:5006 (shows the shares)", "Serveur, p. ex. nas:5006 (affiche les partages)");
    public string ServerUser => T("Benutzer (optional)", "User (optional)", "Utilisateur (facultatif)");
    public string ServerTrustCertificate => T("Zertifikat dieses Servers akzeptieren (selbstsigniert)", "Accept this server's certificate (self-signed)", "Accepter le certificat de ce serveur (auto-signé)");
    public string ServerPassword => T("Passwort", "Password", "Mot de passe");
    public string ServerHint => T("Die Playlist und ihre relativen Pfade werden von diesem Server geladen. Zugangsdaten gehen nur an diesen Server.", "The playlist and its relative paths are loaded from this server. Credentials are sent only to this server.", "La playlist et ses chemins relatifs sont chargés depuis ce serveur. Les identifiants ne sont envoyés qu’à ce serveur.");
    public string Connect => T("Verbinden", "Connect", "Se connecter");
        public string ServerUp => T("Eine Ebene höher", "Up one level", "Niveau supérieur");
        public string ServerEmpty => T("Keine Ordner oder Playlists gefunden.", "No folders or playlists found.", "Aucun dossier ni playlist trouvé.");
        public string ServerLoading => T("Wird geladen …", "Loading …", "Chargement …");
        public string ServerChoosePlaylist => T("Playlist auswählen", "Choose a playlist", "Choisir une playlist");
    public string ServerAddressExampleHint => T(
        "Gültige Serveradressen sind z.B. für ein Synology NAS DS720+ DS720plus:5006 oder 192.168.1.2:5006",
        "e.g. DS720plus:5006 or 192.168.1.2:5006 (Synology NAS DS720+)",
        "p. ex. DS720plus:5006 ou 192.168.1.2:5006 (NAS Synology DS720+)");
    public string ServerTrustCertificateHint => T(
        "Es könnte helfen, den Haken bei „Zertifikat dieses Servers akzeptieren (selbstsigniert)“ zu setzen",
        "Enabling \"Accept this server's certificate (self-signed)\" might help",
        "Cocher « Accepter le certificat de ce serveur (auto-signé) » pourrait aider");
    public string ErrorInvalidServerUrl => T(
        $"Bitte eine gültige http- oder https-Adresse angeben, {ServerAddressExampleHint}.",
        $"Please enter a valid http or https address, {ServerAddressExampleHint}.",
        $"Veuillez saisir une adresse http ou https valide, {ServerAddressExampleHint}.");
    public string ErrorServerFile(string url, int status)
    {
        var message = T($"Der Server liefert {url} nicht (Status {status} – {DescribeHttpStatus(status)}).",
            $"The server does not deliver {url} (status {status} – {DescribeHttpStatus(status)}).",
            $"Le serveur ne fournit pas {url} (statut {status} – {DescribeHttpStatus(status)}).");

        // Bei 405 unterstützt der Server vermutlich kein WebDAV an dieser Adresse – ein gültiges Beispiel hilft weiter.
        return status == 405 ? $"{message} {ServerAddressExampleHint}." : message;
    }

    // Erklärt den HTTP-Statuscode in Klartext, damit z. B. "401" nicht ohne Kontext erscheint.
    private string DescribeHttpStatus(int status) => status switch
    {
        401 => T("nicht autorisiert, Benutzername oder Passwort falsch", "unauthorized, wrong username or password", "non autorisé, nom d'utilisateur ou mot de passe incorrect"),
        403 => T("Zugriff verweigert", "access forbidden", "accès refusé"),
        404 => T("nicht gefunden", "not found", "introuvable"),
        405 => T("Methode nicht erlaubt, Server unterstützt vermutlich kein WebDAV", "method not allowed, the server likely does not support WebDAV", "méthode non autorisée, le serveur ne prend probablement pas en charge WebDAV"),
        407 => T("Proxy-Authentifizierung erforderlich", "proxy authentication required", "authentification du proxy requise"),
        408 => T("Zeitüberschreitung bei der Anfrage", "request timed out", "délai d'attente de la requête dépassé"),
        409 => T("Konflikt beim Zugriff auf die Ressource", "conflict accessing the resource", "conflit lors de l'accès à la ressource"),
        410 => T("nicht mehr verfügbar", "no longer available", "n'est plus disponible"),
        423 => T("Ressource ist gesperrt", "resource is locked", "ressource verrouillée"),
        429 => T("zu viele Anfragen, bitte später erneut versuchen", "too many requests, please try again later", "trop de requêtes, veuillez réessayer plus tard"),
        >= 500 and < 600 => T("Serverfehler", "server error", "erreur du serveur"),
        _ => T("unerwarteter Fehler", "unexpected error", "erreur inattendue")
    };
    public string MusicSourceNeeded(int count) => T($"Für {count} Titel der Playlist fehlen die Audiodateien. Wähle den Musikordner, einzelne Audiodateien oder einen Server.", $"The audio files for {count} playlist tracks are missing. Select the music folder, individual audio files or a server.", $"Les fichiers audio de {count} morceaux de la playlist manquent. Choisissez le dossier de musique, des fichiers audio ou un serveur.");
    public string SelectMusicFolder => T("Musikordner wählen", "Select music folder", "Choisir le dossier de musique");
    public string MusicFolderNeeded(int count) => T(
        $"Für {count} Titel der Playlist wurden die Audiodateien nicht gefunden. Bitte den Ordner wählen, der die Musikdateien enthält.",
        $"The audio files for {count} playlist tracks were not found. Please select the folder that contains the music files.",
        $"Les fichiers audio de {count} morceaux de la playlist sont introuvables. Veuillez choisir le dossier contenant les fichiers de musique.");
    public string PickPlaylistTitle => T("M3U-Playlist auswählen", "Select M3U playlist", "Choisir une playlist M3U");
    public string ErrorEmptyPlaylist => T("Die Playlist enthält keine abspielbaren Titel.", "The playlist contains no playable tracks.", "La playlist ne contient aucun morceau lisible.");
    public string ErrorReadPlaylist(string message) => T($"Playlist konnte nicht gelesen werden: {message}", $"Playlist could not be read: {message}", $"Impossible de lire la playlist : {message}");
    public string ErrorSeek(string message) => T($"Die Abspielposition konnte nicht geändert werden: {message}", $"The playback position could not be changed: {message}", $"La position de lecture n’a pas pu être modifiée : {message}");
    public string ErrorReadAudioFiles(string message) => T($"Audiodateien konnten nicht gelesen werden: {message}", $"Audio files could not be read: {message}", $"Impossible de lire les fichiers audio : {message}");
    public string ErrorSavePlaylist(string message) => T($"Playlist konnte nicht gespeichert werden: {message}", $"Playlist could not be saved: {message}", $"Impossible d’enregistrer la playlist : {message}");
    public string ErrorFileNotFound(string path) => T($"Audiodatei nicht gefunden: {path}", $"Audio file not found: {path}", $"Fichier audio introuvable : {path}");
    public string ErrorCannotPlay(string name, string message) => T($"„{name}“ kann nicht abgespielt werden: {message}", $"“{name}” cannot be played: {message}", $"« {name} » ne peut pas être lu : {message}");
    public string ErrorTransition(string message) => T($"Übergang konnte nicht ausgeführt werden: {message}", $"Transition failed: {message}", $"Échec de la transition : {message}");
    public string ErrorMonitor(string message) => T($"Playerüberwachung fehlgeschlagen: {message}", $"Player monitoring failed: {message}", $"Échec de la surveillance du lecteur : {message}");
}
