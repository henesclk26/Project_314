using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Unity.Netcode;

public class MeetingUI : MonoBehaviour
{
    public static MeetingUI Instance { get; private set; }

    public GameObject panel; // Tüm toplantı arayüzü
    public TextMeshProUGUI timerText;
    public Transform cardsContainer; // Grid Layout Group içeren panel
    public GameObject playerCardPrefab;
    
    private List<PlayerVoteCard> activeCards = new List<PlayerVoteCard>();
    private ulong myVotedTarget = ulong.MaxValue;

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
        
        panel.SetActive(false);
    }

    private void Update()
    {
        if (MeetingManager.Instance != null && MeetingManager.Instance.IsMeetingActive)
        {
            float time = MeetingManager.Instance.MeetingTimer;
            if (timerText != null) timerText.text = "Kalan Süre: " + Mathf.CeilToInt(time).ToString();
        }
    }

    public void OpenPanel()
    {
        Debug.Log("[MeetingUI] OpenPanel çağrıldı!");
        panel.SetActive(true);
        myVotedTarget = ulong.MaxValue;
        GeneratePlayerCards();
    }

    public void ClosePanel()
    {
        panel.SetActive(false);
        foreach(var card in activeCards) Destroy(card.gameObject);
        activeCards.Clear();
    }

    private void GeneratePlayerCards()
    {
        foreach(var card in activeCards) Destroy(card.gameObject);
        activeCards.Clear();

        // Prefab veya container atanmamışsa kart oluşturmayı geç
        if (playerCardPrefab == null)
        {
            Debug.LogWarning("[MeetingUI] playerCardPrefab atanmamış! Kartlar oluşturulamadı.");
            return;
        }
        if (cardsContainer == null)
        {
            Debug.LogWarning("[MeetingUI] cardsContainer atanmamış! Kartlar oluşturulamadı.");
            return;
        }

        FirstPersonController[] players = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
        foreach (var p in players)
        {
            GameObject cardObj = Instantiate(playerCardPrefab, cardsContainer);
            PlayerVoteCard card = cardObj.GetComponent<PlayerVoteCard>();
            string pName = p.playerName.Value.ToString();
            if (string.IsNullOrEmpty(pName)) pName = "Oyuncu " + p.OwnerClientId;
            card.Setup(p.OwnerClientId, pName, p.isDead.Value);
            activeCards.Add(card);
        }
    }

    public void SelectVote(ulong targetId)
    {
        myVotedTarget = targetId;
        MeetingTrigger.Singleton?.SubmitVoteServerRpc(NetworkManager.Singleton.LocalClientId, targetId);
    }

    public void SkipVote() // Oyu Geç Butonu İçin
    {
        myVotedTarget = ulong.MaxValue;
        MeetingTrigger.Singleton?.SubmitVoteServerRpc(NetworkManager.Singleton.LocalClientId, ulong.MaxValue);
    }

    public void UpdatePlayerVoteStatus(ulong voterId)
    {
        foreach (var card in activeCards)
        {
            if (card.ownerId == voterId)
            {
                card.ShowVotedIndicator();
            }
        }
    }
}
