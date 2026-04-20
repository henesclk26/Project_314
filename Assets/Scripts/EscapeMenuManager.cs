using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using System.Collections;

public class EscapeMenuManager : MonoBehaviour
{
    [Header("ESC Menü Paneli")]
    public GameObject escMenuPanel; // Inspector'dan bağlanacak UI paneli

    private bool isMenuOpen = false;
    private bool isReturning = false; // Çift tıklama koruması

    void Start()
    {
        // Oyun başladığında menü kapalı olsun
        if (escMenuPanel != null)
            escMenuPanel.SetActive(false);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && !isReturning)
        {
            ToggleMenu();
        }
    }

    public void ToggleMenu()
    {
        isMenuOpen = !isMenuOpen;

        if (escMenuPanel != null)
            escMenuPanel.SetActive(isMenuOpen);

        if (isMenuOpen)
        {
            // Menü açılırken her zaman fareyi serbest bırak
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            // Menü kapanırken: ölü oyuncunun cursor'ını kilitleme
            if (!FirstPersonController.LocalPlayerIsDead)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            // Ölüysek: cursor serbest kalır (spectator için)
        }
    }

    // Bu metodu butonun OnClick() kısmına bağla (Inspector'da görünür)
    public void ReturnToMainMenu()
    {
        if (isReturning) return;
        isReturning = true;
        StartCoroutine(ReturnToMainMenuCoroutine());
    }

    private IEnumerator ReturnToMainMenuCoroutine()
    {
        Debug.Log("Ana menüye dönülüyor - Agresif Temizlik Başlatıldı...");

        // 1. UI ve Zaman Ayarları
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 2. Network Kapatma
        if (NetworkManager.Singleton != null)
        {
            GameObject networkManagerObject = NetworkManager.Singleton.gameObject;
            NetworkManager.Singleton.Shutdown();
            
            // Shutdown'un işlemesi için biraz bekle
            yield return new WaitForSecondsRealtime(0.1f);
            
            // Agresif temizlik: NetworkManager objesini sahneden tamamen sil
            // Bu, MainMenu'ye gidince çakışma yaşanmasını engeller
            Destroy(networkManagerObject);
        }

        // 3. Lobi Temizliği (MultiplayerManager DontDestroyOnLoad olduğu için silmiyoruz ama içini boşaltıyoruz)
        if (MultiplayerManager.Instance != null)
        {
            try
            {
                _ = MultiplayerManager.Instance.LeaveLobby();
            }
            catch { }
        }

        yield return new WaitForSecondsRealtime(0.1f);

        // 4. Sahne Yükle
        SceneManager.LoadScene("MainMenu");
    }
}
