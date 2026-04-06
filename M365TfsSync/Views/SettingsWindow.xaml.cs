using System.Windows;
using M365TfsSync.ViewModels;

namespace M365TfsSync.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () => Close();
    }
}
