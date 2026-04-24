using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public enum PlayerRole
{
    None,
    Crewmate,
    Impostor
}

public class RoleManager : NetworkBehaviour
{
    public static RoleManager Instance { get; private set; }

    [Header("Arayuz (UI) Ayarlari")]
    [Tooltip("Arayuzde sag ustte rolu gosterecek TextMeshPro componenti.")]
    public TextMeshProUGUI roleText;

    [Tooltip("Sag altta belirecek 'E - Oldur' yazisi (TMP)")]
    public TextMeshProUGUI killText;

    [Tooltip("Bekleme suresi, sayac yazisi (TMP)")]
    public TextMeshProUGUI cooldownText;

    [Tooltip("Oldukten sonra cikacak Izleyici Modu bilgi yazisi (TMP)")]
    public TextMeshProUGUI spectatorHintText;

    [Tooltip("Ceset yanindayken cikacak 'E - Cesedi Bildir' yazisi (TMP)")]
    public TextMeshProUGUI reportBodyText;

    private PlayerRole localPlayerRole = PlayerRole.None;
    private GameObject singlePlayerRolePanel;
    private TextMeshProUGUI singlePlayerRoleTitle;
    private TextMeshProUGUI singlePlayerRoleHint;
    private FirstPersonController cachedLocalController;
    private bool cachedPlayerCanMove;
    private bool cachedCameraCanMove;
    private bool hasCachedControlState;
    private bool singlePlayerChoicePending;

