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
        // =========================================================
        // AÇIKLAMA: Input.GetKeyDown
        // ---------------------------------------------------------
        // GetKeyDown → tuşa BASILDIĞI AN (1 frame) true döner
        // GetKey     → tuş BASILI KALDIKÇA her frame true döner
        // GetKeyUp   → tuş BIRAKILDIĞI AN true döner
        //
        // ESC için GetKeyDown kullanıyoruz çünkü sadece basıldığında
        // bir kez toggle yapmak istiyoruz, sürekli değil.
        // =========================================================
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

        if (isMenuOpen)
        {
            // Menü açık → cursor serbest, karakter donuk
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;

            if (fpc != null)
            {
                fpc.playerCanMove = false;
                fpc.cameraCanMove = false;
            }
        }
        else
        {
            // Menü kapalı → cursor kilitli, karakter aktif
            UnityEngine.Cursor.lockState = CursorLockMode.Locked;
            UnityEngine.Cursor.visible = false;

            if (fpc != null)
            {
                fpc.playerCanMove = true;
                fpc.cameraCanMove = true;
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
}
