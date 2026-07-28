using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Oyun sırasında ESC tuşuyla açılıp kapanan pause menüsünü yönetir.
/// UI Toolkit (UIDocument) tabanlı çalışır.
/// </summary>
public class SciFiEscMenuController : MonoBehaviour
{
    [Header("Inspector'dan Ata")]
    [Tooltip("Sahnedeki FirstPersonController objesi")]
    public FirstPersonController fpc;

    // --- Dahili değişkenler ---
    private UIDocument uiDocument;
    private VisualElement escMenuRoot;
    private bool isMenuOpen = false;

    // =========================================================
    // AÇIKLAMA: Awake vs Start vs OnEnable
    // ---------------------------------------------------------
    // Awake()  → Obje yaratıldığında 1 kez çağrılır (diğer scriptlerden önce)
    // OnEnable() → Obje her aktif olduğunda çağrılır
    // Start() → İlk frame'den önce 1 kez çağrılır (Awake'den sonra)
    //
    // Biz Awake'te UIDocument referansını alıyoruz çünkü
    // Start'ta menüyü gizlememiz lazım ve sıralama önemli.
    // =========================================================

    private void Awake()
    {
        // UIDocument component'ini bu GameObject'ten al
        uiDocument = GetComponent<UIDocument>();
    }

    private void Start()
    {
        // =========================================================
        // AÇIKLAMA: rootVisualElement
        // ---------------------------------------------------------
        // UI Toolkit'te her UIDocument'ın bir "rootVisualElement"i var.
        // Bu, UXML dosyasındaki en üst elementtir.
        // Tüm butonlara, label'lara vs. buradan Q<T>() ile ulaşırız.
        // =========================================================
        if (uiDocument == null) return;

        var root = uiDocument.rootVisualElement;

        // =========================================================
        // AÇIKLAMA: Q<T>("name") — Query metodu
        // ---------------------------------------------------------
        // UI Toolkit'te element bulmak CSS selector gibi çalışır:
        //   root.Q<Button>("QuitGameButton")
        //     → rootVisualElement altında name="QuitGameButton" olan 
        //       ilk Button elementini bul
        //
        // Başka yollar:
        //   root.Q<Button>(className: "button")  → class ile bul
        //   root.Query<Button>().ToList()          → tüm butonları listele
        // =========================================================

        // UXML'deki kök VisualElement (name="EscMenu")
        escMenuRoot = root.Q<VisualElement>("EscMenu");

        // Butonları bul ve event'leri bağla
        var quitBtn = root.Q<Button>("QuitGameButton");
        var settingsBtn = root.Q<Button>("SettingsButton");

        // =========================================================
        // AÇIKLAMA: clicked event
        // ---------------------------------------------------------
        // UI Toolkit butonlarında onClick yerine "clicked" event'i var.
        // += ile dinleyici eklersin, -= ile çıkarırsın.
        //
        //   buton.clicked += MetodAdı;     ← parametre almaz
        //   buton.clicked += () => { };     ← lambda da olur
        // =========================================================
        if (quitBtn != null)
            quitBtn.clicked += OnQuitToMainMenu;

        // Settings şimdilik işlevsiz — ileride buraya bağlarsın
        // if (settingsBtn != null)
        //     settingsBtn.clicked += OnOpenSettings;

        // Oyun başlarken menüyü gizle
        SetMenuVisible(false);
    }

private void Update()
    {
        // A mission UI that owns ESC must consume it before the pause menu.
        if (ComputerUIManager.Instance != null &&
            (ComputerUIManager.Instance.IsComputerOpen || ComputerUIManager.Instance.WasClosedThisFrame))
            return;

        if (SecurityCameraUIManager.Instance != null &&
            (SecurityCameraUIManager.Instance.IsOpen || SecurityCameraUIManager.Instance.WasClosedThisFrame))
            return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            ToggleMenu();
        }
    }

    /// <summary>
    /// Menüyü aç/kapa toggle'ı
    /// </summary>
private void ToggleMenu()
    {
        isMenuOpen = !isMenuOpen;
        SetMenuVisible(isMenuOpen);

        // Runtime'da owner olan FPC'yi bul (inspector referansı prefab olabilir, spawn'dan sonra geçersiz)
        FirstPersonController activeFpc = GetOwnerFpc();

        if (isMenuOpen)
        {
            // Menü açık → cursor serbest, karakter donuk
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;

            if (activeFpc != null)
            {
                activeFpc.playerCanMove = false;
                activeFpc.cameraCanMove = false;
            }
        }
        else
        {
            // Menü kapalı → cursor kilitli, karakter aktif
            UnityEngine.Cursor.lockState = CursorLockMode.Locked;
            UnityEngine.Cursor.visible = false;

            if (activeFpc != null)
            {
                activeFpc.playerCanMove = true;
                activeFpc.cameraCanMove = true;
            }
        }
    }

    /// <summary>
    /// Menü panelini göster/gizle
    /// </summary>
    private void SetMenuVisible(bool visible)
    {
        // =========================================================
        // AÇIKLAMA: DisplayStyle
        // ---------------------------------------------------------
        // UI Toolkit'te objeleri gizlemek için 2 yol var:
        //
        // 1. DisplayStyle.None → CSS'teki display:none gibi
        //    Element tamamen kaldırılır, yer de kaplamaz.
        //
        // 2. Visibility.Hidden → CSS'teki visibility:hidden gibi
        //    Element görünmez ama yer kaplar.
        //
        // Biz DisplayStyle kullanıyoruz çünkü menü açık değilken
        // hiçbir şekilde ekranda olmamalı.
        // =========================================================
        if (escMenuRoot != null)
        {
            escMenuRoot.style.display = visible
                ? DisplayStyle.Flex    // Göster
                : DisplayStyle.None;   // Gizle
        }
    }

    /// <summary>
    /// Ana menüye dönüş — Main Menu UIDocument'ını açıp FPC'yi kapatır
    /// </summary>
    private void OnQuitToMainMenu()
    {
        Debug.Log("[EscMenu] Ana menüye dönülüyor...");

        // ESC menüsünü kapat
        isMenuOpen = false;
        SetMenuVisible(false);

        // SciFiMenuController üzerinden ana menüyü aç
        // (FPC'yi durdurur, cursor'u serbest bırakır, menüyü gösterir)
        var menuController = FindFirstObjectByType<SciFiMenuController>();
        if (menuController != null)
        {
            menuController.ShowMainMenu();
        }
    }


/// <summary>
    /// Sahnedeki spawn olmuş ve IsOwner olan FirstPersonController'ı döner.
    /// Inspector referansı prefab olabileceğinden runtime'da dinamik olarak aranır.
    /// </summary>
private FirstPersonController GetOwnerFpc()
    {
        // Sahnedeki tüm FPC'leri bul (prefablar dahil olmaz)
        var allFpcs = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);

        // Network aktifse Owner olanı bul
        if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening)
        {
            foreach (var f in allFpcs)
            {
                if (f.IsOwner) return f;
            }
        }
        
        // Offline/Editor testinde veya owner bulunamazsa sahnedeki ilk FPC'yi dön
        if (allFpcs.Length > 0) return allFpcs[0];

        // Fallback: inspector referansı sahnedeyse onu dön (prefab değilse)
        if (fpc != null && fpc.gameObject.scene.IsValid()) return fpc;

        return null;
    }
}
