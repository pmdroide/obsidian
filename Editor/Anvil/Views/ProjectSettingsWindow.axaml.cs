using Anvil.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Anvil.Views;

/// <summary>Modal Project Settings dialog. Closes with <c>true</c> when the user confirmed Delete Project.</summary>
public partial class ProjectSettingsWindow : Window
{
    public ProjectSettingsWindow()
    {
        InitializeComponent();
    }

    private void ConfirmDelete_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProjectSettingsViewModel { IsConfirmationValid: true }) Close(true);
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close(false);
}
