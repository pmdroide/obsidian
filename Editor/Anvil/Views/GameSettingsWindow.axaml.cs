using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Anvil.Views;

/// <summary>Modal Game Settings dialog. Closes with <c>true</c> when the user saves.</summary>
public partial class GameSettingsWindow : Window
{
    public GameSettingsWindow()
    {
        InitializeComponent();
    }

    private void Save_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
