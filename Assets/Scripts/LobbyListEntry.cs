#define USE_STEAM // Steam'i iptal edip UGS'ye donmek icin bu satiri silin veya basina // koyun

using UnityEngine;
using TMPro;
using UnityEngine.UI;

#if USE_STEAM
using Steamworks;
using Steamworks.Data;
#else
using Unity.Services.Lobbies.Models;
#endif

public class LobbyListEntry : MonoBehaviour
{
    public TMP_Text lobbyNameText;
    public TMP_Text playerCountText;
    public Button joinBtn;

    private string _lobbyId;
    private LobbyUIManager _uiManager;

#if USE_STEAM
    public void Setup(Lobby lobby, LobbyUIManager uiManager)
    {
        _lobbyId = lobby.Id.ToString();
        _uiManager = uiManager;

        // EĞER PREFAB ÜZERİNDE REFERANSLAR KOPMUŞSA (Örn: Boş gri kutu sorunu), ÇOCUKLARDAN ZORLA BUL!
        if (lobbyNameText == null || playerCountText == null || joinBtn == null)
        {
            Debug.LogWarning("[LobbyListEntry] Inspector referansları kopmuş! Çocuklardan otomatik bulunuyor...");
            TMP_Text[] allTexts = GetComponentsInChildren<TMP_Text>(true);
            if (lobbyNameText == null && allTexts.Length > 0) lobbyNameText = allTexts[0];
            if (playerCountText == null && allTexts.Length > 1) playerCountText = allTexts[1];
            
            if (joinBtn == null) joinBtn = GetComponentInChildren<Button>(true);
        }
        
        if (lobbyNameText != null) 
        {
            string lName = lobby.GetData("Name");
            lobbyNameText.text = string.IsNullOrEmpty(lName) ? "Steam Lobby" : lName;
        }
        
        if (playerCountText != null) 
        {
            playerCountText.text = $"{lobby.MemberCount}/{lobby.MaxMembers}";
        }
#else
    public void Setup(Lobby lobby, LobbyUIManager uiManager)
    {
        _lobbyId = lobby.Id;
        _uiManager = uiManager;

        // EĞER PREFAB ÜZERİNDE REFERANSLAR KOPMUŞSA (Örn: Boş gri kutu sorunu), ÇOCUKLARDAN ZORLA BUL!
        if (lobbyNameText == null || playerCountText == null || joinBtn == null)
        {
            Debug.LogWarning("[LobbyListEntry] Inspector referansları kopmuş! Çocuklardan otomatik bulunuyor...");
            TMP_Text[] allTexts = GetComponentsInChildren<TMP_Text>(true);
            if (lobbyNameText == null && allTexts.Length > 0) lobbyNameText = allTexts[0];
            if (playerCountText == null && allTexts.Length > 1) playerCountText = allTexts[1];
            
            if (joinBtn == null) joinBtn = GetComponentInChildren<Button>(true);
        }
        if (lobbyNameText != null) 
        {
            lobbyNameText.text = lobby.Name;
        }
        
        if (playerCountText != null) 
        {
            playerCountText.text = $"{lobby.Players.Count}/{lobby.MaxPlayers}";
        }
#endif

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
