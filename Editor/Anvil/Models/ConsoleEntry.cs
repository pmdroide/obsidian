using CommunityToolkit.Mvvm.ComponentModel;

namespace Anvil.Models;

public enum ConsoleLevel
{
    Log,
    Warn,
    Error,
}

public partial class ConsoleEntry : ObservableObject
{
    [ObservableProperty] private int _id;
    [ObservableProperty] private ConsoleLevel _level = ConsoleLevel.Log;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private string _source = string.Empty;
    [ObservableProperty] private string _time = string.Empty;
}
