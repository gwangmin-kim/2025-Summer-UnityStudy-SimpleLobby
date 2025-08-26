using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text;

using UnityEngine;

using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;

using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using System.Linq;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-1000)]
public class LobbyManager : MonoBehaviour {
    public static LobbyManager Instance { get; private set; }

    // for lobby ui setting
    private ILobbyUI lobbyUi;
    private MonoBehaviour lobbyUiMonobehaviour; // for null checking
    private bool HasUI => lobbyUi != null && lobbyUiMonobehaviour && lobbyUiMonobehaviour.gameObject;

    [Header("Game Scene(for transition)")]
    [SerializeField] string gameSceneName = "Game";

    private string MyId => AuthenticationService.Instance.PlayerId;
    private bool IsLobbyHost => joinedLobby != null && joinedLobby.HostId == MyId;

    private readonly Dictionary<string, ulong> authToClient = new();
    private readonly Dictionary<ulong, string> clientToAuth = new();

    private Lobby joinedLobby;
    private string lobbyCode;
    private string relayCode;

    private Coroutine heartbeatCoroutine;
    private Coroutine pollCoroutine;
    private float heartbeatDelay = 15f;
    private float pollDelay = 1.5f;

    private bool isReadyInFlight = false;
    private bool isStartInFlight = false;
    private bool isInGamePhase = false;

    private bool isClosingConnection = false;

