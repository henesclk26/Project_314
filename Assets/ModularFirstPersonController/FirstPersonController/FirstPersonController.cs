#define USE_STEAM
// CHANGE LOG
// 
// CHANGES || version VERSION
//
// "Enable/Disable Headbob, Changed look rotations - should result in reduced camera jitters" || version 1.0.1

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Image = UnityEngine.UI.Image;
using Cursor = UnityEngine.Cursor;
using Unity.Netcode;
using Unity.Collections;
#if USE_STEAM
using Steamworks;
#endif

#if UNITY_EDITOR
    using UnityEditor;
    using System.Net;
#endif

public class FirstPersonController : NetworkBehaviour
{
    /// <summary>
    /// Ölüm kaynağı: katil kurbanı (ceset raporlanabilir) veya oylamayla elenen (ceset yok, oy listesinde yok).
    /// </summary>
    public enum PlayerDeathCause : byte
    {
        None = 0,
        ImpostorKill = 1,
        Ejected = 2,
    }

    private Rigidbody rb;

    [Header("Animation")]
    public Animator animator;
    public string speedParameterName = "Speed";

    #region Camera Movement Variables

    public Camera playerCamera;

    public float fov = 60f;
    public bool invertCamera = false;
    public bool cameraCanMove = true;
    public float mouseSensitivity = 2f;
    public float maxLookAngle = 50f;

    // Crosshair
    public bool lockCursor = true;
    public bool crosshair = true;
    public Sprite crosshairImage;
    public Color crosshairColor = Color.white;

    // Internal Variables
    private float yaw = 0.0f;
    private float pitch = 0.0f;
    private Image crosshairObject;

    #region Camera Zoom Variables

    public bool enableZoom = true;
    public bool holdToZoom = false;
    public KeyCode zoomKey = KeyCode.Mouse1;
    public float zoomFOV = 30f;
    public float zoomStepTime = 5f;

    // Internal Variables
    private bool isZoomed = false;

    #endregion
    #endregion

    #region Movement Variables

    public bool playerCanMove = true;
    public float walkSpeed = 5f;
    public float maxVelocityChange = 10f;

    // Internal Variables
    private bool isWalking = false;

    #region Sprint

    public bool enableSprint = true;
    public bool unlimitedSprint = false;
    public KeyCode sprintKey = KeyCode.LeftShift;
    public float sprintSpeed = 7f;
    public float sprintDuration = 5f;
    public float sprintCooldown = .5f;
    public float sprintFOV = 80f;
    public float sprintFOVStepTime = 10f;

    // Sprint Bar
    public bool useSprintBar = true;
    public bool hideBarWhenFull = true;
    public Image sprintBarBG;
    public Image sprintBar;
    public float sprintBarWidthPercent = .3f;
    public float sprintBarHeightPercent = .015f;

    // Internal Variables
    private CanvasGroup sprintBarCG;
    private bool isSprinting = false;
    private float sprintRemaining;
    private float sprintBarWidth;
    private float sprintBarHeight;
    private bool isSprintCooldown = false;
    private float sprintCooldownReset;

    #endregion

    #region Jump

    public bool enableJump = true;
    public KeyCode jumpKey = KeyCode.Space;
    public float jumpPower = 5f;

    // Internal Variables
    [HideInInspector] public bool isGrounded = false;

    #endregion

    #region Crouch

    public bool enableCrouch = true;
    public bool holdToCrouch = true;
    public KeyCode crouchKey = KeyCode.LeftControl;
    public float crouchHeight = .75f;
    public float speedReduction = .5f;

    // Internal Variables
    private bool isCrouched = false;
    private Vector3 originalScale;
    private CapsuleCollider capsuleCollider;

    #endregion
    #endregion

    #region Head Bob

    public bool enableHeadBob = true;
    public Transform joint;
    public float bobSpeed = 10f;
    public Vector3 bobAmount = new Vector3(.15f, .05f, 0f);

    // Internal Variables
    private Vector3 jointOriginalPos;
    private float timer = 0;

    #endregion

    #region Random Event Integration

    public NetworkVariable<Vector3> serverSpawnPosition = new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<Quaternion> serverSpawnRotation = new NetworkVariable<Quaternion>(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> hasServerSpawnPosition = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private bool hasConsumedSpawnPosition = false;

    /// <summary>
    /// true olduğunda WASD yönleri tersine çevrilir.
    /// RandomEventController tarafından kontrol edilir.
    /// </summary>
    [HideInInspector] public bool invertMovementInput = false;

    /// <summary>
    /// true olduğunda mouse look yönleri tersine çevrilir.
    /// RandomEventController tarafından kontrol edilir.
    /// </summary>
    [HideInInspector] public bool invertMouseLook = false;

    #endregion

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsuleCollider = GetComponent<CapsuleCollider>();
        ConfigurePlayerCollider();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        crosshairObject = GetComponentInChildren<Image>();

        // Set internal variables
        playerCamera.fieldOfView = fov;
        originalScale = transform.localScale;
        jointOriginalPos = joint.localPosition;

        if (!unlimitedSprint)
        {
            sprintRemaining = sprintDuration;
            sprintCooldownReset = sprintCooldown;
        }
    }

    private bool IsLocalPlayerControlled()
    {
        if (NetworkManager.Singleton == null) return true;
        if (!NetworkManager.Singleton.IsListening) return true;
        return IsOwner;
    }

    private void ConfigurePlayerCollider()
    {
        if (capsuleCollider == null)
            return;

        PhysicsMaterial noFrictionMaterial = new PhysicsMaterial("Player No Friction")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };

        capsuleCollider.material = noFrictionMaterial;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        isDead.OnValueChanged += OnDeadChanged;
        if (isDead.Value) OnDeadChanged(false, true);

