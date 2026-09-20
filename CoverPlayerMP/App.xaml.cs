namespace CoverPlayerMP;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage()) { Title = "CoverPlayerMP" };
#if WINDOWS
        window.Created += (_, _) =>
        {
            if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
                return;

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                presenter.SetBorderAndTitleBar(false, false);
        };
#endif
        return window;
    }
}
