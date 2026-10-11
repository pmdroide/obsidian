using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Engine.Editor;
using Steamworks;

namespace Engine.Steam
{
    public enum SteamLobbyState { Idle, Creating, Searching, Joining, InLobby }

    /// <summary>
    /// Peer-to-peer play over a Steam lobby. The lobby says who is playing; every member sends
    /// straight to every other member with <c>SteamNetworkingMessages</c>, relayed through Valve's
    /// network, so there are no ports to open and no dedicated server.
    ///
    /// Create it on the game thread once <see cref="SteamService.Status"/> is connected and logged
    /// on, and dispose it when done (it leaves the lobby). Lobby results and peer changes arrive
    /// from the callback pump in <see cref="SteamService.Update"/>; call <see cref="Receive"/> each frame.
    /// </summary>
    public sealed class SteamP2PSession : IDisposable
    {
        /// <summary>Lobby data key holding the game key, so searches only find lobbies of this game.</summary>
        public const string GameKeyData = "obsidian_game";
        public const string HostNameData = "host_name";
        private const int ReceiveBatch = 64;

        private readonly string _gameKey;
        private readonly Callback<LobbyChatUpdate_t> _chatUpdate;
        private readonly Callback<GameLobbyJoinRequested_t> _joinRequested;
        private readonly Callback<SteamNetworkingMessagesSessionRequest_t> _sessionRequest;
        private readonly Callback<SteamNetworkingMessagesSessionFailed_t> _sessionFailed;
        private readonly CallResult<LobbyCreated_t> _lobbyCreated;
        private readonly CallResult<LobbyEnter_t> _lobbyEntered;
        private readonly CallResult<LobbyMatchList_t> _lobbyList;
        private readonly List<ulong> _peers = new();
        // Session requests from players we don't see in the lobby yet; accepted once they appear.
        private readonly HashSet<ulong> _pendingRequests = new();
        private readonly IntPtr[] _incoming = new IntPtr[ReceiveBatch];
        private byte[] _receiveBuffer = new byte[1200];
        private CSteamID _lobby = CSteamID.Nil;
        private bool _disposed;

        public SteamLobbyState State { get; private set; } = SteamLobbyState.Idle;
        /// <summary>One line describing the last lobby event, for a HUD or log.</summary>
        public string Status { get; private set; } = "Not in a lobby.";
        public ulong LocalId { get; }
        public ulong LobbyId => _lobby.m_SteamID;
        public bool InLobby => State == SteamLobbyState.InLobby;
        public ulong OwnerId => InLobby ? SteamMatchmaking.GetLobbyOwner(_lobby).m_SteamID : 0;
        public bool IsOwner => InLobby && OwnerId == LocalId;
        /// <summary>The other lobby members, in join order.</summary>
        public IReadOnlyList<ulong> Peers => _peers;

        /// <summary>Raised when another player enters the lobby (also for players already there when you join).</summary>
        public event Action<ulong> PeerJoined;
        /// <summary>Raised when another player leaves, and for every peer when you leave.</summary>
        public event Action<ulong> PeerLeft;

        /// <param name="gameKey">Lobbies are tagged with this and searches only match it (App 480 is shared by every developer).</param>
        public SteamP2PSession(string gameKey)
        {
            if (string.IsNullOrWhiteSpace(gameKey)) throw new ArgumentException("A game key is required.", nameof(gameKey));
            _gameKey = gameKey;
            LocalId = SteamUser.GetSteamID().m_SteamID;
            // Fetches the relay network config now, so the first connection doesn't wait for it.
            SteamNetworkingUtils.InitRelayNetworkAccess();

            _chatUpdate = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
            _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);
            _sessionRequest = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(OnSessionRequest);
            _sessionFailed = Callback<SteamNetworkingMessagesSessionFailed_t>.Create(OnSessionFailed);
            _lobbyCreated = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            _lobbyEntered = CallResult<LobbyEnter_t>.Create(OnLobbyEntered);
            _lobbyList = CallResult<LobbyMatchList_t>.Create(OnLobbyList);
        }