    private void Awake()
    {
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
            Invoke(nameof(AssignRoles), 2f);
        }
    }

    public void AssignRoles()
    {
        if (!IsServer)
        {
            Debug.LogWarning("Roller sadece Sunucu (Server/Host) tarafindan dagitilmalidir.");
            return;
        }

        List<ulong> clientIds = NetworkManager.Singleton.ConnectedClientsIds.ToList();
        ShuffleList(clientIds);

        int playerCount = clientIds.Count;
        int impostorCount = 1;

        if (playerCount >= 5)
        {
            impostorCount = 2;
        }

        if (impostorCount > playerCount)
        {
            impostorCount = playerCount;
        }

        if (playerCount == 1)
        {
            ulong onlyClientId = clientIds[0];
            ClientRpcParams clientRpcParams = CreateTargetRpcParams(onlyClientId);
            ShowSinglePlayerRoleSelectionClientRpc(clientRpcParams);
            Debug.Log("Tek oyunculu oyun algilandi. Rol secim ekrani gosteriliyor.");
            return;
        }

        List<ulong> impostorIds = new List<ulong>();
        List<ulong> crewmateIds = new List<ulong>();

        for (int i = 0; i < playerCount; i++)
        {
            ulong targetId = clientIds[i];
            PlayerRole assignedRole = i < impostorCount ? PlayerRole.Impostor : PlayerRole.Crewmate;
            if (assignedRole == PlayerRole.Impostor) impostorIds.Add(targetId);
            else crewmateIds.Add(targetId);
            ReceiveRoleClientRpc(assignedRole, CreateTargetRpcParams(targetId));
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.StartGame(crewmateIds, impostorIds);
        }

        Debug.Log($"Rol atamasi tamamlandi: {playerCount} oyuncunun {impostorCount} tanesi katil yapildi.");
    }

    [ClientRpc]
    private void ShowSinglePlayerRoleSelectionClientRpc(ClientRpcParams clientRpcParams = default)
    {
        singlePlayerChoicePending = true;
        EnsureSinglePlayerRoleSelectionUi();
        SetLocalPlayerControlLocked(true);

        if (singlePlayerRolePanel != null)
        {
            singlePlayerRolePanel.SetActive(true);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubmitSinglePlayerRoleChoiceServerRpc(PlayerRole chosenRole, ServerRpcParams serverRpcParams = default)
    {
        if (!IsServer)
        {
            return;
        }

        if (chosenRole != PlayerRole.Crewmate && chosenRole != PlayerRole.Impostor)
        {
            Debug.LogWarning("Gecersiz tek oyuncu rol secimi geldi.");
            return;
        }

        if (NetworkManager.Singleton == null || NetworkManager.Singleton.ConnectedClientsIds.Count != 1)
        {
            Debug.LogWarning("Tek oyuncu rol secimi, oyuncu sayisi degistigi icin reddedildi.");
            return;
        }

        ulong onlyClientId = NetworkManager.Singleton.ConnectedClientsIds[0];
        if (serverRpcParams.Receive.SenderClientId != onlyClientId)
        {
            Debug.LogWarning("Tek oyuncu rol secimi beklenmeyen bir istemciden geldi.");
            return;
        }

        ClientRpcParams clientRpcParams = CreateTargetRpcParams(onlyClientId);
        ReceiveRoleClientRpc(chosenRole, clientRpcParams);
        HideSinglePlayerRoleSelectionClientRpc(clientRpcParams);

        List<ulong> impostorIds = new List<ulong>();
        List<ulong> crewmateIds = new List<ulong>();
        if (chosenRole == PlayerRole.Impostor) impostorIds.Add(onlyClientId);
        else crewmateIds.Add(onlyClientId);

        if (GameManager.Instance != null)
        {
            GameManager.Instance.StartGame(crewmateIds, impostorIds);
        }

        Debug.Log("Tek oyuncu kendi rolunu secti: " + chosenRole);
    }

    [ClientRpc]
    private void HideSinglePlayerRoleSelectionClientRpc(ClientRpcParams clientRpcParams = default)
    {
        singlePlayerChoicePending = false;

        if (singlePlayerRolePanel != null)
        {
            singlePlayerRolePanel.SetActive(false);
        }

        SetLocalPlayerControlLocked(false);
    }

    [ClientRpc]
    private void ReceiveRoleClientRpc(PlayerRole role, ClientRpcParams clientRpcParams = default)
    {
        localPlayerRole = role;
        UpdateRoleUI(role);
        Debug.Log("Rol atamam ulasti: " + role);
    }

    private void UpdateRoleUI(PlayerRole role)
    {
        if (roleText == null)
        {
            Debug.LogWarning("RoleManager icindeki RoleText (TMP) bos. Hierarchy'den atama yapmayi unutmayin.");
            return;
        }

        if (role == PlayerRole.Impostor)
        {
            roleText.text = "Rolun: Katil";
            roleText.color = Color.red;
        }
        else if (role == PlayerRole.Crewmate)
        {
            roleText.text = "Rolun: Koylu";
            roleText.color = Color.green;
        }
        else
        {
            roleText.text = "Rol Seciliyor...";
            roleText.color = Color.white;
        }
    }

    private void EnsureSinglePlayerRoleSelectionUi()
    {
        if (singlePlayerRolePanel != null)
        {
            return;
        }

        Canvas canvas = GameObject.Find("Canvas") != null
            ? GameObject.Find("Canvas").GetComponent<Canvas>()
            : FindFirstObjectByType<Canvas>();

        if (canvas == null)
        {
            Debug.LogWarning("Tek oyuncu rol secim ekrani icin Canvas bulunamadi.");
            return;
        }

        singlePlayerRolePanel = CreateUiObject("SinglePlayerRolePanel", canvas.transform, typeof(Image));
        RectTransform panelRect = singlePlayerRolePanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(520f, 280f);

        Image panelImage = singlePlayerRolePanel.GetComponent<Image>();
        panelImage.color = new Color(0f, 0f, 0f, 0.88f);

        singlePlayerRoleTitle = CreateText("TitleText", singlePlayerRolePanel.transform, "Rolunu Sec", 34f);
        RectTransform titleRect = singlePlayerRoleTitle.rectTransform;
        titleRect.anchorMin = new Vector2(0.5f, 1f);
        titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -24f);
        titleRect.sizeDelta = new Vector2(420f, 52f);

        singlePlayerRoleHint = CreateText("HintText", singlePlayerRolePanel.transform, "Tek kisi basladin. Koylu veya Katil olmayi sec.", 24f);
        RectTransform hintRect = singlePlayerRoleHint.rectTransform;
        hintRect.anchorMin = new Vector2(0.5f, 0.5f);
        hintRect.anchorMax = new Vector2(0.5f, 0.5f);
        hintRect.pivot = new Vector2(0.5f, 0.5f);
        hintRect.anchoredPosition = new Vector2(0f, 36f);
        hintRect.sizeDelta = new Vector2(440f, 80f);

        Button villagerButton = CreateButton("VillagerButton", singlePlayerRolePanel.transform, "Koylu", new Vector2(-110f, -78f));
        villagerButton.onClick.RemoveAllListeners();
        villagerButton.onClick.AddListener(() => OnSinglePlayerRoleChosen(PlayerRole.Crewmate));

        Button killerButton = CreateButton("KillerButton", singlePlayerRolePanel.transform, "Katil", new Vector2(110f, -78f));
        killerButton.onClick.RemoveAllListeners();
        killerButton.onClick.AddListener(() => OnSinglePlayerRoleChosen(PlayerRole.Impostor));

        singlePlayerRolePanel.SetActive(false);
    }

    private void OnSinglePlayerRoleChosen(PlayerRole chosenRole)
    {
        if (!singlePlayerChoicePending)
        {
            return;
        }

        singlePlayerChoicePending = false;
        SubmitSinglePlayerRoleChoiceServerRpc(chosenRole);
    }

    private void SetLocalPlayerControlLocked(bool isLocked)
    {
        if (cachedLocalController == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
            {
                cachedLocalController = playerObject.GetComponent<FirstPersonController>();
            }
        }

        if (cachedLocalController != null)
        {
            if (isLocked)
            {
                if (!hasCachedControlState)
                {
                    cachedPlayerCanMove = cachedLocalController.playerCanMove;
                    cachedCameraCanMove = cachedLocalController.cameraCanMove;
                    hasCachedControlState = true;
                }

                cachedLocalController.playerCanMove = false;
                cachedLocalController.cameraCanMove = false;
            }
            else if (hasCachedControlState)
            {
                cachedLocalController.playerCanMove = cachedPlayerCanMove;
                cachedLocalController.cameraCanMove = cachedCameraCanMove;
                hasCachedControlState = false;
            }
        }

        Cursor.visible = isLocked;
        Cursor.lockState = isLocked ? CursorLockMode.None : CursorLockMode.Locked;
    }

    private static ClientRpcParams CreateTargetRpcParams(ulong clientId)
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { clientId }
            }
        };
    }

    private static GameObject CreateUiObject(string objectName, Transform parent, params System.Type[] extraComponents)
    {
        Transform existing = parent.Find(objectName);
        GameObject uiObject = existing != null
            ? existing.gameObject
            : new GameObject(objectName, typeof(RectTransform));

        if (existing == null)
        {
            uiObject.transform.SetParent(parent, false);
        }

        for (int i = 0; i < extraComponents.Length; i++)
        {
            if (uiObject.GetComponent(extraComponents[i]) == null)
            {
                uiObject.AddComponent(extraComponents[i]);
            }
        }

        return uiObject;
    }

    private static TextMeshProUGUI CreateText(string objectName, Transform parent, string content, float fontSize)
    {
        GameObject textObject = CreateUiObject(objectName, parent, typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset != null
            ? TMP_Settings.defaultFontAsset
            : Resources.Load<TMP_FontAsset>("LiberationSans SDF");
        text.text = content;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }

    private static Button CreateButton(string objectName, Transform parent, string label, Vector2 anchoredPosition)
    {
        GameObject buttonObject = CreateUiObject(objectName, parent, typeof(CanvasRenderer), typeof(Image), typeof(Button));
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = anchoredPosition;
        buttonRect.sizeDelta = new Vector2(180f, 52f);

        Image buttonImage = buttonObject.GetComponent<Image>();
        buttonImage.color = new Color(0.18f, 0.18f, 0.18f, 1f);

        TextMeshProUGUI labelText = CreateText("Label", buttonObject.transform, label, 24f);
        RectTransform labelRect = labelText.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = buttonImage;
        return button;
    }

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
