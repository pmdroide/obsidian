using System;
using System.Threading.Tasks;
using Anvil.Mcp;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Engine.Editor;

namespace Anvil.Models;

public partial class McpSettingsViewModel : ObservableObject
{
    private readonly EditorMcpSettingsStore _store = new();
    private readonly EditorAutomationService _automation = new();
    private readonly EditorMcpHost _host;
    private DispatcherTimer? _timer;
    private bool _attached;
    private bool _closing;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _isReadOnly = true;
    [ObservableProperty] private decimal _port = 47831;
    [ObservableProperty] private string _token = "";
    [ObservableProperty] private string _serverStatus = "Disabled";
    [ObservableProperty] private string _endpoint = "http://127.0.0.1:47831/mcp";
    [ObservableProperty] private string _recentOperations = "No commands yet.";
    [ObservableProperty] private bool _isBusy;
    public string InstanceId => _automation.InstanceId;
    public string SettingsPath => _store.FilePath;

    public McpSettingsViewModel()
    {
        _host = new(_automation);
        try
        {
            var settings = _store.Load();
            IsEnabled = settings.Enabled;
            IsReadOnly = settings.ReadOnly;
            Port = settings.Port;
            Token = settings.Token;
            Endpoint = $"http://127.0.0.1:{settings.Port}/mcp";
            if (IsEnabled) ServerStatus = "Waiting for the engine.";
        }
        catch (Exception)
        {
            Token = EditorMcpSettings.NewToken();
            ServerStatus = "Could not read MCP settings. Apply to save new settings.";
        }
    }

    public void AttachBridge(IEditorBridge bridge)
    {
        if (_closing) return;
        _automation.AttachBridge(bridge);
        _attached = true;
        if (IsEnabled) _ = ApplyAsync();
        if (_timer == null)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) =>
            {
                var recent = _automation.RecentOperations;
                RecentOperations = recent.Length == 0 ? "No commands yet." : string.Join(Environment.NewLine, recent);
            };
            _timer.Start();
        }
    }

    partial void OnIsReadOnlyChanged(bool value) => _automation.ReadOnly = value;
    partial void OnIsEnabledChanged(bool value)
    {
        if (!value) _automation.Stop();
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (IsBusy || _closing) return;
        if (Port is < 1024 or > 65535 || decimal.Truncate(Port) != Port)
        {
            ServerStatus = "Enter a whole port between 1024 and 65535.";
            return;
        }
        IsBusy = true;
        try
        {
            await _host.StopAsync();
            if (_closing) return;
            _store.Save(new() { Enabled = IsEnabled, ReadOnly = IsReadOnly, Port = (int)Port, Token = Token });
            _automation.ReadOnly = IsReadOnly;
            Endpoint = $"http://127.0.0.1:{(int)Port}/mcp";
            if (IsEnabled && _attached)
            {
                await _host.StartAsync((int)Port, Token);
                ServerStatus = "Listening on this computer.";
            }
            else ServerStatus = IsEnabled ? "Waiting for the engine." : "Disabled";
        }
        catch (Exception ex)
        {
            _automation.Stop();
            ServerStatus = "MCP could not start or save settings. Check the port and local settings folder.";
            EditorBridge.Log("MCP host failure: " + ex.GetType().Name);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RotateTokenAsync()
    {
        if (IsBusy || _closing) return;
        Token = EditorMcpSettings.NewToken();
        await ApplyAsync();
    }

    public void BeginShutdown()
    {
        _closing = true;
        _timer?.Stop();
        _automation.Stop(detach: true);
    }

    public async Task ShutdownAsync()
    {
        BeginShutdown();
        await _host.StopAsync();
    }
}
