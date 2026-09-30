namespace CoverPlayerMP.Shared.Player;

public sealed record LibraryTrack(Track Track, TimeSpan Duration);

public enum TrackInsertPosition
{
    Start,
    BeforeCurrent,
    AfterCurrent,
    AfterCurrentAndPlay,
    End
}
