using System;
using System.Linq;
using Anvil.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Anvil.Views;

/// <summary>Generate Material dialog (see <see cref="GenerateMaterialViewModel"/>). It stays open after an import.</summary>
public partial class GenerateMaterialWindow : Window
{
    public GenerateMaterialWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is not GenerateMaterialViewModel vm) return;
            vm.PickPhoto = PickPhotoAsync;
            await vm.InitializeAsync();
        };
        Closed += (_, _) => (DataContext as GenerateMaterialViewModel)?.Dispose();
    }

    private async System.Threading.Tasks.Task<string?> PickPhotoAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a photo or texture",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tga" } },
            },
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