        playerColorIndex.OnValueChanged += OnColorChanged;
        if (IsServer)
        {
            playerColorIndex.Value = (int)(OwnerClientId % 16) + 1;
        }
        ApplyPlayerColor(playerColorIndex.Value);

        if (IsOwner)
        {
            string sName = "Player " + OwnerClientId;
#if USE_STEAM
            try { sName = SteamClient.Name; } catch { }
#endif
            playerName.Value = sName;
        }

        if (IsOwner)
        {
            // Bu benim karakterim, benim kameram olmalı. Özellikleri açıyorum.
            if (playerCamera != null) playerCamera.gameObject.SetActive(true);
            if (crosshairObject != null) crosshairObject.gameObject.SetActive(true);
            
            var listener = playerCamera != null ? playerCamera.GetComponent<AudioListener>() : null;
            if (listener != null) listener.enabled = true;

            // Kendi vücudumuzu içeriden görmemek için Animator'un bağlı olduğu modeldeki tüm parçaları bulup otomatik gizliyoruz (sadece gölge kalıyor)
            if (animator != null)
            {
                Renderer[] bodyRenderers = animator.GetComponentsInChildren<Renderer>();
                foreach (Renderer r in bodyRenderers)
                {
                    if (r != null)
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                }
            }

            Debug.Log("Kendi karakterim doğdu, kamera aktif edildi.");
        }
        else
        {
            // Bu benim karakterim değil (diğer oyuncu), kamerasını ve kulaklığını kapatıyorum.
            if (playerCamera != null) playerCamera.gameObject.SetActive(false);
            if (crosshairObject != null) crosshairObject.gameObject.SetActive(false);
            
            var listener = playerCamera != null ? playerCamera.GetComponent<AudioListener>() : null;
            if (listener != null) listener.enabled = false;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        isDead.OnValueChanged -= OnDeadChanged;
        playerColorIndex.OnValueChanged -= OnColorChanged;
    }

    private void OnColorChanged(int oldVal, int newVal)
    {
        ApplyPlayerColor(newVal);
    }

    private static readonly string[] PlayerColorHexes = new string[]
    {
        "#FFFFFF", // 0 (Kullanılmıyor, fallback)
        "#FFFFFF", // 1: Beyaz
        "#FF0000", // 2: Kırmızı
        "#00FF00", // 3: Yeşil
        "#FFFF00", // 4: Sarı
        "#FF8000", // 5: Turuncu
        "#800080", // 6: Mor
        "#0000FF", // 7: Mavi
        "#FF66B2", // 8: Pembe
        "#808080", // 9: Gri
        "#663300", // 10: Kahverengi
        "#D4AF37", // 11: Altın
        "#F5F5DC", // 12: Bej
        "#000080", // 13: Lacivert
        "#40E0D0", // 14: Turkuaz
        "#800000", // 15: Bordo
        "#4B5320"  // 16: Haki
    };

