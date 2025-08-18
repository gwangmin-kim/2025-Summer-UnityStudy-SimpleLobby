using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text;

using UnityEngine;
using UnityEngine.UI;
using TMPro;

using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;

using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

public class LobbyManager : MonoBehaviour {
    public LobbyManager Instance { get; private set; }

    [Header("Left Panel (Find Session)")]
    [SerializeField] private GameObject leftPanel;
    // for all players
    [SerializeField] private TMP_InputField inputPlayerName;
    // for host
    [SerializeField] private TMP_InputField inputSessionName;
    [SerializeField] private TMP_Dropdown dropdownMaxPlayers;
    [SerializeField] private Button buttonCreateHost;
    // for clients
    [SerializeField] private TMP_InputField inputJoinCode;
    [SerializeField] private Button buttonJoinByCode;
    [SerializeField] private TMP_Text textExceptionInformation;

    [Header("Right Panel (Your Session)")]
    [SerializeField] private GameObject rightPanel;
    [SerializeField] private TMP_Text textSessionName;
    [SerializeField] private TMP_Text textSessionCode;
    [SerializeField] private Button buttonCopyCode;
    [SerializeField] private TMP_Text textCopied;
    [SerializeField] private TMP_Text textPlayerCount;
    [SerializeField] private Button buttonLeave;
    [SerializeField] private Button buttonReady;
    [SerializeField] private Button buttonStartGame;
    [SerializeField] private List<LobbyPlayerSlot> lobbyPlayerSlots;

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

    private Coroutine copiedTextCoroutine;
    private float copiedTextDuratioon = 1.5f;

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
        // set panel active
        leftPanel.SetActive(true);
        textExceptionInformation.text = "";
        QuitLobbyUI();

        // set button listener
        buttonCreateHost.onClick.AddListener(async () => await HostFlow());
        buttonJoinByCode.onClick.AddListener(async () => await JoinByCodeFlow());
        buttonLeave.onClick.AddListener(async () => await LeaveFlow());
        buttonCopyCode.onClick.AddListener(CopyJoinCode);

