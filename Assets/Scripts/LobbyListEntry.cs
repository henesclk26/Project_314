using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Unity.Services.Lobbies.Models;

public class LobbyListEntry : MonoBehaviour
{
    public TMP_Text lobbyNameText;
    public TMP_Text playerCountText;
    public Button joinBtn;

    private string _lobbyId;
    private LobbyUIManager _uiManager;

    public void Setup(Lobby lobby, LobbyUIManager uiManager)
    {
        _lobbyId = lobby.Id;
        _uiManager = uiManager;

        if (lobbyNameText == null || playerCountText == null || joinBtn == null)
        {
            TMP_Text[] allTexts = GetComponentsInChildren<TMP_Text>(true);
            if (lobbyNameText == null && allTexts.Length > 0) lobbyNameText = allTexts[0];
            if (playerCountText == null && allTexts.Length > 1) playerCountText = allTexts[1];
            if (joinBtn == null) joinBtn = GetComponentInChildren<Button>(true);
        }

        if (lobbyNameText != null)
            lobbyNameText.text = lobby.Name;

        if (playerCountText != null)
            playerCountText.text = $"{lobby.Players.Count}/{lobby.MaxPlayers}";

        if (joinBtn != null)
        {
            joinBtn.onClick.RemoveAllListeners();
            joinBtn.onClick.AddListener(OnJoinClicked);
        }
    }

    private void OnJoinClicked()
    {
        _uiManager.JoinLobbyById(_lobbyId);
    }
}
