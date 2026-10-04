namespace CoverPlayerMP.Platforms.Android;

public sealed class LocalNetworkPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        global::Android.OS.Build.VERSION.SdkInt >= (global::Android.OS.BuildVersionCodes)36
            ? [("android.permission.ACCESS_LOCAL_NETWORK", true)]
            : [];
}
