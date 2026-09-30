namespace CoverPlayerMP;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
    }

    protected override bool OnBackButtonPressed() =>
        CoverPlayerMP.Shared.Player.BackNavigation.TryHandle() || base.OnBackButtonPressed();
}
