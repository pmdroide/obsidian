using Engine.Networking;

namespace Engine.Scripting;

public abstract partial class ScriptBehaviour
{
    /// <summary>The session this script owns, or null. Share this reference with other gameplay scripts.</summary>
    public NetworkSession Network { get; private set; }

    /// <summary>
    /// Creates a Steam-backed session. Connect Steam first, then call Host/FindAndJoin/Join.
    /// It polls before this script's Update and is disposed automatically when the script stops.
    /// </summary>
    public NetworkSession CreateNetwork(string gameKey, int tickRate = 20)
    {
        RequireNoNetwork();
        if (tickRate < 1 || tickRate > 120) throw new ArgumentOutOfRangeException(nameof(tickRate));
        return CreateNetwork(new SteamNetworkTransport(gameKey), tickRate);
    }

    /// <summary>Creates a session using a custom transport; the session owns and disposes it.</summary>
    public NetworkSession CreateNetwork(INetworkTransport transport, int tickRate = 20)
    {
        RequireNoNetwork();
        Network = new NetworkSession(transport, tickRate);
        return Network;
    }

    private void RequireNoNetwork()
    {
        if (Network is { IsDisposed: false }) throw new InvalidOperationException("This script already owns a network session.");
    }
}
