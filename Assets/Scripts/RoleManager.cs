using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using TMPro; // TextMeshPro için gerekli
using System.Linq;

public enum PlayerRole
{
    None,
    Crewmate,
    Impostor
}

public class RoleManager : NetworkBehaviour
{
    public static RoleManager Instance { get; private set; }

    [Header("Arayüz (UI) Ayarları")]
    [Tooltip("Arayüzde sağ üstte rolü gösterecek TextMeshPro componenti.")]
    public TextMeshProUGUI roleText;

    [Tooltip("Sağ altta belirecek 'E - Öldür' yazısı (TMP)")]
    public TextMeshProUGUI killText;
    
    [Tooltip("Bekleme süresi, sayaç yazısı (TMP)")]
    public TextMeshProUGUI cooldownText;

    [Tooltip("Öldükten sonra çıkacak İzleyici Modu bilgi yazısı (TMP)")]
    public TextMeshProUGUI spectatorHintText;

    private PlayerRole localPlayerRole = PlayerRole.None;

    private void Awake()
    {
        // Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
    }

    private void Start()
    {
        // Başlangıçta izleyici ipucu yazısını gizle (sadece öldüğünde açılsın)
        if (spectatorHintText != null)
        {
            spectatorHintText.gameObject.SetActive(false);
        }
    }

    public PlayerRole GetLocalPlayerRole()
    {
        return localPlayerRole;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Senkronizasyon için sahneler yüklendikten 2 saniye sonra görevleri dağıt (herkes girdiğinde garanti olsun)
            Invoke(nameof(AssignRoles), 2f);
        }
    }

    /// <summary>
    /// Bu fonksiyon SADECE oyunu kuran kişi (Host/Server) tarafından oyun başladığında çağrılır.
    /// </summary>
    public void AssignRoles()
    {
        if (!IsServer) 
        {
            Debug.LogWarning("Roller sadece Sunucu (Server/Host) tarafından dağıtılmalıdır.");
            return;
        }

        // O an sunucuya bağlı tüm oyuncuların Network ID'lerini bir Listeye alalım
        List<ulong> clientIds = NetworkManager.Singleton.ConnectedClientsIds.ToList();
        
        // Listeyi karıştır (Shuffle Algoritması)
        ShuffleList(clientIds);

        int playerCount = clientIds.Count;
        int impostorCount = 1;

        // 5 kişiden itibaren 2 katil olsun kuralı
        if (playerCount >= 5)
        {
            impostorCount = 2; 
        }

        // Oyunda atanması gereken katil sayısından daha az oyuncu varsa (test amaçlı 1 kişi girerseniz diye)
        if (impostorCount > playerCount) { impostorCount = playerCount; }

        for (int i = 0; i < playerCount; i++)
        {
            ulong targetId = clientIds[i];
            
            // Eğer karıştırılmış listemizde ilk 'impostorCount' sırasındaysan katil, yoksan köylüsün.
            PlayerRole assignedRole = (i < impostorCount) ? PlayerRole.Impostor : PlayerRole.Crewmate;

            // SADECE hedef client'ın alabileceği özel bir mesaj paketi hazırlıyoruz (TargetClientIds)
            ClientRpcParams clientRpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { targetId }
                }
            };

            // Hedef client a RPC'yi (Network fonksiyonunu) gönder (Sadece kendisine gidecek)
            ReceiveRoleClientRpc(assignedRole, clientRpcParams);
        }
        
        Debug.Log($"Rol ataması tamamlandı: {playerCount} oyuncunun {impostorCount} tanesi katil yapıldı.");
    }

    /// <summary>
    /// Sunucunun sadece hedeflenen istemciye (Client) rolünü ilettiği metot
    /// </summary>
    [ClientRpc]
    private void ReceiveRoleClientRpc(PlayerRole role, ClientRpcParams clientRpcParams = default)
    {
        localPlayerRole = role;
        UpdateRoleUI(role);
        Debug.Log("Rol atamam ulaştı: " + role.ToString());
    }

    /// <summary>
    /// Ekrandaki yazıyı oyuncunun gizli rolüne göre günceller
    /// </summary>
    private void UpdateRoleUI(PlayerRole role)
    {
        if (roleText == null)
        {
            Debug.LogWarning("RoleManager'ın içindeki RoleText (TMP) boş! Hierarchy'den atama yapmayı unutmayın.");
            return;
        }

        if (role == PlayerRole.Impostor)
        {
            roleText.text = "Rolün: Katil";
            roleText.color = Color.red;
        }
        else if (role == PlayerRole.Crewmate)
        {
            roleText.text = "Rolün: Köylü";
            roleText.color = Color.green;
        }
    }

    /// <summary>
    /// Fisher-Yates Shuffle algoritması: Listeyi rastgele karıştırır.
    /// </summary>
    private void ShuffleList<T>(List<T> list)
    {
        System.Random rng = new System.Random();
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = rng.Next(n + 1);
            T value = list[k];
            list[k] = list[n];
            list[n] = value;
        }
    }
}
