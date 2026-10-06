using CoverPlayerMP.Shared.Player;

namespace CoverPlayerMP.Platforms.Windows;

public sealed class WindowControls : IWindowControls
{
    public bool IsFullScreen => GetAppWindow()?.Presenter switch
    {
        Microsoft.UI.Windowing.OverlappedPresenter p => p.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized,
        Microsoft.UI.Windowing.FullScreenPresenter => true,
        _ => false,
    };

    public void Restore()
    {
        if (GetAppWindow()?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            presenter.Restore();
    }

    public void Close() => Application.Current?.Quit();

    private static Microsoft.UI.Windowing.AppWindow? GetAppWindow()
    {
        var window = Application.Current?.Windows.FirstOrDefault();
        if (window?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
            return null;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        return Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
    }
}