        await EnsureServices();
    }

    private void EnterLobbyUIForHost() {
        if (joinedLobby == null) {
            return;
        }

        leftPanel.SetActive(false);
        rightPanel.SetActive(true);
        buttonStartGame.gameObject.SetActive(true);
        buttonReady.gameObject.SetActive(false);
        textCopied.gameObject.SetActive(false);

        textSessionName.text = joinedLobby.Name;
        textSessionCode.text = joinedLobby.LobbyCode;

        UpdatePlayerSlotUI();
    }

    private void EnterLobbyUIForClient() {
        if (joinedLobby == null) {
            return;
        }

        leftPanel.SetActive(false);
        rightPanel.SetActive(true);
        buttonReady.gameObject.SetActive(true);
        buttonStartGame.gameObject.SetActive(false);
        textCopied.gameObject.SetActive(false);

        textSessionName.text = joinedLobby.Name;
        textSessionCode.text = joinedLobby.LobbyCode;

        UpdatePlayerSlotUI();
    }

    private void QuitLobbyUI() {
        leftPanel.SetActive(true);
        rightPanel.SetActive(false);
        buttonReady.gameObject.SetActive(false);
        buttonStartGame.gameObject.SetActive(false);
        textCopied.gameObject.SetActive(false);
        foreach (LobbyPlayerSlot slot in lobbyPlayerSlots) {
            slot.gameObject.SetActive(false);
        }
    }

    private void UpdatePlayerSlotUI() {
        if (joinedLobby == null) {
            return;
        }

        foreach (LobbyPlayerSlot slot in lobbyPlayerSlots) {
            slot.gameObject.SetActive(false);
        }

        string hostId = joinedLobby.HostId;

        int slotIndex = 1; // for clients who isn't host (use from second slot)

        foreach (Player player in joinedLobby.Players) {
            // get player info
            string playerId = player.Id;
            string playerName = GetPlayerString(player, "name");
            bool isPlayerReady = GetPlayerBool(player, "ready");
            bool canKickedByMe = IsLobbyHost && (playerId != hostId);

            LobbyPlayerSlot slot = null;
            // if host, use first slot
            if (playerId == hostId) {
                slot = lobbyPlayerSlots[0];
            }
            // else, use from second slot
            else {
                if (slotIndex >= joinedLobby.MaxPlayers) {
                    Debug.LogWarning($"UpdatePlayerSlotUI: slotIndex is invalid. slotIndex={slotIndex}, maxPlayers={joinedLobby.MaxPlayers}");
                }
                else {
                    slot = lobbyPlayerSlots[slotIndex++];
                }
            }

            if (slot != null) {
                slot.gameObject.SetActive(true);

                slot.SetPlayerId(playerId);
                slot.SetPlayerName(playerName);

                if (IsLobbyHost && (playerId != hostId)) {
                    slot.EnableKickButton();
                }

                slot.Bind(playerId, playerName, isPlayerReady, canKickedByMe, KickPlayer);
            }
        }

        textPlayerCount.text = $"({joinedLobby.Players.Count}/{joinedLobby.MaxPlayers})";
    }

    private void CopyJoinCode() {
        string code = textSessionCode.text;
        if (string.IsNullOrEmpty(code)) {
            return;
        }

        GUIUtility.systemCopyBuffer = code;

        copiedTextCoroutine = StartCoroutine(PopCopiedText());
    }

    private async Task EnsureServices() {
        await UnityServices.InitializeAsync();

        AuthenticationService.Instance.SignedIn += () => {
            Debug.Log($"Signed in: {AuthenticationService.Instance.PlayerId}");
        };

        if (!AuthenticationService.Instance.IsSignedIn) {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }

    private async Task HostFlow() {
        try {
            // get input values
            string playerName = GetSafeName(inputPlayerName.text, "Player");
            string sessionName = GetSafeName(inputSessionName.text, "Game");
            int maxPlayers = int.Parse(dropdownMaxPlayers.options[dropdownMaxPlayers.value].text);

            // create relay allocation
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
            relayCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            Debug.Log($"Relay created. Code={relayCode}");

            // set relay to transport
            UnityTransport utp = NetworkManager.Singleton.GetComponent<UnityTransport>();
            RelayServerData relayServerData = AllocationUtils.ToRelayServerData(allocation, "dtls");
            utp.SetRelayServerData(relayServerData);

            // set player, lobby data and lobby option
            var playerData = new Dictionary<string, PlayerDataObject> {
                {"name",  new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, playerName)},
                {"ready", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, "true")} // host는 언제나 ready 상태
            };

            // string lobbyName = $"{sessionName}-{UnityEngine.Random.Range(1000, 9999)}";
            string lobbyName = sessionName;
            var lobbyData = new Dictionary<string, DataObject> {
                {"joinCode", new DataObject(DataObject.VisibilityOptions.Member, relayCode, DataObject.IndexOptions.S1)},
                {"state", new DataObject(DataObject.VisibilityOptions.Public, "waiting", DataObject.IndexOptions.S2)}
            };

            CreateLobbyOptions createLobbyOptions = new CreateLobbyOptions {
                IsPrivate = false,
                Data = lobbyData,
                Player = new Player(null, null, playerData)
            };

            // create lobby
            joinedLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, createLobbyOptions);
            lobbyCode = joinedLobby.LobbyCode;
            Debug.Log($"Created {lobbyName} for {maxPlayers} players");

            // start host
            if (!NetworkManager.Singleton.StartHost()) {
                throw new Exception("StartHost failed");
            }

            // heartbeat, poll coroutine
            heartbeatCoroutine = StartCoroutine(Heartbeat());
            pollCoroutine = StartCoroutine(PollLobby());

            // set ui for host
            EnterLobbyUIForHost();

            Debug.Log($"Lobby '{joinedLobby.Name}' created. Code={lobbyCode}");
        }
        catch (Exception e) {
            Debug.LogError($"HostFlow: {e}");
        }
    }

    private async Task JoinByCodeFlow() {
        try {
            // get input values
            string playerName = GetSafeName(inputPlayerName.text, "Player");
            var code = inputJoinCode.text?.Trim();
            if (string.IsNullOrEmpty(code)) {
                Debug.LogWarning("Enter session code.");
                return;
            }

            // set player data, join lobby options
            var playerData = new Dictionary<string, PlayerDataObject> {
                {"name",  new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, playerName)},
                {"ready", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, "false")}
            };

            JoinLobbyByCodeOptions joinOptions = new JoinLobbyByCodeOptions {
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
            UnityTransport utp = NetworkManager.Singleton.GetComponent<UnityTransport>();
            RelayServerData relayServerData = AllocationUtils.ToRelayServerData(joinAllocation, "dtls");
            utp.SetRelayServerData(relayServerData);

            if (!NetworkManager.Singleton.StartClient()) {
                throw new Exception("StartClient failed");
            }

            // poll coroutine
            pollCoroutine = StartCoroutine(PollLobby());

            // set ui for client
            EnterLobbyUIForClient();

            Debug.Log($"Joined lobby '{lobby.Name}'. Code={code}");
            // Debug.Log($"Your Id = {}");
        }
        catch (LobbyServiceException e) {
            Debug.LogError($"JoinFlow: {e.Reason}");
            switch (e.Reason) {
                case LobbyExceptionReason.InvalidJoinCode:
                    textExceptionInformation.text = "Invalid Format.";
                    break;
                case LobbyExceptionReason.LobbyNotFound:
                    textExceptionInformation.text = "Lobby not Found. Try another code.";
                    break;
                case LobbyExceptionReason.LobbyFull:
                    textExceptionInformation.text = "Lobby is Full.";
                    break;
                default:
                    textExceptionInformation.text = e.Reason.ToString();
                    break;
            }
        }
        catch (Exception e) {
            Debug.LogError($"JoinFlow: {e}");
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

    public async void KickPlayer(string targetPlayerId) {
        if (!IsLobbyHost || targetPlayerId == MyId) {
            return;
        }

        try {
            // disconnect from relay server
            if (NetworkManager.Singleton.IsServer) {
                if (authToClient.TryGetValue(targetPlayerId, out var clientId)) {
                    if (clientId != NetworkManager.Singleton.LocalClientId)
                        NetworkManager.Singleton.DisconnectClient(clientId);
                }
            }

            // disconnect from lobby
            await LobbyService.Instance.RemovePlayerAsync(joinedLobby.Id, targetPlayerId);
            await RefreshLobby();
        }
        catch (LobbyServiceException e) {
            Debug.LogError($"Kick: {e.Reason}");
        }
        catch (Exception e) {
            Debug.LogError($"Kick: Unexpected Exception: {e}");
        }
    }

    private void HandleSessionClosed(string reason) {
        if (isClosingConnection) {
            return;
        }

        isClosingConnection = true;

        Debug.Log($"Session Closed: {reason}");
        StopLobbyCoroutines();

        if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsHost) {
            NetworkManager.Singleton.Shutdown();
        }

        QuitLobbyUI();
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

    private IEnumerator PopCopiedText() {
        textCopied.gameObject.SetActive(true);
        yield return new WaitForSecondsRealtime(copiedTextDuratioon);

        textCopied.gameObject.SetActive(false);

        StopCoroutine(copiedTextCoroutine);
        copiedTextCoroutine = null;
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
            UpdatePlayerSlotUI();

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

    private static string GetSafeName(string name, string fallback) {
        name = (name ?? "").Trim();
        return string.IsNullOrEmpty(name) ? $"{fallback}{UnityEngine.Random.Range(1000, 9999)}" : name;
    }

    static string GetPlayerString(Player player, string key, string fallback = "") {
        if (player.Data != null && player.Data.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v.Value)) {
            return v.Value;
        }
        return fallback;
    }

    static bool GetPlayerBool(Player player, string key, bool fallback = false) {
        if (player.Data != null && player.Data.TryGetValue(key, out var v)) {
            return v.Value == "true";
        }
        return fallback;
    }
}