    private void Awake() {
        // singleton
        if (Instance != null && Instance == this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private async void Start() {
        await EnsureServices();
    }

    private void OnEnable() {
        var nm = NetworkManager.Singleton;
        if (nm != null) {
            NetworkManager.Singleton.ConnectionApprovalCallback += ApprovalCheck;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }

        var sm = NetworkManager.Singleton?.SceneManager;
        if (sm != null) {
            sm.OnSceneEvent += OnNetworkSceneEvent;
        }
    }

    private void OnDisable() {
        authToClient.Clear();
        clientToAuth.Clear();

        var nm = NetworkManager.Singleton;
        if (nm != null) {
            NetworkManager.Singleton.ConnectionApprovalCallback -= ApprovalCheck;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        var sm = NetworkManager.Singleton?.SceneManager;
        if (sm != null) {
            sm.OnSceneEvent -= OnNetworkSceneEvent;
        }
    }

    private void OnDestroy() {
        if (Instance == this) {
            Instance = null;
        }
    }

    public void UI_Attach(ILobbyUI ui, MonoBehaviour asMonobehavior) {
        lobbyUi = ui;
        lobbyUiMonobehaviour = asMonobehavior;

        // apply current state
        lobbyUi.ShowFindSessionPanel(joinedLobby == null);
        lobbyUi.ShowLobbyPanel(joinedLobby != null);
        if (joinedLobby != null) {

        }
    }

    public void UI_Detach(ILobbyUI ui) {
        if (lobbyUi != ui) {
            Debug.LogWarning($"DetachUI: ui item don't match; {lobbyUi} != {ui}");
        }
        lobbyUi = null;
        lobbyUiMonobehaviour = null;
    }

    private void UI_UpdateAll() {
        if (!HasUI || joinedLobby == null) {
            return;
        }

        lobbyUi.SetLobbyHeader(joinedLobby);
        lobbyUi.RedrawPlayers(joinedLobby, IsLobbyHost);
        lobbyUi.SetButtons(IsLobbyHost);
    }

    // wrapper functions for ui
    public async Task HostFlow_FromUI(string playerName, string sessionName, int maxPlayers) {
        bool succeed = await HostFlow_Internal(playerName, sessionName, maxPlayers);

        if (succeed) {
            // set ui for host
            lobbyUi.ShowFindSessionPanel(false);
            lobbyUi.ShowLobbyPanel(true);
            UI_UpdateAll();
        }
    }

    public async Task JoinByCodeFlow_FromUI(string playerName, string code, Action<string> onError) {
        bool succeed = await JoinByCodeFlow_Internal(playerName, code, onError);

        if (succeed) {
            // set ui for client
            lobbyUi.ShowFindSessionPanel(false);
            lobbyUi.ShowLobbyPanel(true);
            UI_UpdateAll();
        }
    }

    public void CopyJoinCode_FromUI() {
        string code = joinedLobby?.LobbyCode;
        if (string.IsNullOrEmpty(code)) {
            return;
        }
        GUIUtility.systemCopyBuffer = code;
        lobbyUi.ShowCopiedText();
    }
    public async Task ToggleReady_FromUI() => await ToggleReady();
    public async Task StartGame_FromUI() => await StartGame();
    public async Task LeaveFlow_FromUI() => await LeaveFlow();
    public void KickPlayer_FromUI(string targetId) => KickPlayer(targetId);

    private async Task EnsureServices() {
        await UnityServices.InitializeAsync();

        AuthenticationService.Instance.SignedIn += () => {
            Debug.Log($"Signed in: {AuthenticationService.Instance.PlayerId}");
        };

        if (!AuthenticationService.Instance.IsSignedIn) {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }

    public void EnsureApprovalBoundBeforeStart() {
        var nm = NetworkManager.Singleton;
        if (!nm) {
            Debug.LogWarning("EnsureApprovalBoundBeforeStart: No NetworkManager found.");
            return;
        }

        nm.ConnectionApprovalCallback -= ApprovalCheck; // prevent redundancy
        nm.ConnectionApprovalCallback += ApprovalCheck;
        nm.OnClientDisconnectCallback -= OnClientDisconnected;
        nm.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request,
                               NetworkManager.ConnectionApprovalResponse response) {
        ulong clientId = request.ClientNetworkId;
        string authId = Encoding.UTF8.GetString(request.Payload ?? Array.Empty<byte>());

        Debug.Log($"clientId={clientId}; authId={authId}");

        // option: approval check
        bool ok = !string.IsNullOrEmpty(authId) && !isInGamePhase;

        response.Approved = ok;

        if (ok) {
            authToClient[authId] = clientId;
            clientToAuth[clientId] = authId;
        }
        else {
            if (string.IsNullOrEmpty(authId)) {
                response.Reason = "AuthId is Invalid. (NullOrEmpty)";
            }
            else if (isInGamePhase) {
                response.Reason = "Game is Already Started in this Session.";
            }
        }

        response.CreatePlayerObject = false;
        response.Pending = false;
    }

    private void OnClientDisconnected(ulong clientId) {
        var nm = NetworkManager.Singleton;
        if (!nm) {
            Debug.LogWarning("OnClientDisconnected: No NetworkManager found.");
            return;
        }

        Debug.Log($"clientId={clientId}; localId={nm.LocalClientId}");

        // server-side: cleanup mapping
        if (nm.IsServer && clientToAuth.TryGetValue(clientId, out string authId)) {
            clientToAuth.Remove(clientId);
            authToClient.Remove(authId);
        }
        // client-side: disconnect
        else if (clientId == NetworkManager.ServerClientId || clientId == nm.LocalClientId) {
            HandleSessionClosed("Disconnected from Server.");
        }
    }

    private void OnNetworkSceneEvent(SceneEvent e) {
        // all client loaded game scene
        if (e.SceneEventType == SceneEventType.LoadComplete && e.SceneName == gameSceneName) {
            Debug.Log("OnNetworkSceneEvent: All Clients Loaded the Game Scene.");
            // only host: round start trigger
            if (NetworkManager.Singleton.IsServer) {
                Debug.Log("OnNetworkSceneEvent: As a Host, Start Game.");
                // 예: RoundManager.Instance.BeginRound();
            }
        }
    }

    private async Task ToggleReady() {
        if (isReadyInFlight || joinedLobby == null) {
            return;
        }
        isReadyInFlight = true;

        try {
            Player me = joinedLobby.Players.FirstOrDefault(p => p.Id == MyId);
            bool isReady = !GetPlayerBool(me, "ready", false); // toggle

            string readyValue = isReady ? "true" : "false";
            var newPlayerData = new Dictionary<string, PlayerDataObject> {
                {"ready", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, readyValue)},
            };
            var updateOption = new UpdatePlayerOptions { Data = newPlayerData };
            await LobbyService.Instance.UpdatePlayerAsync(joinedLobby.Id, MyId, updateOption);
        }
        catch (LobbyServiceException e) {
            Debug.LogWarning($"ToggleReady: {e.Reason}");
        }
        catch (Exception e) {
            Debug.LogWarning($"ToggleReady: {e}");
        }
        finally {
            isReadyInFlight = false;
            await RefreshLobby();
        }
    }

    private async Task StartGame() {
        if (!IsLobbyHost || isStartInFlight || joinedLobby == null) {
            return;
        }
        if (!joinedLobby.Players.All(p => (p.Id == joinedLobby.HostId) || GetPlayerBool(p, "ready", false))) {
            Debug.Log("StartGame: all players need to be ready");
            return;
        }

        isStartInFlight = true;
        try {
            // update lobby state()
            var newLobbyData = new Dictionary<string, DataObject> {
                {"state", new DataObject(DataObject.VisibilityOptions.Public, "ingame")}
            };
            var updateOption = new UpdateLobbyOptions {
                IsLocked = true,
                Data = newLobbyData
            };
            await LobbyService.Instance.UpdateLobbyAsync(joinedLobby.Id, updateOption);

            NetworkSceneManager sceneManager = NetworkManager.Singleton.SceneManager;
            if (sceneManager == null) {
                Debug.LogError("NetworkSceneManager is null. Check Enable Scene Management.");
                return;
            }

            sceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);

            // to decline later-join request
            isInGamePhase = true;
        }
        catch (LobbyServiceException e) {
            Debug.LogWarning($"StartGame: {e.Reason}");
        }
        catch (Exception e) {
            Debug.LogWarning($"StartGame: {e}");
        }
        finally {
            isStartInFlight = false;
        }
    }

    private async Task<bool> HostFlow_Internal(string playerName, string sessionName, int maxPlayers) {
        try {
            // create relay allocation
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
            relayCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            Debug.Log($"Relay created. Code={relayCode}");

            // set relay to transport
            var utp = NetworkManager.Singleton.GetComponent<UnityTransport>();
            RelayServerData relayServerData = AllocationUtils.ToRelayServerData(allocation, "dtls");
            utp.SetRelayServerData(relayServerData);

            // set player, lobby data and lobby option
            var playerData = new Dictionary<string, PlayerDataObject> {
                {"name",  new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, playerName)},
                {"ready", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, "true")} // host는 언제나 ready 상태
            };

            // string lobbyName = $"{sessionName}-{UnityEngine.Random.Range(1000, 9999)}";
            string lobbyName = sessionName;
            var lobbyData = new Dictionary<string, DataObject> {
                {"joinCode", new DataObject(DataObject.VisibilityOptions.Member, relayCode, DataObject.IndexOptions.S1)},
                {"state", new DataObject(DataObject.VisibilityOptions.Public, "waiting", DataObject.IndexOptions.S2)}
            };

            var createLobbyOptions = new CreateLobbyOptions {
                IsPrivate = false,
                IsLocked = false,
                Data = lobbyData,
                Player = new Player(null, null, playerData)
            };

            // create lobby
            joinedLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, createLobbyOptions);
            lobbyCode = joinedLobby.LobbyCode;
            Debug.Log($"Created {lobbyName} for {maxPlayers} players");

            EnsureApprovalBoundBeforeStart();
            // start host
            if (!NetworkManager.Singleton.StartHost()) {
                throw new Exception("StartHost failed");
            }

            // heartbeat, poll coroutine
            heartbeatCoroutine = StartCoroutine(Heartbeat());
            pollCoroutine = StartCoroutine(PollLobby());

            Debug.Log($"Lobby '{joinedLobby.Name}' created. Code={lobbyCode}");
            return true;
        }
        catch (Exception e) {
            Debug.LogError($"HostFlow: {e}");
            return false;
        }
    }

    private async Task<bool> JoinByCodeFlow_Internal(string playerName, string code, Action<string> onError) {
        try {
            if (string.IsNullOrEmpty(code)) {
                Debug.LogWarning("Enter session code.");
                return false;
            }

            // set player data, join lobby options
            var playerData = new Dictionary<string, PlayerDataObject> {
                {"name",  new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, playerName)},
                {"ready", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, "false")}
            };

            var joinOptions = new JoinLobbyByCodeOptions {
                Player = new Player(null, null, playerData)
            };

            // join lobby
            Debug.Log($"Joining Lobby with {code}");
            Lobby lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(code, joinOptions);
            joinedLobby = lobby;

            // join relay
            relayCode = joinedLobby.Data["joinCode"].Value;

            Debug.Log($"Joining Relay with {relayCode}");
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(relayCode);

            // set relay to transport
            var utp = NetworkManager.Singleton.GetComponent<UnityTransport>();
            RelayServerData relayServerData = AllocationUtils.ToRelayServerData(joinAllocation, "dtls");
            utp.SetRelayServerData(relayServerData);

            // send my auth player id to host
            EnsureApprovalBoundBeforeStart();
            NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(MyId);

            if (!NetworkManager.Singleton.StartClient()) {
                throw new Exception("StartClient failed");
            }

            // poll coroutine
            pollCoroutine = StartCoroutine(PollLobby());

            Debug.Log($"Joined lobby '{lobby.Name}'. Code={code}");
            // Debug.Log($"Your Id = {}");

            return true;
        }
        catch (LobbyServiceException e) {
            Debug.LogError($"JoinFlow: {e.Reason}");
            onError?.Invoke(e.Reason.ToString());

            return false;
        }
        catch (Exception e) {
            Debug.LogError($"JoinFlow: {e}");

            return false;
        }
    }

    private async Task LeaveFlow() {
        StopLobbyCoroutines();

        try {
            if (joinedLobby != null) {
                // if you're host, delete it, or just leave
                if (IsLobbyHost) {
                    await DeleteLobby();

                }
                else {
                    await LeaveLobby();
                }
            }
        }
        catch (Exception e) {
            // ignore, just print log
            Debug.LogError($"LeaveFlow: {e}");
        }
        finally {
            HandleSessionClosed("You Left Session");
        }
    }

    private async Task DeleteLobby() {
        try {
            await LobbyService.Instance.DeleteLobbyAsync(joinedLobby.Id);
        }
        catch (LobbyServiceException e) {
            Debug.LogError($"DeleteLobby: {e.Reason}");
        }
    }

    private async Task LeaveLobby() {
        try {
            await LobbyService.Instance.RemovePlayerAsync(joinedLobby.Id, MyId);
        }
        catch (LobbyServiceException e) {
            switch (e.Reason) {
                // case LobbyExceptionReason.LobbyNotFound:
                // case LobbyExceptionReason.Forbidden:
                //     HandleSessionClosed("Host Closed Session");
                //     break;
                default:
                    Debug.LogError($"RemovePlayer: {e.Reason}");
                    break;
            }
        }
        catch (Exception e) {
            Debug.LogError($"RemovePlayer: {e}");
        }
    }

    private async void KickPlayer(string targetPlayerId) {
        if (!IsLobbyHost || targetPlayerId == MyId) {
            return;
        }

        try {
            // disconnect from relay server
            if (NetworkManager.Singleton && NetworkManager.Singleton.IsServer) {
                if (authToClient.TryGetValue(targetPlayerId, out var clientId)) {
                    if (clientId != NetworkManager.Singleton.LocalClientId) {
                        NetworkManager.Singleton.DisconnectClient(clientId);
                    }
                }
            }

            // disconnect from lobby
            await LobbyService.Instance.RemovePlayerAsync(joinedLobby.Id, targetPlayerId);
        }
        catch (LobbyServiceException e) {
            Debug.LogError($"Kick: {e.Reason}");
        }
        catch (Exception e) {
            Debug.LogError($"Kick: Unexpected Exception: {e}");
        }
        finally {
            await RefreshLobby();
        }
    }

    private void HandleSessionClosed(string reason) {
        if (isClosingConnection) {
            return;
        }

        isClosingConnection = true;

        Debug.Log($"Session Closed: {reason}");
        StopLobbyCoroutines();

        if (NetworkManager.Singleton && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsHost)) {
            NetworkManager.Singleton.Shutdown();
        }

        lobbyUi?.ShowFindSessionPanel(true);
        lobbyUi?.ShowLobbyPanel(false);
        lobbyUi?.ClearAllSlots();

        isClosingConnection = false;
    }

    // keep lobby alive
    private IEnumerator Heartbeat() {
        while (joinedLobby != null) {
            LobbyService.Instance.SendHeartbeatPingAsync(joinedLobby.Id);
            yield return new WaitForSecondsRealtime(heartbeatDelay);
        }
    }

    // automatically refresh lobby
    private IEnumerator PollLobby() {
        // distribute latelimit
        var jitter = UnityEngine.Random.Range(0f, 0.5f);

        while (joinedLobby != null && !isClosingConnection) {
            Task t = RefreshLobby();
            yield return new WaitUntil(() => t.IsCompleted);
            yield return new WaitForSecondsRealtime(pollDelay + jitter);
        }
    }

    private async Task RefreshLobby() {
        if (joinedLobby == null || isClosingConnection) {
            return;
        }

        try {
            Lobby refreshedLobby = await LobbyService.Instance.GetLobbyAsync(joinedLobby.Id);
            if (!refreshedLobby.Players.Exists(p => p.Id == MyId)) {
                HandleSessionClosed("You were kicked from the lobby.");
                return;
            }

            joinedLobby = refreshedLobby;
            UI_UpdateAll();

            pollDelay = 1.5f; // poll succeed: clear backoff
        }
        catch (LobbyServiceException e) {
            switch (e.Reason) {
                case LobbyExceptionReason.LobbyNotFound:
                case LobbyExceptionReason.Forbidden:
                    HandleSessionClosed("Host Closed Session");
                    break;
                case LobbyExceptionReason.RateLimited:
                    // backoff
                    pollDelay = Mathf.Clamp(pollDelay * 1.5f, 2f, 8f);
                    Debug.LogWarning($"RefreshLobby: poll rate-limited. Next delay = {pollDelay:0.0}s");
                    break;
                default:
                    pollDelay = Mathf.Min(pollDelay + 0.5f, 5f);
                    Debug.LogError($"RefreshLobby: {e.Reason}");
                    break;
            }
        }
        catch (Exception e) {
            pollDelay = Mathf.Min(pollDelay + 0.5f, 5f);
            Debug.LogError($"RefreshLobby: Unexpected Exception: {e}");
        }
    }

    private void StopLobbyCoroutines() {
        if (heartbeatCoroutine != null) {
            StopCoroutine(heartbeatCoroutine);
            heartbeatCoroutine = null;
        }
        if (pollCoroutine != null) {
            StopCoroutine(pollCoroutine);
            pollCoroutine = null;
        }
    }

    public static string GetPlayerString(Player player, string key, string fallback = "") {
        if (player.Data != null && player.Data.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v.Value)) {
            return v.Value;
        }
        return fallback;
    }

    public static bool GetPlayerBool(Player player, string key, bool fallback = false) {
        if (player.Data != null && player.Data.TryGetValue(key, out var v)) {
            return v.Value == "true";
        }
        return fallback;
    }
}