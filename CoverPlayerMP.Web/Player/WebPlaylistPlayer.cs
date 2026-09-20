using CoverPlayerMP.Shared.Player;
using Microsoft.JSInterop;

namespace CoverPlayerMP.Web.Player;

public sealed class WebPlaylistPlayer(IJSRuntime js) : IPlaylistPlayer
{
    private readonly IJSRuntime js = js;
    private List<Track> tracks = [];
    private int currentIndex = -1;
    private double volume = 1;
    private double crossfadeSeconds = 2;
    private bool isPlaying;
    private bool isPaused;
    private double position;
    private double duration;

    public event EventHandler? StateChanged;
    public IReadOnlyList<Track> Tracks => tracks;
    public Track? CurrentTrack => currentIndex >= 0 && currentIndex < tracks.Count ? tracks[currentIndex] : null;
    public int CurrentIndex => currentIndex;
    public double Position => position;
    public double Duration => duration;
    public double Volume
    {
        get => volume;
        set
        {
            volume = Math.Clamp(value, 0, 1);
            _ = js.InvokeVoidAsync("coverPlayer.setVolume", volume);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public double CrossfadeSeconds { get => crossfadeSeconds; set => crossfadeSeconds = Math.Clamp(value, 0, 10); }
    public bool IsPlaying => isPlaying;
    public bool IsPaused => isPaused;
    public string? ErrorMessage => null;

    public async Task OpenPlaylistAsync(CancellationToken cancellationToken = default)
    {
        var content = await js.InvokeAsync<string?>("coverPlayer.pickPlaylist", cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
            return;
        tracks = PlaylistParser.Parse(content).ToList();
        currentIndex = -1;
        if (tracks.Count > 0)
            await PlayAsync(0, cancellationToken);
    }

    public Task LoadPlaylistAsync(string pathOrContent, bool isContent = false, CancellationToken cancellationToken = default)
    {
        tracks = PlaylistParser.Parse(pathOrContent, isContent ? null : Path.GetDirectoryName(pathOrContent)).ToList();
        currentIndex = -1;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public async Task PlayAsync(int index = 0, CancellationToken cancellationToken = default)
    {
        if (tracks.Count == 0)
            return;
        currentIndex = Math.Clamp(index, 0, tracks.Count - 1);
        await js.InvokeVoidAsync("coverPlayer.loadAndPlay", cancellationToken, tracks[currentIndex].Source, volume);
        isPlaying = true;
        isPaused = false;
        await UpdateStateAsync(cancellationToken);
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        await js.InvokeVoidAsync("coverPlayer.pause", cancellationToken);
        isPlaying = false;
        isPaused = true;
        await UpdateStateAsync(cancellationToken);
    }

    public async Task SeekAsync(double seconds, CancellationToken cancellationToken = default)
    {
        position = Math.Clamp(seconds, 0, Duration);
        await js.InvokeVoidAsync("coverPlayer.seek", cancellationToken, position);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task NextAsync(CancellationToken cancellationToken = default) =>
        tracks.Count == 0 ? Task.CompletedTask : PlayAsync((currentIndex + 1) % tracks.Count, cancellationToken);

    public Task PreviousAsync(CancellationToken cancellationToken = default) =>
        tracks.Count == 0 ? Task.CompletedTask : PlayAsync((currentIndex - 1 + tracks.Count) % tracks.Count, cancellationToken);

    public Task RestartAsync(CancellationToken cancellationToken = default) => SeekAsync(0, cancellationToken);

    private async Task UpdateStateAsync(CancellationToken cancellationToken)
    {
        var state = await js.InvokeAsync<AudioState>("coverPlayer.state", cancellationToken);
        position = state.Position;
        duration = state.Duration;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed record AudioState(double Position, double Duration);
}
