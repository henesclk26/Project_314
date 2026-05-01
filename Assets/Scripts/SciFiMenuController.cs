using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Sci-fi-map sahnesinde menü → oyun geçişini yönetir.
/// Ayrı bir GameObject'e eklenir (Main Menu'ye DEĞİL).
/// Inspector'dan menuObject ve fpc sürükle-bırak ile atanır.
/// </summary>
public class SciFiMenuController : MonoBehaviour
{
    [Header("Inspector'dan Ata")]
    [Tooltip("Sahnedeki Main Menu GameObject'i (UIDocument olan)")]
    public GameObject menuObject;

    [Tooltip("Sahnedeki FirstPersonController GameObject'i")]
    public FirstPersonController fpc;

    private void Start()
    {
        // ── Menü açıkken FPC'yi tamamen kapat ──
        if (fpc != null)
        {
            fpc.enabled = false;  // Update/FixedUpdate çalışmasın

            // Kamerayı AÇIK bırak ki sahne renderlansin (No cameras rendering hatası olmasın)
            // FPC.enabled=false olduğu için input almayacak zaten
            if (fpc.playerCamera != null)
            {
                fpc.playerCamera.gameObject.SetActive(true);
                var listener = fpc.playerCamera.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = true;
            }
        }

        // Cursor serbest (menüde tıklayabilmek için)
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;

        // Menüyü göster
        if (menuObject != null)
            menuObject.SetActive(true);

        // UI butonlarını bağla
        SetupButtons();
    }

    private void SetupButtons()
    {
        var uiDoc = menuObject != null ? menuObject.GetComponent<UIDocument>() : null;
        if (uiDoc == null)
        {
            Debug.LogError("[SciFiMenuController] menuObject'te UIDocument bulunamadı!");
            return;
        }

        var root = uiDoc.rootVisualElement;

        var btnPrivate = root.Q<Button>("btn-private-game");
        var btnPublic  = root.Q<Button>("btn-public-game");
        var btnQuit    = root.Q<Button>("btn-quit-game");

        if (btnPrivate != null) btnPrivate.clicked += OnStartGame;
        if (btnPublic  != null) btnPublic.clicked  += OnStartGame;
        if (btnQuit    != null) btnQuit.clicked    += OnQuitGame;

        Debug.Log($"[SciFiMenuController] Butonlar bağlandı. Private={btnPrivate != null}, Public={btnPublic != null}, Quit={btnQuit != null}");
    }

    private void OnStartGame()
    {
        Debug.Log("[SciFiMenuController] Oyun başlatılıyor...");

        // ── FPC'yi aç ──
        if (fpc != null)
        {
            // Kamerayı aktif et
            if (fpc.playerCamera != null)
            {
                fpc.playerCamera.gameObject.SetActive(true);
                var listener = fpc.playerCamera.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = true;
            }

            // Hareket ve fare kontrolünü aç
            fpc.playerCanMove = true;
            fpc.cameraCanMove = true;

            // FPC component'ini aktif et (Update çalışsın)
            fpc.enabled = true;
        }

        // Cursor'u kilitle
        UnityEngine.Cursor.lockState = CursorLockMode.Locked;
        UnityEngine.Cursor.visible = false;

        // Menüyü kapat
        if (menuObject != null)
            menuObject.SetActive(false);
    }

    private void OnQuitGame()
    {
        Debug.Log("[SciFiMenuController] Oyundan çıkılıyor...");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>
    /// ESC menüsünden ana menüye dönüşte çağrılır.
    /// Start ile aynı mantığı yeniden çalıştırır.
    /// </summary>
    public void ShowMainMenu()
    {
        // FPC'yi durdur
        if (fpc != null)
        {
            fpc.enabled = false;
            fpc.playerCanMove = false;
            fpc.cameraCanMove = false;
        }

        // Cursor serbest
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;

        // Menüyü göster
        if (menuObject != null)
            menuObject.SetActive(true);

        // Butonları TEKRAR bağla (UIDocument yeniden aktif olunca
        // visual tree sıfırdan oluşur, eski handler'lar ölür)
        SetupButtons();
    }
}
