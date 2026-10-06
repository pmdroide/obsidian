using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Editor;
using Engine.Steam;

namespace Anvil.Models;

public partial class SteamViewModel : ObservableObject
{
    private IEditorBridge? _bridge;
    private bool _suppress;
    private uint _lastAppId;
    [ObservableProperty] private string _statusMessage = "Waiting for the engine Steam service.";
    [ObservableProperty] private string _personaName = "-";
    [ObservableProperty] private string _steamId = "-";
    [ObservableProperty] private string _connectionState = "Disconnected";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(DisconnectCommand))]
    private bool _isConnected;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditSettings), nameof(CanToggleSteam))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(ApplyAppIdCommand), nameof(DisconnectCommand))]
    private bool _isReady;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggleSteam))]
    private bool _isEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValidAppId), nameof(CanEditSettings), nameof(CanToggleSteam))]
    [NotifyCanExecuteChangedFor(nameof(ApplyAppIdCommand), nameof(ConnectCommand))]
    private decimal _appId = SteamService.SpacewarAppId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditSettings), nameof(CanToggleSteam))]
    [NotifyCanExecuteChangedFor(nameof(ApplyAppIdCommand), nameof(ConnectCommand), nameof(DisconnectCommand))]
    private bool _isBusy;

    public bool IsValidAppId => AppId >= 1 && AppId <= uint.MaxValue && decimal.Truncate(AppId) == AppId;
    public bool CanEditSettings => IsReady && !IsBusy && IsValidAppId;
    public bool CanToggleSteam => IsReady && !IsBusy && (IsEnabled || IsValidAppId);
    private bool CanConnect => CanEditSettings && !IsConnected;
    private bool CanDisconnect => IsReady && !IsBusy && IsConnected;

    public void AttachBridge(IEditorBridge bridge)
    {
        _bridge = bridge;
        IsReady = true;
        Refresh();
    }

    public void Refresh()
    {
        if (_bridge == null || IsBusy) return;
        var status = _bridge.SteamStatus;
        _suppress = true;
        try
        {
            IsEnabled = status.IsEnabled;
            // Preserve an unsaved App ID while snapshots refresh the account/status.
            if (_lastAppId != status.AppId)
            {
                AppId = status.AppId;
                _lastAppId = status.AppId;
            }
        }
        finally { _suppress = false; }
        IsConnected = status.IsConnected;
        StatusMessage = status.Message;
        PersonaName = string.IsNullOrEmpty(status.PersonaName) ? "-" : status.PersonaName;
        SteamId = status.SteamId == 0 ? "-" : status.SteamId.ToString(CultureInfo.InvariantCulture);
        ConnectionState = !status.IsEnabled ? "Disabled" : !status.IsConnected ? "Disconnected" : status.IsLoggedOn ? "Online" : "Offline";
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_suppress) ApplySettings(value);
    }

    private void ApplySettings(bool enabled)
    {
        if (_bridge == null || IsBusy || (enabled && !IsValidAppId)) return;
        uint appId = IsValidAppId ? (uint)AppId : _lastAppId;
        IsBusy = true;
        _bridge.EnqueueSteamSettings(enabled, appId, () =>
        {
            void Complete()
            {
                IsBusy = false;
                // Also restore the saved ID when persistence failed.
                _lastAppId = 0;
                Refresh();
            }
            if (Dispatcher.UIThread.CheckAccess()) Complete();
            else Dispatcher.UIThread.Post(Complete);
        });
    }

    [RelayCommand(CanExecute = nameof(CanEditSettings))]
    private void ApplyAppId() => ApplySettings(IsEnabled);

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private void Connect() => ApplySettings(true);

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private void Disconnect() => ApplySettings(false);
}
