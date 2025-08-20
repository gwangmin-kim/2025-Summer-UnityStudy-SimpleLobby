using Unity.Services.Lobbies.Models;

public interface ILobbyUI {
    void ShowFindSessionPanel(bool on);
    void ShowLobbyPanel(bool on);

    void SetLobbyHeader(Lobby lobby);

    void ClearAllSlots();
    void RedrawPlayers(Lobby lobby, bool isHost);
    void SetButtons(bool isHost);


    void ShowCopiedText();
    void ShowError(string message);
}
