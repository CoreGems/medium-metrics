using System.Windows;
using MediumMetrics.ViewModels;

namespace MediumMetrics;

/// <summary>
/// Interaction logic for MainWindow.xaml. Owns no logic beyond wiring the view
/// model and routing the Sign-in button to the app's login flow (which needs a
/// Window owner for the modal WebView2 dialog).
/// </summary>
public partial class MainWindow : Window
{
    private readonly App _app;

    public MainWindow(App app, MainViewModel viewModel)
    {
        _app = app;
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnSignInClick(object sender, RoutedEventArgs e)
    {
        _app.SignIn(this);
    }
}
