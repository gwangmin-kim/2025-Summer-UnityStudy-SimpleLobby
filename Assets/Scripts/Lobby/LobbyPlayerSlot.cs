using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyPlayerSlot : MonoBehaviour {
    [Header("UI Components")]
    [SerializeField] private TMP_Text textPlayerName;
    [SerializeField] private Button buttonKick;
    public string PlayerId { get; private set; }

    private static Color colorReady = Color.yellowGreen;
    private static Color colorNotReady = Color.gray;

    private Action<string> onKickAction;

    public void SetPlayerId(string id) {
        PlayerId = id;
    }

    public void SetPlayerName(string playerName) {
        textPlayerName.text = playerName;
    }

    public void EnableKickButton() {
        buttonKick.gameObject.SetActive(true);
    }

    public void Bind(string playerId, string playerName, bool isPlayerReady, bool canKicked, Action<string> onKick) {
        PlayerId = playerId;
        textPlayerName.text = playerName;
        textPlayerName.color = isPlayerReady ? colorReady : colorNotReady;

        onKickAction = onKick;
        buttonKick.onClick.RemoveAllListeners();
        buttonKick.onClick.AddListener(() => onKickAction?.Invoke(PlayerId));
        buttonKick.gameObject.SetActive(canKicked);
    }

    public void Clear() {
        PlayerId = null;
        textPlayerName.text = "";
        buttonKick.onClick.RemoveAllListeners();
        buttonKick.gameObject.SetActive(false);
    }
}