    private void ApplyPlayerColor(int colorIndex)
    {
        if (colorIndex < 1 || colorIndex > 16) colorIndex = 1;
        
        Color targetColor = Color.white;
        ColorUtility.TryParseHtmlString(PlayerColorHexes[colorIndex], out targetColor);

        Renderer[] allRenderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer rend in allRenderers)
        {
            Material[] sharedMats = rend.materials; // Geçici dizi üzerinden işlem
            bool changed = false;
            for (int i = 0; i < sharedMats.Length; i++)
            {
                // Material atandıktan sonra adına " (Instance)" eklendiği için StartsWith kullanılır
                if (sharedMats[i].name.StartsWith("Renkdegisenbolum"))
                {
                    sharedMats[i].color = targetColor;
                    changed = true;
                }
            }
            if (changed)
            {
                rend.materials = sharedMats; // Diziyi geri ata
            }
        }
    }

    private void OnDeadChanged(bool oldVal, bool newVal)
    {
        if (newVal) Die();
    }

    private bool cursorLockedForGame = false;

    void Start()
    {
        // Fare kilitleme işlemi Update() içinde dinamik olarak sahneye göre yapılacaktır.

        if(crosshair)
        {
            crosshairObject.sprite = crosshairImage;
            crosshairObject.color = crosshairColor;
        }
        else
        {
            crosshairObject.gameObject.SetActive(false);
        }

        #region Sprint Bar

        sprintBarCG = GetComponentInChildren<CanvasGroup>();

        if(useSprintBar)
        {
            sprintBarBG.gameObject.SetActive(true);
            sprintBar.gameObject.SetActive(true);

            float screenWidth = Screen.width;
            float screenHeight = Screen.height;

            sprintBarWidth = screenWidth * sprintBarWidthPercent;
            sprintBarHeight = screenHeight * sprintBarHeightPercent;

            sprintBarBG.rectTransform.sizeDelta = new Vector3(sprintBarWidth, sprintBarHeight, 0f);
            sprintBar.rectTransform.sizeDelta = new Vector3(sprintBarWidth - 2, sprintBarHeight - 2, 0f);

            if(hideBarWhenFull && sprintBarCG != null)
            {
                sprintBarCG.alpha = 0;
            }
        }
        else
        {
            sprintBarBG.gameObject.SetActive(false);
            sprintBar.gameObject.SetActive(false);
        }

        #endregion
        
        // Başlangıçta Spectator yazısını kapat

    }

    float camRotation;

    public NetworkVariable<bool> isDead = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<PlayerDeathCause> deathCause = new NetworkVariable<PlayerDeathCause>(PlayerDeathCause.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    /// <summary>
    /// Katil kurbanı için: false iken ceset görünür/raporlanabilir; rapor sonrası true (görünmez).
    /// Oylamayla ölen için baştan true.
    /// </summary>
    public NetworkVariable<bool> corpseHidden = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<FixedString32Bytes> playerName = new NetworkVariable<FixedString32Bytes>("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<float> networkAnimSpeed = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<int> playerColorIndex = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool CanBeReportedAsBody()
    {
        return isDead.Value && deathCause.Value == PlayerDeathCause.ImpostorKill && !corpseHidden.Value;
    }

    // --- Diğer script'lerin kolayca okuyabileceği statik bayrak ---
    public static bool LocalPlayerIsDead { get; private set; } = false;

    // --- İzleyici (Spectator) Modu Değişkenleri ---
    private int currentSpectateIndex = 0;
    private FirstPersonController spectatedPlayer = null;
    private bool spectatorInitialized = false;

    private void Update()
    {
        if (IsLocalPlayerControlled() && hasServerSpawnPosition.Value && !hasConsumedSpawnPosition)
        {
            hasConsumedSpawnPosition = true;
            transform.SetPositionAndRotation(serverSpawnPosition.Value, serverSpawnRotation.Value);
            if (rb != null)
            {
                rb.position = serverSpawnPosition.Value;
                rb.rotation = serverSpawnRotation.Value;
                rb.linearVelocity = Vector3.zero;
            }
        }

        if (IsLocalPlayerControlled())
        {
            try { UpdateInteractionUI(); }
            catch (System.Exception ex) { Debug.LogError($"[FPC] UpdateInteractionUI Error: {ex.Message}"); }
        }

        if (IsLocalPlayerControlled() &&
            Input.GetKeyDown(KeyCode.F1) &&
            MissionManager.Instance != null &&
            (ComputerUIManager.Instance == null ||
             !ComputerUIManager.Instance.IsComputerOpen) &&
            (CircuitMissionUIManager.Instance == null ||
             !CircuitMissionUIManager.Instance.IsOpen) &&
            (WaveFrequencyUIManager.Instance == null ||
             !WaveFrequencyUIManager.Instance.IsOpen))
        {
            MissionManager.Instance.ActivateValveMissionServerRpc();
        }

        if (animator != null)
        {
            float currentSpeed = 0f;
            if (IsLocalPlayerControlled())
            {
                currentSpeed = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z).magnitude;
                if (IsSpawned) networkAnimSpeed.Value = currentSpeed;
            }
            else
            {
                currentSpeed = networkAnimSpeed.Value;
            }
            animator.SetFloat(speedParameterName, currentSpeed);
        }

        if (!IsLocalPlayerControlled()) return;

        if (isDead.Value)
        {
            HandleSpectator(); // Sadece girişleri (tıklamaları) kontrol et
            return;
        }

        // EĞER ŞU AN ANA MENÜDE VEYA LOBİDE İSEK: Fareyi göster, karakteri dondur ve crosshair'ı kapat!
        bool isMainMenu = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu";
        bool isLobbyMode = GameManager.Instance == null || !GameManager.Instance.isGameStarted.Value;

        if (isMainMenu || isLobbyMode)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            cursorLockedForGame = false;
            if (crosshairObject != null && crosshairObject.gameObject.activeSelf)
            {
                crosshairObject.gameObject.SetActive(false);
            }
            return; 
        }
        // EĞER OYUN SAHNESİNE GEÇTİYSEK VE FARE HENÜZ KİLİTLENMEDİYSE: Kilitle!
        else if (!cursorLockedForGame && lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            cursorLockedForGame = true;
        }

        // Crosshair'i kameranın hareket edebilme durumuna (UI açık/kapalı) göre ayarla
        if (crosshair && crosshairObject != null)
        {
            if (crosshairObject.gameObject.activeSelf != cameraCanMove)
            {
                crosshairObject.gameObject.SetActive(cameraCanMove);
            }
        }

        #region Camera

        // Control camera movement
        if(cameraCanMove)
        {
            float mouseMultiplier = invertMouseLook ? -1f : 1f;
            yaw = transform.localEulerAngles.y + Input.GetAxis("Mouse X") * mouseSensitivity * mouseMultiplier;

            if (!invertCamera)
            {
                pitch -= mouseSensitivity * Input.GetAxis("Mouse Y") * mouseMultiplier;
            }
            else
            {
                // Inverted Y
                pitch += mouseSensitivity * Input.GetAxis("Mouse Y") * mouseMultiplier;
            }

            // Clamp pitch between lookAngle
            pitch = Mathf.Clamp(pitch, -maxLookAngle, maxLookAngle);

            transform.localEulerAngles = new Vector3(0, yaw, 0);
            playerCamera.transform.localEulerAngles = new Vector3(pitch, 0, 0);
        }

        #region Camera Zoom

        if (enableZoom)
        {
            // Changes isZoomed when key is pressed
            // Behavior for toogle zoom
            if(Input.GetKeyDown(zoomKey) && !holdToZoom && !isSprinting)
            {
                if (!isZoomed)
                {
                    isZoomed = true;
                }
                else
                {
                    isZoomed = false;
                }
            }

            // Changes isZoomed when key is pressed
            // Behavior for hold to zoom
            if(holdToZoom && !isSprinting)
            {
                if(Input.GetKeyDown(zoomKey))
                {
                    isZoomed = true;
                }
                else if(Input.GetKeyUp(zoomKey))
                {
                    isZoomed = false;
                }
            }

            // Lerps camera.fieldOfView to allow for a smooth transistion
            if(isZoomed)
            {
                playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, zoomFOV, zoomStepTime * Time.deltaTime);
            }
            else if(!isZoomed && !isSprinting)
            {
                playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, fov, zoomStepTime * Time.deltaTime);
            }
        }

        #endregion
        #endregion

        #region Sprint

        if(enableSprint)
        {
            if(isSprinting)
            {
                isZoomed = false;
                playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, sprintFOV, sprintFOVStepTime * Time.deltaTime);

                // Drain sprint remaining while sprinting
                if(!unlimitedSprint)
                {
                    sprintRemaining -= 1 * Time.deltaTime;
                    if (sprintRemaining <= 0)
                    {
                        isSprinting = false;
                        isSprintCooldown = true;
                    }
                }
            }
            else
            {
                // Regain sprint while not sprinting
                sprintRemaining = Mathf.Clamp(sprintRemaining += 1 * Time.deltaTime, 0, sprintDuration);
            }

            // Handles sprint cooldown 
            // When sprint remaining == 0 stops sprint ability until hitting cooldown
            if(isSprintCooldown)
            {
                sprintCooldown -= 1 * Time.deltaTime;
                if (sprintCooldown <= 0)
                {
                    isSprintCooldown = false;
                }
            }
            else
            {
                sprintCooldown = sprintCooldownReset;
            }

            // Handles sprintBar 
            if(useSprintBar && !unlimitedSprint)
            {
                float sprintRemainingPercent = sprintRemaining / sprintDuration;
                sprintBar.transform.localScale = new Vector3(sprintRemainingPercent, 1f, 1f);
            }
        }

        #endregion

        #region Jump

        // Gets input and calls jump method
        if(enableJump && Input.GetKeyDown(jumpKey) && isGrounded && !isCrouched && !Input.GetKey(crouchKey))
        {
            Jump();
        }

        #endregion

        #region Crouch

        if (enableCrouch)
        {
            if(Input.GetKeyDown(crouchKey) && !holdToCrouch)
            {
                Crouch();
            }
            
            if(Input.GetKeyDown(crouchKey) && holdToCrouch)
            {
                isCrouched = false;
                Crouch();
            }
            else if(Input.GetKeyUp(crouchKey) && holdToCrouch)
            {
                isCrouched = true;
                Crouch();
            }
        }

        #endregion

        CheckGround();

        if(enableHeadBob)
        {
            HeadBob();
        }
    }

    #region Spectator Mode
    private void HandleSpectator()
    {
        if (!spectatorInitialized)
        {
            // Spectator hint text references removed
            spectatorInitialized = true;
        }

        if (Input.GetMouseButtonDown(0)) // Sol tık: İleri
        {
            CycleSpectator(1);
        }
        else if (Input.GetMouseButtonDown(1)) // Sağ tık: Geri
        {
            CycleSpectator(-1);
        }
    }

    private string interactionText = "";
    private float interactionTextTimer = 0f;
    private UnityEngine.UIElements.VisualElement promptContainer;
    private UnityEngine.UIElements.VisualElement promptBox;
    private UnityEngine.UIElements.Label promptKeyLabel;
    private UnityEngine.UIElements.Label promptTextLabel;
    private UnityEngine.UIElements.VisualElement warningImg;
    private UnityEngine.UIElements.VisualElement roleBadge;
    private UnityEngine.UIElements.Label roleBadgeText;
    private bool roleBadgeInitialized = false;

    public void SetInteractionText(string text)
    {
        if (!IsLocalPlayerControlled())
            return;

        interactionText = text;
        interactionTextTimer = Time.time + 0.1f;
    }

    private void UpdateInteractionUI()
    {
        if (promptContainer == null || promptContainer.panel == null)
        {
            promptContainer = null;
            promptBox = null;
            promptKeyLabel = null;
            promptTextLabel = null;

            var documents = FindObjectsByType<UnityEngine.UIElements.UIDocument>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (var doc in documents)
            {
                if (doc == null || doc.rootVisualElement == null)
                    continue;

                var candidate = doc.rootVisualElement.Q<UnityEngine.UIElements.VisualElement>("game-prompt-container");
                if (candidate == null || candidate.panel == null)
                    continue;

                promptContainer = candidate;
                promptBox = promptContainer.Q<UnityEngine.UIElements.VisualElement>(className: "prompt-box");
                promptKeyLabel = promptContainer.Q<UnityEngine.UIElements.Label>(className: "prompt-key");
                promptTextLabel = promptContainer.Q<UnityEngine.UIElements.Label>("game-prompt-text");
                warningImg = doc.rootVisualElement.Q<UnityEngine.UIElements.VisualElement>("valve-warning-image");
                roleBadge = doc.rootVisualElement.Q<UnityEngine.UIElements.VisualElement>("role-badge");
                roleBadgeText = doc.rootVisualElement.Q<UnityEngine.UIElements.Label>("role-badge-text");

                if (promptBox != null && promptKeyLabel != null && promptTextLabel != null)
                {
                    break;
                }

                promptContainer = null;
                promptBox = null;
                promptKeyLabel = null;
                promptTextLabel = null;
            }
        }

        if (MissionManager.Instance != null && MissionManager.Instance.IsValveMissionActive.Value)
        {
            float pulse = Mathf.PingPong(Time.time * 2f, 1f); // Oscillates between 0 and 1
            
            if (warningImg != null)
            {
                warningImg.style.display = UnityEngine.UIElements.DisplayStyle.Flex;
                warningImg.style.opacity = Mathf.Lerp(0.7f, 1f, pulse);
            }
        }
        else
        {
            if (warningImg != null) warningImg.style.display = UnityEngine.UIElements.DisplayStyle.None;
        }

        // ── Rol Badge Güncelleme ──
        if (roleBadge != null && roleBadgeText != null && !roleBadgeInitialized)
        {
            if (RoleManager.Instance != null && RoleManager.Instance.AreRolesDistributed())
            {
                PlayerRole myRole = RoleManager.Instance.GetLocalPlayerRole();
                if (myRole != PlayerRole.None)
                {
                    roleBadgeInitialized = true;
                    roleBadge.RemoveFromClassList("hidden");

                    if (myRole == PlayerRole.Impostor)
                    {
                        roleBadgeText.text = "KATİL";
                        roleBadge.AddToClassList("impostor");
                    }
                    else
                    {
                        roleBadgeText.text = "KÖYLÜ";
                    }
                }
            }
        }

        if (promptContainer != null && promptTextLabel != null && promptKeyLabel != null && promptBox != null)
        {
            if (Time.time < interactionTextTimer && !string.IsNullOrEmpty(interactionText) && IsLocalPlayerControlled() && !isDead.Value)
            {
                bool isInfoOnly = !interactionText.Contains("[F]");
                string cleanText = interactionText.Replace("[F] ", "").Replace("[Batarya Gerekiyor]", "Batarya Gerekiyor");

                promptTextLabel.text = cleanText;

                if (isInfoOnly)
                {
                    promptKeyLabel.style.display = UnityEngine.UIElements.DisplayStyle.None;
                    promptBox.style.backgroundColor = new UnityEngine.UIElements.StyleColor(new Color(6f/255f, 12f/255f, 22f/255f, 0.8f));
                    var redBorder = new UnityEngine.UIElements.StyleColor(new Color(1f, 0.3f, 0.3f, 0.6f));
                    promptBox.style.borderTopColor = redBorder;
                    promptBox.style.borderBottomColor = redBorder;
                    promptBox.style.borderLeftColor = redBorder;
                    promptBox.style.borderRightColor = redBorder;
                }
                else
                {
                    promptKeyLabel.style.display = UnityEngine.UIElements.DisplayStyle.Flex;
                    promptBox.style.backgroundColor = new UnityEngine.UIElements.StyleColor(new Color(6f/255f, 12f/255f, 22f/255f, 0.8f));
                    var blueBorder = new UnityEngine.UIElements.StyleColor(new Color(0f, 240f/255f, 255f/255f, 0.4f));
                    promptBox.style.borderTopColor = blueBorder;
                    promptBox.style.borderBottomColor = blueBorder;
                    promptBox.style.borderLeftColor = blueBorder;
                    promptBox.style.borderRightColor = blueBorder;
                }

                promptContainer.RemoveFromClassList("hidden");
            }
            else
            {
                promptContainer.AddToClassList("hidden");
            }
        }
    }

    private void OnGUI()
    {
        // OnGUI interaction text logic removed, replaced by UI Toolkit (UpdateInteractionUI)
    }

    private void LateUpdate()
    {
        if (!IsLocalPlayerControlled()) return;

        // İzleyici modundaysak, kamera takibini LateUpdate'te yapıyoruz ki titreme (jitter) olmasın.
        if (isDead.Value)
        {
            SpectateCurrentTarget();
        }
    }

    private void CycleSpectator(int direction)
    {
        // Sahnedeki tüm oyuncu kontrolcülerini bul
        FirstPersonController[] allPlayers = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
        List<FirstPersonController> alivePlayers = new List<FirstPersonController>();
        
        foreach(var p in allPlayers)
        {
            // Adamlar ölü değilse ve Network olarak oyundaysa ekle
            if (!p.isDead.Value && p.IsSpawned)
            {
                alivePlayers.Add(p);
            }
        }

        if (alivePlayers.Count == 0) 
        {
            spectatedPlayer = null;
            return; // Kimse hayatta değilse izleyecek kimse yok
        }

        currentSpectateIndex += direction;
        
        // Liste dışına çıkmayı engelle (Başa sar)
        if (currentSpectateIndex >= alivePlayers.Count) currentSpectateIndex = 0;
        if (currentSpectateIndex < 0) currentSpectateIndex = alivePlayers.Count - 1;

        spectatedPlayer = alivePlayers[currentSpectateIndex];
    }

    private void SpectateCurrentTarget()
    {
        if (spectatedPlayer != null && !spectatedPlayer.isDead.Value && spectatedPlayer.IsSpawned)
        {
            // İzlenen kişinin gözlerine yerleş
            if (playerCamera != null && spectatedPlayer.playerCamera != null)
            {
                playerCamera.transform.position = spectatedPlayer.playerCamera.transform.position;
                playerCamera.transform.rotation = spectatedPlayer.playerCamera.transform.rotation;
            }
        }
        else
        {
            // Hedef sonradan ölürse veya düşerse, bir kereden sonraya atla
            CycleSpectator(1);
        }
    }
    #endregion

    void FixedUpdate()
    {
        if (!IsLocalPlayerControlled()) return;
        if (isDead.Value) return;

        bool isMainMenu = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu";
        bool isLobbyMode = GameManager.Instance == null || !GameManager.Instance.isGameStarted.Value;
        if (isMainMenu || isLobbyMode) return;

        #region Movement

        if (playerCanMove)
        {
            // Calculate how fast we should be moving
            float inputMultiplier = invertMovementInput ? -1f : 1f;
            Vector3 targetVelocity = new Vector3(Input.GetAxis("Horizontal") * inputMultiplier, 0, Input.GetAxis("Vertical") * inputMultiplier);

            // Checks if player is walking and isGrounded
            // Will allow head bob
            if ((targetVelocity.x != 0 || targetVelocity.z != 0) && isGrounded)
            {
                isWalking = true;
            }
            else
            {
                isWalking = false;
            }

            // All movement calculations shile sprint is active
            if (enableSprint && Input.GetKey(sprintKey) && sprintRemaining > 0f && !isSprintCooldown)
            {
                targetVelocity = transform.TransformDirection(targetVelocity) * sprintSpeed;

                // Apply a force that attempts to reach our target velocity
                Vector3 velocity = rb.linearVelocity;
                Vector3 velocityChange = (targetVelocity - velocity);
                velocityChange.x = Mathf.Clamp(velocityChange.x, -maxVelocityChange, maxVelocityChange);
                velocityChange.z = Mathf.Clamp(velocityChange.z, -maxVelocityChange, maxVelocityChange);
                velocityChange.y = 0;

                // Player is only moving when valocity change != 0
                // Makes sure fov change only happens during movement
                if (velocityChange.x != 0 || velocityChange.z != 0)
                {
                    isSprinting = true;

                    if (isCrouched)
                    {
                        Crouch();
                    }

                    if (hideBarWhenFull && !unlimitedSprint && sprintBarCG != null)
                    {
                        sprintBarCG.alpha += 5 * Time.deltaTime;
                    }
                }

                rb.AddForce(velocityChange, ForceMode.VelocityChange);
            }
            // All movement calculations while walking
            else
            {
                isSprinting = false;

                if (hideBarWhenFull && sprintRemaining == sprintDuration && sprintBarCG != null)
                {
                    sprintBarCG.alpha -= 3 * Time.deltaTime;
                }

                targetVelocity = transform.TransformDirection(targetVelocity) * walkSpeed;

                // Apply a force that attempts to reach our target velocity
                Vector3 velocity = rb.linearVelocity;
                Vector3 velocityChange = (targetVelocity - velocity);
                velocityChange.x = Mathf.Clamp(velocityChange.x, -maxVelocityChange, maxVelocityChange);
                velocityChange.z = Mathf.Clamp(velocityChange.z, -maxVelocityChange, maxVelocityChange);
                velocityChange.y = 0;

                rb.AddForce(velocityChange, ForceMode.VelocityChange);
            }
        }
        else
        {
            // Mission UIs disable movement, but any velocity from the previous frame must not carry the player away.
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
            isWalking = false;
            isSprinting = false;
        }

        #endregion
    }

    // Sets isGrounded based on a raycast sent straigth down from the player object
    private void CheckGround()
    {
        Bounds playerBounds = capsuleCollider != null ? capsuleCollider.bounds : new Bounds(transform.position, transform.lossyScale);
        Vector3 origin = new Vector3(playerBounds.center.x, playerBounds.min.y + 0.08f, playerBounds.center.z);
        Vector3 direction = transform.TransformDirection(Vector3.down);
        float distance = .16f;

        if (Physics.Raycast(origin, direction, out RaycastHit hit, distance))
        {
            Debug.DrawRay(origin, direction * distance, Color.red);
            isGrounded = true;
        }
        else
        {
            isGrounded = false;
        }
    }

    private void Jump()
    {
        // Adds force to the player rigidbody to jump
        if (isGrounded)
        {
            rb.AddForce(0f, jumpPower, 0f, ForceMode.Impulse);
            isGrounded = false;
        }

        // When crouched and using toggle system, will uncrouch for a jump
        if(isCrouched && !holdToCrouch)
        {
            Crouch();
        }
    }

    private void Crouch()
    {
        // Stands player up to full height
        // Brings walkSpeed back up to original speed
        if(isCrouched)
        {
            transform.localScale = new Vector3(originalScale.x, originalScale.y, originalScale.z);
            walkSpeed /= speedReduction;

            isCrouched = false;
        }
        // Crouches player down to set height
        // Reduces walkSpeed
        else
        {
            transform.localScale = new Vector3(originalScale.x, originalScale.y * crouchHeight, originalScale.z);
            walkSpeed *= speedReduction;

            isCrouched = true;
        }
    }

    private void HeadBob()
    {
        if(isWalking)
        {
            // Calculates HeadBob speed during sprint
            if(isSprinting)
            {
                timer += Time.deltaTime * (bobSpeed + sprintSpeed);
            }
            // Calculates HeadBob speed during crouched movement
            else if (isCrouched)
            {
                timer += Time.deltaTime * (bobSpeed * speedReduction);
            }
            // Calculates HeadBob speed during walking
            else
            {
                timer += Time.deltaTime * bobSpeed;
            }
            // Applies HeadBob movement
            joint.localPosition = new Vector3(jointOriginalPos.x + Mathf.Sin(timer) * bobAmount.x, jointOriginalPos.y + Mathf.Sin(timer) * bobAmount.y, jointOriginalPos.z + Mathf.Sin(timer) * bobAmount.z);
        }
        else
        {
            // Resets when play stops moving
            timer = 0;
            joint.localPosition = new Vector3(Mathf.Lerp(joint.localPosition.x, jointOriginalPos.x, Time.deltaTime * bobSpeed), Mathf.Lerp(joint.localPosition.y, jointOriginalPos.y, Time.deltaTime * bobSpeed), Mathf.Lerp(joint.localPosition.z, jointOriginalPos.z, Time.deltaTime * bobSpeed));
        }
    }

    public void Die()
    {
        // Not: isDead.Value artık server tarafından set ediliyor.
        playerCanMove = false;
        cameraCanMove = false;
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
        }

        if (IsLocalPlayerControlled())
        {
            Debug.Log("ÖLDÜRÜLDÜN!");
            // Role manager references removed


            // Spectator modunu otomatik başlat — ilk hayatta kalana geç
            spectatorInitialized = false; // Hint mesajını yeniden göster
            CycleSpectator(1);

            // Cursor'u serbest bırak: sol/sağ tıkla kamera değiştirebilsin
            lockCursor = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            cursorLockedForGame = false;

            // Statik bayrağı güncelle
            LocalPlayerIsDead = true;
        }
    }
    [ClientRpc]
    public void TeleportClientRpc(Vector3 position, Quaternion rotation)
    {
        transform.SetPositionAndRotation(position, rotation);
        
        if (rb != null)
        {
            rb.position = position;
            rb.rotation = rotation;
            rb.linearVelocity = Vector3.zero;
        }
    }
}


