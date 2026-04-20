using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using TMPro;

public class ChatManager : NetworkBehaviour
{
    public static ChatManager Instance { get; private set; }

    [Header("UI References")]
    public TMP_InputField chatInputField;
    public TextMeshProUGUI chatDisplayArea;

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    private void Update()
    {
        if (chatInputField != null && chatInputField.isFocused && Input.GetKeyDown(KeyCode.Return))
        {
            if (!string.IsNullOrEmpty(chatInputField.text))
            {
                SendChatMessage(chatInputField.text);
                chatInputField.text = "";
            }
        }
    }

    public void SendChatMessage(string message)
    {
        ulong localId = NetworkManager.Singleton.LocalClientId;
        SendChatMessageServerRpc(message, localId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SendChatMessageServerRpc(string message, ulong senderId)
    {
        // Gönderen kişinin ölü olup olmadığını bul
        bool isSenderDead = false;
        FirstPersonController[] allPlayers = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
        foreach(var p in allPlayers)
        {
            if (p.OwnerClientId == senderId)
            {
                isSenderDead = p.isDead.Value;
                break;
            }
        }

        ReceiveChatMessageClientRpc(message, senderId, isSenderDead);
    }

    [ClientRpc]
    private void ReceiveChatMessageClientRpc(string message, ulong senderId, bool isSenderDead)
    {
        bool isLocalDead = false;
        FirstPersonController localPlayer = null;
        
        FirstPersonController[] allPlayers = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
        foreach(var p in allPlayers)
        {
            if (p.IsOwner)
            {
                localPlayer = p;
                isLocalDead = p.isDead.Value;
                break;
            }
        }

        // --- KRİTİK FİLTRELEME MANTIĞI ---
        // 1. Eğer gönderen ölüyse ve biz canlıysak: Mesajı görme.
        if (isSenderDead && !isLocalDead)
        {
            return; 
        }

        // Mesajın başına etiketi ekle (Hayalet mesajıysa belirtmek için)
        string tag = isSenderDead ? "<color=gray>[GHOST]</color> " : "";
        string finalMessage = tag + "Player " + senderId + ": " + message;

        // UI'da göster
        if (chatDisplayArea != null)
        {
            chatDisplayArea.text += "\n" + finalMessage;
        }
        
        Debug.Log("Chat Received: " + finalMessage);
    }
}
