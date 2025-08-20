using UnityEngine;
using UnityEngine.UI;
using TMPro;

using System.Collections.Generic;
using System.Collections;

using Unity.Services.Lobbies.Models;

public class LobbyUI : MonoBehaviour, ILobbyUI {
    // UI Elements
    [Header("Find Session Panel")]
    [SerializeField] private GameObject findSessionPanel;
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

    [Header("Lobby Panel")]
    [SerializeField] private GameObject lobbyPanel;
    [SerializeField] private TMP_Text textSessionName;
    [SerializeField] private TMP_Text textSessionCode;
    [SerializeField] private Button buttonCopyCode;
    [SerializeField] private TMP_Text textCopied;
    [SerializeField] private TMP_Text textPlayerCount;
    [SerializeField] private Button buttonLeave;
    [SerializeField] private Button buttonReady;
    [SerializeField] private TMP_Text textButtonReady;
    [SerializeField] private Button buttonStartGame;
    [SerializeField] private List<LobbyPlayerSlot> lobbyPlayerSlots;

    // copied text coroutine
    private Coroutine copiedTextCoroutine;
    private float copiedTextDuratioon = 1.5f;

    // lobby manager
    LobbyManager lobbyManager;

    private void OnEnable() {
        lobbyManager = LobbyManager.Instance ? LobbyManager.Instance : FindAnyObjectByType<LobbyManager>(FindObjectsInactive.Include);
        if (lobbyManager == null) {
            Debug.LogWarning("No LobbyManager Found");
            return;
        }

        // set button listener
        buttonCreateHost.onClick.AddListener(async () => {
            string playerName = GetSafeName(inputPlayerName.text, "Player");
            string sessionName = GetSafeName(inputSessionName.text, "Game");
            int maxPlayers = int.Parse(dropdownMaxPlayers.options[dropdownMaxPlayers.value].text);
            await lobbyManager.HostFlow_FromUI(playerName, sessionName, maxPlayers);
        });
        buttonJoinByCode.onClick.AddListener(async () => {
            string playerName = GetSafeName(inputPlayerName.text, "Player");
            string code = inputJoinCode.text?.Trim();
            await lobbyManager.JoinByCodeFlow_FromUI(playerName, code, ShowError);
        });

        buttonCopyCode.onClick.AddListener(lobbyManager.CopyJoinCode_FromUI);
        buttonLeave.onClick.AddListener(async () => await lobbyManager.LeaveFlow_FromUI());
        buttonReady.onClick.AddListener(async () => await lobbyManager.ToggleReady_FromUI());
        buttonStartGame.onClick.AddListener(async () => await lobbyManager.StartGame_FromUI());

        lobbyManager.UI_Attach(this, this);
        ResetVisuals();
    }

    private void OnDisable() {
        if (lobbyManager != null) {
            lobbyManager.UI_Detach(this);
            lobbyManager = null;
        }

        // prevent redundancy
        buttonCreateHost.onClick.RemoveAllListeners();
        buttonJoinByCode.onClick.RemoveAllListeners();
        buttonCopyCode.onClick.RemoveAllListeners();
        buttonLeave.onClick.RemoveAllListeners();
        buttonReady.onClick.RemoveAllListeners();
        buttonStartGame.onClick.RemoveAllListeners();
    }

    private void AttachToManager() {

    }

    public void ShowFindSessionPanel(bool on) {
        findSessionPanel.SetActive(on);
        textExceptionInformation.text = "";
    }
    public void ShowLobbyPanel(bool on) {
        lobbyPanel.SetActive(on);
        textCopied.gameObject.SetActive(false);
    }

    public void SetLobbyHeader(Lobby lobby) {
        textSessionName.text = lobby?.Name ?? "";
        textSessionCode.text = lobby?.LobbyCode ?? "";
        textPlayerCount.text = lobby == null ? "" : $"({lobby.Players.Count}/{lobby.MaxPlayers})";
    }

    public void ClearAllSlots() {
        foreach (LobbyPlayerSlot slot in lobbyPlayerSlots) {
            slot.Clear();
        }
    }

    public void RedrawPlayers(Lobby lobby, bool isCalledByHost) {
        foreach (LobbyPlayerSlot slot in lobbyPlayerSlots) {
            slot.gameObject.SetActive(false);
        }
        if (lobby == null) {
            return;
        }

        string hostId = lobby.HostId;
        int slotIndex = 1; // for clients who isn't host (use from second slot)

        foreach (Player player in lobby.Players) {
            // get player info
            string id = player.Id;
            bool isHost = id == hostId;
            string name = LobbyManager.GetPlayerString(player, "name");
            bool isReady = LobbyManager.GetPlayerBool(player, "ready");
            bool canKicked = isCalledByHost && !isHost;
            LobbyPlayerSlot slot = isHost ? lobbyPlayerSlots[0]
                                   : (slotIndex < lobby.MaxPlayers ? lobbyPlayerSlots[slotIndex++] : null);

            if (slot == null) {
                Debug.LogWarning($"UpdatePlayerSlotUI: slotIndex is invalid. slotIndex={slotIndex}, maxPlayers={lobby.MaxPlayers}");
                continue;
            }
            slot.gameObject.SetActive(true);
            slot.Bind(id, name, isHost, isReady, canKicked, lobbyManager.KickPlayer_FromUI);
        }
    }

    public void SetButtons(bool isHost) {
        buttonReady.gameObject.SetActive(!isHost);
        buttonStartGame.gameObject.SetActive(isHost);
    }

    public void ShowCopiedText() {
        copiedTextCoroutine = StartCoroutine(PopCopiedText());
    }

    private IEnumerator PopCopiedText() {
        textCopied.gameObject.SetActive(true);
        yield return new WaitForSecondsRealtime(copiedTextDuratioon);

        textCopied.gameObject.SetActive(false);

        StopCoroutine(copiedTextCoroutine);
        copiedTextCoroutine = null;
    }

    public void ShowError(string message) {
        textExceptionInformation.text = message ?? "";
    }

    private void ResetVisuals() {
        ShowFindSessionPanel(true);
        ShowLobbyPanel(false);
        ClearAllSlots();
    }

    private static string GetSafeName(string name, string fallback) {
        name = (name ?? "").Trim();
        return string.IsNullOrEmpty(name) ? $"{fallback}{UnityEngine.Random.Range(1000, 9999)}" : name;
    }
}