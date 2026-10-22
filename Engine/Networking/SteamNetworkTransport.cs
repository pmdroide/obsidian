using Engine.Steam;

namespace Engine.Networking;

/// <summary>Lazy Steam lobby transport. One scripting transport owns the process lobby at a time.</summary>
public sealed class SteamNetworkTransport : INetworkTransport
{
    // Keep the legacy MultiplayerTest's channel 0 separate from scripting packets.
    private const int Channel = 17185;
    private static SteamNetworkTransport _active;
    private readonly string _gameKey;
    private SteamP2PSession _session;
    private bool _disposed;
    private static bool Ready => SteamService.Current?.Status is { IsConnected: true, IsLoggedOn: true };
    public bool IsConnected => !_disposed && Ready && _session?.InLobby == true;
    public ulong LocalId => !_disposed && Ready ? _session?.LocalId ?? 0 : 0;
    public ulong SessionId => IsConnected ? _session.LobbyId : 0;
    public ulong ServerId => IsConnected ? _session.OwnerId : 0;
    public IReadOnlyList<ulong> Peers => IsConnected ? _session.Peers : Array.Empty<ulong>();
    public string Status => _disposed ? "Network session disposed." : !Ready
        ? "Connect Steam and sign in before hosting or joining." : _session?.Status ?? "Not in a lobby.";

    public SteamNetworkTransport(string gameKey)
    {
        if (string.IsNullOrWhiteSpace(gameKey)) throw new ArgumentException("A game key is required.", nameof(gameKey));
        if (_active != null) throw new InvalidOperationException("Share one NetworkSession between scripts; a Steam scripting session already exists.");
        _gameKey = gameKey;
        _active = this;
    }
    private bool EnsureSession()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Ready) return false;
        _session ??= new SteamP2PSession(_gameKey);
        return true;
    }
    public void Host(int maxPlayers) { if (EnsureSession()) _session.Host(maxPlayers); }
    public void FindAndJoin() { if (EnsureSession()) _session.FindAndJoin(); }
    public void Join(ulong sessionId) { if (EnsureSession()) _session.Join(sessionId); }
    public void Leave() { if (!_disposed && Ready) _session?.Leave(); }
    public bool InviteFriends() => IsConnected && _session.InviteFriends();
    public string NameOf(ulong peer) => !_disposed && Ready && _session != null ? _session.NameOf(peer) : peer.ToString();
    public int PingMs(ulong peer) => IsConnected ? _session.GetPeerLink(peer).PingMs : -1;
    public bool Send(ulong peer, byte[] data, int length, bool reliable) =>
        IsConnected && Peers.Contains(peer) && _session.Send(peer, data, length, reliable, Channel);
    public void Poll(Action<ulong, byte[], int> receive, int maxMessages)
    {
        if (_disposed) return;
        if (!Ready)
        {
            _session?.Dispose();
            _session = null;
            return;
        }
        // Register Steam callbacks while idle too, so accepting a friend's lobby invite works
        // before this player has ever pressed Host or FindAndJoin.
        if (EnsureSession()) _session.Receive(receive, Channel, maxMessages, NetworkSession.MaxPacketSize);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _session?.Dispose(); }
        finally { _session = null; if (_active == this) _active = null; }
    }
}
