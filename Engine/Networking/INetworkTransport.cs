namespace Engine.Networking;

/// <summary>Game-thread transport seam. Receive buffers are borrowed only for the callback.</summary>
public interface INetworkTransport : IDisposable
{
    bool IsConnected { get; }
    ulong LocalId { get; }
    ulong SessionId { get; }
    ulong ServerId { get; }
    IReadOnlyList<ulong> Peers { get; }
    string Status { get; }
    void Host(int maxPlayers);
    void FindAndJoin();
    void Join(ulong sessionId);
    void Leave();
    bool InviteFriends();
    string NameOf(ulong peer);
    int PingMs(ulong peer);
    bool Send(ulong peer, byte[] data, int length, bool reliable);
    /// <summary>Poll connection state and deliver at most maxMessages packets, including rejected packets.</summary>
    void Poll(Action<ulong, byte[], int> receive, int maxMessages);
}

public enum NetworkDelivery { Reliable, UnreliableSequenced }
