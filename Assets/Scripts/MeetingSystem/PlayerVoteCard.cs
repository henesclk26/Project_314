using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerVoteCard : MonoBehaviour
{
    public ulong ownerId;
    public TextMeshProUGUI nameText;
    public Image background;
    public Button voteButton;
    public GameObject deadOverlay; // Üstü çizili veya gri panel
    public GameObject votedIndicator; // "Oy Kullandı" İkonu
    public GameObject speakingIcon; // Mikrofon Ses İkonu

    [Header("Renk Ayarları")]
    public Color normalColor = Color.white;
    public Color deadColor = new Color(0.3f, 0.3f, 0.3f, 1f);
    public Color nameTextColor = Color.black;
    public Color buttonTextColor = new Color(0.1f, 0.1f, 0.1f, 1f);

    private bool isDead;

    public void Setup(ulong id, string playerName, bool deadStatus)
    {
        ownerId = id;
        nameText.text = playerName;
        isDead = deadStatus;
        
        if(votedIndicator) votedIndicator.SetActive(false);
        if(speakingIcon) speakingIcon.SetActive(false);

        if (isDead)
        {
            if(deadOverlay) deadOverlay.SetActive(true);
            if(nameText) nameText.fontStyle = FontStyles.Strikethrough;
            if(voteButton) voteButton.interactable = false; // Ölü oyuncuya oy verilmez
            if(background) background.color = deadColor; 
            if(nameText) nameText.color = nameTextColor;
            if(voteButton) 
            {
                var txt = voteButton.GetComponentInChildren<TextMeshProUGUI>();
                if(txt) txt.color = buttonTextColor;
            }
        }
        else
        {
            if(deadOverlay) deadOverlay.SetActive(false);
            if(voteButton) voteButton.interactable = true;
            if(background) background.color = normalColor; 
            if(nameText) nameText.color = nameTextColor;
            if(voteButton) 
            {
                var txt = voteButton.GetComponentInChildren<TextMeshProUGUI>();
                if(txt) txt.color = buttonTextColor;
            }
        }

        if(voteButton) voteButton.onClick.AddListener(OnCardClicked);
    }

    /// <summary>
    /// Yerel oyuncu ölüyse tüm kartlarda oy düğmesini kapatır; canlıysa sadece ölü hedeflere tıklanamaz kalır.
    /// </summary>
    public void SetLocalVoterCanParticipate(bool localPlayerCanVote)
    {
        if (voteButton == null) return;
        voteButton.interactable = localPlayerCanVote && !isDead;
    }

    private void OnCardClicked()
    {
        if (isDead) return;

        FirstPersonController localPlayer = GetLocalPlayer();
        if (localPlayer != null && localPlayer.isDead.Value) return; // Eğer ben ölüysem oy veremem

        MeetingUI.Instance.SelectVote(ownerId);
    }

    public void ShowVotedIndicator()
    {
        if(votedIndicator) votedIndicator.SetActive(true);
    }

    private void Update()
    {
        // --- SESLİ SOHBET ENTEGRASYONU (VOICE CHAT HOOK) ---
        // Bu oyuncunun (ownerId) o an konuşup konuşmadığını Steam/Photon Voice vb. altyapıdan kontrol et.
        // Örnek (Steam P2P Voice veya harici eklenti):
        // bool isSpeaking = VoiceManager.Instance.IsPlayerTalking(ownerId);
        // speakingIcon.SetActive(isSpeaking);
    }

    private FirstPersonController GetLocalPlayer()
    {
        FirstPersonController[] players = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
        foreach (var p in players) { if (p.IsOwner) return p; }
        return null;
    }
}
