using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyPlayerSlot : MonoBehaviour {
    [Header("UI Components")]
    [SerializeField] private Image imageSlotPanel;
    [SerializeField] private TMP_Text textPlayerName;
    [SerializeField] private Button buttonKick;
    public string PlayerId { get; private set; }

    private static float panelAlpha = 0.4f;
    private static Color colorHost = Color.yellow;
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

    public void Bind(string playerId, string playerName, bool isHost, bool isPlayerReady, bool canKicked, Action<string> onKick) {
        PlayerId = playerId;
        textPlayerName.text = playerName;

        Color panelColor = isHost ? colorHost :
                           isPlayerReady ? colorReady :
                           colorNotReady;
        panelColor.a = panelAlpha;
        imageSlotPanel.color = panelColor;

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
