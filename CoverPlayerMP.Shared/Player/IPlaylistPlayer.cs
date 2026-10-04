namespace CoverPlayerMP.Shared.Player;

public interface IPlaylistPlayer : IAsyncDisposable
{
    event EventHandler? StateChanged;

    IReadOnlyList<Track> Tracks { get; }
    Track? CurrentTrack { get; }
    int CurrentIndex { get; }
    double Position { get; }
    double Duration { get; }
    double Volume { get; set; }
    double CrossfadeSeconds { get; set; }
    bool IsPlaying { get; }
    bool IsPaused { get; }
    string? ErrorMessage { get; }
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
    Task<IReadOnlyList<LibraryTrack>> GetLibraryTracksAsync(CancellationToken cancellationToken = default);
    Task InsertTrackAsync(Track track, TrackInsertPosition position, CancellationToken cancellationToken = default);
    Task PlayWithCrossfadeAsync(int index, CancellationToken cancellationToken = default);
    void MoveTrack(int fromIndex, int toIndex);
    Task RemoveTrackAsync(int index, CancellationToken cancellationToken = default);
    bool IsPlaylistModified { get; }
    bool CanQuit { get; }
    Task<string?> SavePlaylistAsync(CancellationToken cancellationToken = default);
    void Quit();
}
