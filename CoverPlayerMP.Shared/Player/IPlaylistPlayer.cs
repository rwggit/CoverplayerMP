namespace CoverPlayerMP.Shared.Player;

public interface IPlaylistPlayer : IAsyncDisposable
{
    event EventHandler? StateChanged;

    IReadOnlyList<Track> Tracks { get; }
    string? PlaylistPath => null;
    Track? CurrentTrack { get; }
    int CurrentIndex { get; }
    double Position { get; }
    double Duration { get; }
    double Volume { get; set; }
    double CrossfadeSeconds { get; set; }
    bool SupportsCrossfade => true;
    bool IsPlaying { get; }
    bool IsPaused { get; }
    string? ErrorMessage { get; }
    bool NeedsMusicFolder => false;
    int LibraryTracksFound => 0;
    bool LibraryTracksLoadedFromIndex => false;
    Task SelectMusicFolderAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task SelectAudioFilesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    bool SupportsServerPlaylist => false;
    Task<IReadOnlyList<ServerEntry>> ListServerFolderAsync(string folderUrl, string? user, string? password, bool trustCertificate = false, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ServerEntry>>([]);
    IReadOnlyList<string> MissingFiles => [];
    Task OpenServerPlaylistAsync(string playlistUrl, string? user, string? password, bool trustCertificate = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    bool SupportsRemotePlayback => false;
    RemoteDevice? ActiveDevice => null;
    Task<IReadOnlyList<RemoteDevice>> GetRemoteDevicesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RemoteDevice>>([]);
    Task SelectRemoteDeviceAsync(RemoteDevice? device, CancellationToken cancellationToken = default) => Task.CompletedTask;

    Task OpenPlaylistAsync(CancellationToken cancellationToken = default);
    Task LoadPlaylistAsync(string pathOrContent, bool isContent = false, CancellationToken cancellationToken = default);
    Task PlayAsync(int index = 0, CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task SeekAsync(double seconds, CancellationToken cancellationToken = default);
    Task NextAsync(CancellationToken cancellationToken = default);
    Task PreviousAsync(CancellationToken cancellationToken = default);
    Task RestartAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LibraryTrack>> GetLibraryTracksAsync(CancellationToken cancellationToken = default, bool refresh = false);
    Task InsertTrackAsync(Track track, TrackInsertPosition position, CancellationToken cancellationToken = default);
    Task PlayWithCrossfadeAsync(int index, CancellationToken cancellationToken = default);
    void MoveTrack(int fromIndex, int toIndex);
    Task RemoveTrackAsync(int index, CancellationToken cancellationToken = default);
    bool IsPlaylistModified { get; }
    bool CanQuit { get; }
    Task<string?> SavePlaylistAsync(CancellationToken cancellationToken = default);
    void Quit();
}