        public string NameOf(ulong steamId) =>
            steamId == LocalId ? SteamFriends.GetPersonaName() : SteamFriends.GetFriendPersonaName(new CSteamID(steamId));

        ////////////////////////////////////////////////////////////////////////////////
        //  LOBBY
        ////////////////////////////////////////////////////////////////////////////////

        /// <summary>Creates a public lobby tagged with the game key and enters it.</summary>
        public void Host(int maxMembers = 4)
        {
            if (!CanStart()) return;
            SetState(SteamLobbyState.Creating, "Creating a lobby...");
            _lobbyCreated.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, Math.Clamp(maxMembers, 2, 250)));
        }

        /// <summary>Searches for a lobby with this game key and a free slot, and joins the first one.</summary>
        public void FindAndJoin()
        {
            if (!CanStart()) return;
            SetState(SteamLobbyState.Searching, "Searching for a lobby...");
            SteamMatchmaking.AddRequestLobbyListStringFilter(GameKeyData, _gameKey, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable(1);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            _lobbyList.Set(SteamMatchmaking.RequestLobbyList());
        }

        public void Join(ulong lobbyId)
        {
            if (_disposed || lobbyId == 0) return;
            if (State != SteamLobbyState.Idle) Leave();
            SetState(SteamLobbyState.Joining, "Joining a lobby...");
            _lobbyEntered.Set(SteamMatchmaking.JoinLobby(new CSteamID(lobbyId)));
        }

        /// <summary>Leaves the lobby (or cancels a pending create/search/join) and closes peer sessions.</summary>
        public void Leave()
        {
            if (State == SteamLobbyState.Idle) return;
            _lobbyCreated.Cancel();
            _lobbyEntered.Cancel();
            _lobbyList.Cancel();
            if (_lobby != CSteamID.Nil) SteamMatchmaking.LeaveLobby(_lobby);
            _lobby = CSteamID.Nil;
            _pendingRequests.Clear();
            foreach (ulong peer in _peers.ToArray()) RemovePeer(peer);
            SetState(SteamLobbyState.Idle, "Left the lobby.");
        }

        /// <summary>Opens the Steam overlay's invite dialog for this lobby. False when not in a lobby.</summary>
        public bool InviteFriends()
        {
            if (!InLobby) return false;
            SteamFriends.ActivateGameOverlayInviteDialog(_lobby);
            return true;
        }

        private bool CanStart()
        {
            if (_disposed) return false;
            if (State == SteamLobbyState.Idle) return true;
            Status = InLobby ? "Already in a lobby. Leave it first." : "Busy, wait for the current request.";
            return false;
        }

        private void OnLobbyCreated(LobbyCreated_t result, bool ioFailure)
        {
            if (State != SteamLobbyState.Creating) return;
            if (ioFailure || result.m_eResult != EResult.k_EResultOK)
            {
                SetState(SteamLobbyState.Idle, $"Couldn't create a lobby ({(ioFailure ? "I/O failure" : result.m_eResult.ToString())}).");
                return;
            }
            _lobby = new CSteamID(result.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(_lobby, GameKeyData, _gameKey);
            SteamMatchmaking.SetLobbyData(_lobby, HostNameData, SteamFriends.GetPersonaName());
            SetState(SteamLobbyState.InLobby, "Hosting a lobby. Waiting for players.");
            RefreshPeers();
        }

        private void OnLobbyList(LobbyMatchList_t result, bool ioFailure)
        {
            if (State != SteamLobbyState.Searching) return;
            if (ioFailure || result.m_nLobbiesMatching == 0)
            {
                SetState(SteamLobbyState.Idle, ioFailure ? "Lobby search failed." : "No open lobby found. Host one instead.");
                return;
            }
            CSteamID lobby = SteamMatchmaking.GetLobbyByIndex(0);
            SetState(SteamLobbyState.Joining, $"Joining {SteamMatchmaking.GetLobbyData(lobby, HostNameData)}'s lobby...");
            _lobbyEntered.Set(SteamMatchmaking.JoinLobby(lobby));
        }

        private void OnLobbyEntered(LobbyEnter_t result, bool ioFailure)
        {
            if (State != SteamLobbyState.Joining) return;
            var response = (EChatRoomEnterResponse)result.m_EChatRoomEnterResponse;
            if (ioFailure || response != EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                if (!ioFailure && result.m_ulSteamIDLobby != 0) SteamMatchmaking.LeaveLobby(new CSteamID(result.m_ulSteamIDLobby));
                SetState(SteamLobbyState.Idle, $"Couldn't join the lobby ({(ioFailure ? "I/O failure" : response.ToString())}).");
                return;
            }
            _lobby = new CSteamID(result.m_ulSteamIDLobby);
            SetState(SteamLobbyState.InLobby, $"Joined {SteamMatchmaking.GetLobbyData(_lobby, HostNameData)}'s lobby.");
            RefreshPeers();
        }

        private void OnLobbyChatUpdate(LobbyChatUpdate_t update)
        {
            if (!InLobby || update.m_ulSteamIDLobby != _lobby.m_SteamID) return;
            RefreshPeers();
        }

        /// <summary>The player accepted an invite or chose Join Game in the Steam friends list.</summary>
        private void OnJoinRequested(GameLobbyJoinRequested_t request)
        {
            if (request.m_steamIDLobby == _lobby) return;
            Join(request.m_steamIDLobby.m_SteamID);
        }

        /// <summary>Diffs the lobby's member list against <see cref="Peers"/> and raises join/leave events.</summary>
        private void RefreshPeers()
        {
            var current = new List<ulong>();
            int count = SteamMatchmaking.GetNumLobbyMembers(_lobby);
            for (int i = 0; i < count; i++)
            {
                ulong id = SteamMatchmaking.GetLobbyMemberByIndex(_lobby, i).m_SteamID;
                if (id != LocalId && id != 0) current.Add(id);
            }
            foreach (ulong peer in _peers.ToArray())
                if (!current.Contains(peer)) RemovePeer(peer);
            foreach (ulong peer in current)
            {
                if (_peers.Contains(peer)) continue;
                _peers.Add(peer);
                if (_pendingRequests.Remove(peer)) Accept(peer);
                Status = $"{NameOf(peer)} joined.";
                Raise(PeerJoined, peer);
            }
            if (InLobby && _peers.Count == 0 && IsOwner) Status = "Hosting a lobby. Waiting for players.";
        }

        private void RemovePeer(ulong peer)
        {
            if (!_peers.Remove(peer)) return;
            var identity = Identity(peer);
            SteamNetworkingMessages.CloseSessionWithUser(ref identity);
            Status = $"{NameOf(peer)} left.";
            Raise(PeerLeft, peer);
        }

        private void SetState(SteamLobbyState state, string status)
        {
            State = state;
            Status = status;
            EditorBridge.Log("Steam P2P: " + status);
        }

        private static void Raise(Action<ulong> handler, ulong peer)
        {
            try { handler?.Invoke(peer); }
            catch (Exception ex) { EditorBridge.Log("Steam P2P: peer event handler threw: " + ex); }
        }

        ////////////////////////////////////////////////////////////////////////////////
        //  MESSAGES
        ////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Sends <paramref name="length"/> bytes to one peer. Unreliable messages may be dropped or
        /// arrive out of order (use them for state sent every frame); reliable ones arrive once, in order.
        /// </summary>
        public bool Send(ulong peer, byte[] data, int length, bool reliable, int channel = 0)
        {
            if (!InLobby || data == null || length <= 0 || length > data.Length) return false;
            int flags = (reliable ? Constants.k_nSteamNetworkingSend_Reliable : Constants.k_nSteamNetworkingSend_Unreliable)
                        | Constants.k_nSteamNetworkingSend_AutoRestartBrokenSession;
            var identity = Identity(peer);
            GCHandle pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                // Steam copies the payload before returning.
                return SteamNetworkingMessages.SendMessageToUser(ref identity, pinned.AddrOfPinnedObject(),
                    (uint)length, flags, channel) == EResult.k_EResultOK;
            }
            finally { pinned.Free(); }
        }

        /// <summary>Sends to every peer in the lobby.</summary>
        public void Broadcast(byte[] data, int length, bool reliable, int channel = 0)
        {
            foreach (ulong peer in _peers) Send(peer, data, length, reliable, channel);
        }

        /// <summary>
        /// Delivers every waiting message from current lobby members to <paramref name="handler"/>
        /// (sender, buffer, length). The buffer is reused, so copy what you keep. Returns the count.
        /// </summary>
        public int Receive(Action<ulong, byte[], int> handler, int channel = 0)
        {
            if (_disposed || handler == null) return 0;
            int delivered = 0, received;
            do
            {
                received = SteamNetworkingMessages.ReceiveMessagesOnChannel(channel, _incoming, _incoming.Length);
                for (int i = 0; i < received; i++)
                {
                    try
                    {
                        var message = SteamNetworkingMessage_t.FromIntPtr(_incoming[i]);
                        ulong sender = message.m_identityPeer.GetSteamID64();
                        // Late packets from someone who already left are dropped.
                        if (!_peers.Contains(sender) || message.m_cbSize <= 0) continue;
                        if (_receiveBuffer.Length < message.m_cbSize) _receiveBuffer = new byte[message.m_cbSize];
                        Marshal.Copy(message.m_pData, _receiveBuffer, 0, message.m_cbSize);
                        delivered++;
                        handler(sender, _receiveBuffer, message.m_cbSize);
                    }
                    finally { SteamNetworkingMessage_t.Release(_incoming[i]); }
                }
            } while (received == _incoming.Length);
            return delivered;
        }

        /// <summary>The connection to a peer: "Connected", "Connecting", "Finding route"... and the ping in ms (-1 when unknown).</summary>
        public (string State, int PingMs) GetPeerLink(ulong peer)
        {
            if (!InLobby || !_peers.Contains(peer)) return ("Not connected", -1);
            var identity = Identity(peer);
            var state = SteamNetworkingMessages.GetSessionConnectionInfo(ref identity, out _, out SteamNetConnectionRealTimeStatus_t live);
            string text = state switch
            {
                ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected => "Connected",
                ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting => "Connecting",
                ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_FindingRoute => "Finding route",
                ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_None => "Waiting",
                _ => "Problem",
            };
            return (text, state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected ? live.m_nPing : -1);
        }

        private void OnSessionRequest(SteamNetworkingMessagesSessionRequest_t request)
        {
            ulong id = request.m_identityRemote.GetSteamID64();
            // Only lobby members may talk to us; someone not listed yet is accepted when they appear.
            if (_peers.Contains(id)) Accept(id);
            else if (InLobby) _pendingRequests.Add(id);
        }

        private void OnSessionFailed(SteamNetworkingMessagesSessionFailed_t failure)
        {
            ulong id = failure.m_info.m_identityRemote.GetSteamID64();
            EditorBridge.Log($"Steam P2P: session with {id} failed: {failure.m_info.m_szEndDebug}");
        }

        private static void Accept(ulong peer)
        {
            var identity = Identity(peer);
            SteamNetworkingMessages.AcceptSessionWithUser(ref identity);
        }

        private static SteamNetworkingIdentity Identity(ulong steamId)
        {
            var identity = new SteamNetworkingIdentity();
            identity.SetSteamID64(steamId);
            return identity;
        }

        public void Dispose()
        {
            if (_disposed) return;
            // Steam may already be shut down (the Steam client closed, or Steam was disabled).
            if (SteamService.Current?.Status.IsConnected != false)
            {
                try { Leave(); }
                catch (Exception ex) { EditorBridge.Log("Steam P2P: leave on dispose failed: " + ex.Message); }
            }
            _disposed = true;
            _chatUpdate.Dispose();
            _joinRequested.Dispose();
            _sessionRequest.Dispose();
            _sessionFailed.Dispose();
            _lobbyCreated.Dispose();
            _lobbyEntered.Dispose();
            _lobbyList.Dispose();
        }
    }
}