// Custom Editor
#if UNITY_EDITOR
    [CustomEditor(typeof(FirstPersonController)), InitializeOnLoadAttribute]
    public class FirstPersonControllerEditor : Editor
    {
    FirstPersonController fpc;
    SerializedObject SerFPC;

    private void OnEnable()
    {
        fpc = (FirstPersonController)target;
        SerFPC = new SerializedObject(fpc);
    }

    public override void OnInspectorGUI()
    {
        SerFPC.Update();

        EditorGUILayout.Space();
        GUILayout.Label("Modular First Person Controller", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 16 });
        GUILayout.Label("By Jess Case", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Normal, fontSize = 12 });
        GUILayout.Label("version 1.0.1", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Normal, fontSize = 12 });
        EditorGUILayout.Space();

        #region Camera Setup

        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        GUILayout.Label("Camera Setup", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));
        EditorGUILayout.Space();

        fpc.playerCamera = (Camera)EditorGUILayout.ObjectField(new GUIContent("Camera", "Camera attached to the controller."), fpc.playerCamera, typeof(Camera), true);
        fpc.fov = EditorGUILayout.Slider(new GUIContent("Field of View", "The camera’s view angle. Changes the player camera directly."), fpc.fov, fpc.zoomFOV, 179f);
        fpc.cameraCanMove = EditorGUILayout.ToggleLeft(new GUIContent("Enable Camera Rotation", "Determines if the camera is allowed to move."), fpc.cameraCanMove);

        GUI.enabled = fpc.cameraCanMove;
        fpc.invertCamera = EditorGUILayout.ToggleLeft(new GUIContent("Invert Camera Rotation", "Inverts the up and down movement of the camera."), fpc.invertCamera);
        fpc.mouseSensitivity = EditorGUILayout.Slider(new GUIContent("Look Sensitivity", "Determines how sensitive the mouse movement is."), fpc.mouseSensitivity, .1f, 10f);
        fpc.maxLookAngle = EditorGUILayout.Slider(new GUIContent("Max Look Angle", "Determines the max and min angle the player camera is able to look."), fpc.maxLookAngle, 40, 90);
        GUI.enabled = true;

        fpc.lockCursor = EditorGUILayout.ToggleLeft(new GUIContent("Lock and Hide Cursor", "Turns off the cursor visibility and locks it to the middle of the screen."), fpc.lockCursor);

        fpc.crosshair = EditorGUILayout.ToggleLeft(new GUIContent("Auto Crosshair", "Determines if the basic crosshair will be turned on, and sets is to the center of the screen."), fpc.crosshair);

        // Only displays crosshair options if crosshair is enabled
        if(fpc.crosshair) 
        { 
            EditorGUI.indentLevel++; 
            EditorGUILayout.BeginHorizontal(); 
            EditorGUILayout.PrefixLabel(new GUIContent("Crosshair Image", "Sprite to use as the crosshair.")); 
            fpc.crosshairImage = (Sprite)EditorGUILayout.ObjectField(fpc.crosshairImage, typeof(Sprite), false);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            fpc.crosshairColor = EditorGUILayout.ColorField(new GUIContent("Crosshair Color", "Determines the color of the crosshair."), fpc.crosshairColor);
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel--; 
        }

        EditorGUILayout.Space();

        #region Camera Zoom Setup

        GUILayout.Label("Zoom", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));

        fpc.enableZoom = EditorGUILayout.ToggleLeft(new GUIContent("Enable Zoom", "Determines if the player is able to zoom in while playing."), fpc.enableZoom);

        GUI.enabled = fpc.enableZoom;
        fpc.holdToZoom = EditorGUILayout.ToggleLeft(new GUIContent("Hold to Zoom", "Requires the player to hold the zoom key instead if pressing to zoom and unzoom."), fpc.holdToZoom);
        fpc.zoomKey = (KeyCode)EditorGUILayout.EnumPopup(new GUIContent("Zoom Key", "Determines what key is used to zoom."), fpc.zoomKey);
        fpc.zoomFOV = EditorGUILayout.Slider(new GUIContent("Zoom FOV", "Determines the field of view the camera zooms to."), fpc.zoomFOV, .1f, fpc.fov);
        fpc.zoomStepTime = EditorGUILayout.Slider(new GUIContent("Step Time", "Determines how fast the FOV transitions while zooming in."), fpc.zoomStepTime, .1f, 10f);
        GUI.enabled = true;

        #endregion

        #endregion

        #region Movement Setup

        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        GUILayout.Label("Movement Setup", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));
        EditorGUILayout.Space();

        fpc.playerCanMove = EditorGUILayout.ToggleLeft(new GUIContent("Enable Player Movement", "Determines if the player is allowed to move."), fpc.playerCanMove);

        GUI.enabled = fpc.playerCanMove;
        fpc.walkSpeed = EditorGUILayout.Slider(new GUIContent("Walk Speed", "Determines how fast the player will move while walking."), fpc.walkSpeed, .1f, fpc.sprintSpeed);
        GUI.enabled = true;

        EditorGUILayout.Space();

        #region Sprint

        GUILayout.Label("Sprint", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));

        fpc.enableSprint = EditorGUILayout.ToggleLeft(new GUIContent("Enable Sprint", "Determines if the player is allowed to sprint."), fpc.enableSprint);

        GUI.enabled = fpc.enableSprint;
        fpc.unlimitedSprint = EditorGUILayout.ToggleLeft(new GUIContent("Unlimited Sprint", "Determines if 'Sprint Duration' is enabled. Turning this on will allow for unlimited sprint."), fpc.unlimitedSprint);
        fpc.sprintKey = (KeyCode)EditorGUILayout.EnumPopup(new GUIContent("Sprint Key", "Determines what key is used to sprint."), fpc.sprintKey);
        fpc.sprintSpeed = EditorGUILayout.Slider(new GUIContent("Sprint Speed", "Determines how fast the player will move while sprinting."), fpc.sprintSpeed, fpc.walkSpeed, 20f);

        //GUI.enabled = !fpc.unlimitedSprint;
        fpc.sprintDuration = EditorGUILayout.Slider(new GUIContent("Sprint Duration", "Determines how long the player can sprint while unlimited sprint is disabled."), fpc.sprintDuration, 1f, 20f);
        fpc.sprintCooldown = EditorGUILayout.Slider(new GUIContent("Sprint Cooldown", "Determines how long the recovery time is when the player runs out of sprint."), fpc.sprintCooldown, .1f, fpc.sprintDuration);
        //GUI.enabled = true;

        fpc.sprintFOV = EditorGUILayout.Slider(new GUIContent("Sprint FOV", "Determines the field of view the camera changes to while sprinting."), fpc.sprintFOV, fpc.fov, 179f);
        fpc.sprintFOVStepTime = EditorGUILayout.Slider(new GUIContent("Step Time", "Determines how fast the FOV transitions while sprinting."), fpc.sprintFOVStepTime, .1f, 20f);

        fpc.useSprintBar = EditorGUILayout.ToggleLeft(new GUIContent("Use Sprint Bar", "Determines if the default sprint bar will appear on screen."), fpc.useSprintBar);

        // Only displays sprint bar options if sprint bar is enabled
        if(fpc.useSprintBar)
        {
            EditorGUI.indentLevel++;

            EditorGUILayout.BeginHorizontal();
            fpc.hideBarWhenFull = EditorGUILayout.ToggleLeft(new GUIContent("Hide Full Bar", "Hides the sprint bar when sprint duration is full, and fades the bar in when sprinting. Disabling this will leave the bar on screen at all times when the sprint bar is enabled."), fpc.hideBarWhenFull);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(new GUIContent("Bar BG", "Object to be used as sprint bar background."));
            fpc.sprintBarBG = (Image)EditorGUILayout.ObjectField(fpc.sprintBarBG, typeof(Image), true);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(new GUIContent("Bar", "Object to be used as sprint bar foreground."));
            fpc.sprintBar = (Image)EditorGUILayout.ObjectField(fpc.sprintBar, typeof(Image), true);
            EditorGUILayout.EndHorizontal();


            EditorGUILayout.BeginHorizontal();
            fpc.sprintBarWidthPercent = EditorGUILayout.Slider(new GUIContent("Bar Width", "Determines the width of the sprint bar."), fpc.sprintBarWidthPercent, .1f, .5f);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            fpc.sprintBarHeightPercent = EditorGUILayout.Slider(new GUIContent("Bar Height", "Determines the height of the sprint bar."), fpc.sprintBarHeightPercent, .001f, .025f);
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel--;
        }
        GUI.enabled = true;

        EditorGUILayout.Space();

        #endregion

        #region Jump

        GUILayout.Label("Jump", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));

        fpc.enableJump = EditorGUILayout.ToggleLeft(new GUIContent("Enable Jump", "Determines if the player is allowed to jump."), fpc.enableJump);

        GUI.enabled = fpc.enableJump;
        fpc.jumpKey = (KeyCode)EditorGUILayout.EnumPopup(new GUIContent("Jump Key", "Determines what key is used to jump."), fpc.jumpKey);
        fpc.jumpPower = EditorGUILayout.Slider(new GUIContent("Jump Power", "Determines how high the player will jump."), fpc.jumpPower, .1f, 20f);
        GUI.enabled = true;

        EditorGUILayout.Space();

        #endregion

        #region Crouch

        GUILayout.Label("Crouch", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));

        fpc.enableCrouch = EditorGUILayout.ToggleLeft(new GUIContent("Enable Crouch", "Determines if the player is allowed to crouch."), fpc.enableCrouch);

        GUI.enabled = fpc.enableCrouch;
        fpc.holdToCrouch = EditorGUILayout.ToggleLeft(new GUIContent("Hold To Crouch", "Requires the player to hold the crouch key instead if pressing to crouch and uncrouch."), fpc.holdToCrouch);
        fpc.crouchKey = (KeyCode)EditorGUILayout.EnumPopup(new GUIContent("Crouch Key", "Determines what key is used to crouch."), fpc.crouchKey);
        fpc.crouchHeight = EditorGUILayout.Slider(new GUIContent("Crouch Height", "Determines the y scale of the player object when crouched."), fpc.crouchHeight, .1f, 1);
        fpc.speedReduction = EditorGUILayout.Slider(new GUIContent("Speed Reduction", "Determines the percent 'Walk Speed' is reduced by. 1 being no reduction, and .5 being half."), fpc.speedReduction, .1f, 1);
        GUI.enabled = true;

        #endregion

        #endregion

        #region Head Bob

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        GUILayout.Label("Head Bob Setup", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));
        EditorGUILayout.Space();

        fpc.enableHeadBob = EditorGUILayout.ToggleLeft(new GUIContent("Enable Head Bob", "Determines if the camera will bob while the player is walking."), fpc.enableHeadBob);
        

        GUI.enabled = fpc.enableHeadBob;
        fpc.joint = (Transform)EditorGUILayout.ObjectField(new GUIContent("Camera Joint", "Joint object position is moved while head bob is active."), fpc.joint, typeof(Transform), true);
        fpc.bobSpeed = EditorGUILayout.Slider(new GUIContent("Speed", "Determines how often a bob rotation is completed."), fpc.bobSpeed, 1, 20);
        fpc.bobAmount = EditorGUILayout.Vector3Field(new GUIContent("Bob Amount", "Determines the amount the joint moves in both directions on every axes."), fpc.bobAmount);
        GUI.enabled = true;

        #endregion

        //Sets any changes from the prefab
        if(GUI.changed)
        {
            EditorUtility.SetDirty(fpc);
            Undo.RecordObject(fpc, "FPC Change");
            SerFPC.ApplyModifiedProperties();
        }
    }

}

#endif
