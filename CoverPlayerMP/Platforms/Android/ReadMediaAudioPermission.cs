using Android;
using Android.OS;

namespace CoverPlayerMP.Platforms.Android;

public sealed class ReadMediaAudioPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
            ? [(Manifest.Permission.ReadMediaAudio, true)]
            : [(Manifest.Permission.ReadExternalStorage, true)];
}
