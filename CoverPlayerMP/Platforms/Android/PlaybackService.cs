using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace CoverPlayerMP.Platforms.Android;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
public sealed class PlaybackService : Service
{
    private const string ChannelId = "playback";
    private const int NotificationId = 1;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            manager?.CreateNotificationChannel(new NotificationChannel(ChannelId, "Wiedergabe", NotificationImportance.Low));

        var launch = PackageManager?.GetLaunchIntentForPackage(PackageName!);
        var pending = PendingIntent.GetActivity(this, 0, launch, PendingIntentFlags.Immutable);
        var notification = new Notification.Builder(this, ChannelId)
            .SetContentTitle("CoverPlayerMP")
            .SetContentText("Wiedergabe läuft")
            .SetSmallIcon(ApplicationInfo!.Icon)
            .SetContentIntent(pending)
            .SetOngoing(true)
            .Build();

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            StartForeground(NotificationId, notification, ForegroundService.TypeMediaPlayback);
        else
            StartForeground(NotificationId, notification);
        return StartCommandResult.Sticky;
    }

    public static void Start()
    {
        var context = Platform.AppContext;
        context.StartForegroundService(new Intent(context, typeof(PlaybackService)));
    }

    public static void Stop()
    {
        var context = Platform.AppContext;
        context.StopService(new Intent(context, typeof(PlaybackService)));
    }
}
