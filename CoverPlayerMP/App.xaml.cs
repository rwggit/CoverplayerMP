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
        window.Destroying += async (_, _) =>
        {
            if (IPlatformApplication.Current?.Services.GetService<CoverPlayerMP.Shared.Player.IPlaylistPlayer>() is IAsyncDisposable player)
                await player.DisposeAsync();
        };
#if WINDOWS
        window.Created += (_, _) =>
        {
            if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
                return;

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

            void UpdateTitleBar()
            {
                switch (appWindow.Presenter)
                {
                    case Microsoft.UI.Windowing.OverlappedPresenter presenter:
                        var maximized = presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized;
                        if (presenter.HasTitleBar == maximized)
                            presenter.SetBorderAndTitleBar(!maximized, !maximized);
                        break;
                }
            }

            appWindow.Changed += (_, args) =>
            {
                if (args.DidPresenterChange || args.DidSizeChange)
                    nativeWindow.DispatcherQueue.TryEnqueue(UpdateTitleBar);
            };
            UpdateTitleBar();
        };
#endif
        return window;
    }
}
