using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

public class UpgradeUIManager : MonoBehaviour
{
    private static UpgradeUIManager instance;
    private static byte[] queuedOffers;

    public static bool IsSelectionOpen => instance != null && instance.isOpen;

    private UIDocument document;
    private VisualElement root;
    private VisualElement overlay;
    private Label eyebrow;
    private Label title;
    private Label detail;
    private readonly VisualElement[] cardSlots = new VisualElement[3];
    private readonly Label[] cardIndexes = new Label[3];
    private readonly Label[] cardTitles = new Label[3];
    private readonly Label[] cardDescriptions = new Label[3];
    private readonly Button[] cardButtons = new Button[3];
    private FirstPersonController localPlayer;
    private readonly UpgradeCardId[] localOffers = new UpgradeCardId[3];
    private bool offersReady;
    private bool isOpen;

    public static void CreateIfNeeded(UpgradeManager manager)
    {
        if (instance != null || manager == null)
            return;

        GameObject host = new GameObject("UpgradeScreen");
        instance = host.AddComponent<UpgradeUIManager>();
        instance.CreateDocument();
        if (queuedOffers != null)
        {
            instance.SetOffers(queuedOffers);
            queuedOffers = null;
        }
    }

    public static void ReceiveOffers(byte[] offers)
    {
        if (offers == null || offers.Length != 3)
            return;

        if (instance == null)
        {
            queuedOffers = offers;
            return;
        }

        instance.SetOffers(offers);
    }

    public static void ClearOffers()
    {
        queuedOffers = null;
        instance?.ClearLocalOffers();
    }

    private void CreateDocument()
    {
        document = gameObject.AddComponent<UIDocument>();
        UIDocument gameUi = FindGameUiDocument();
        if (gameUi != null)
            document.panelSettings = gameUi.panelSettings;

        document.visualTreeAsset = Resources.Load<VisualTreeAsset>("UpgradeScreen");
        if (document.visualTreeAsset == null)
        {
            Debug.LogError("[UpgradeUI] Resources/UpgradeScreen.uxml bulunamadı.");
            return;
        }

        root = document.rootVisualElement;
        overlay = root.Q<VisualElement>("upgrade-root");
        eyebrow = root.Q<Label>("upgrade-eyebrow");
        title = root.Q<Label>("upgrade-title");
        detail = root.Q<Label>("upgrade-detail");
        for (int i = 0; i < localOffers.Length; i++)
        {
            int choice = i;
            cardSlots[i] = root.Q<VisualElement>($"upgrade-card-{i}");
            cardIndexes[i] = root.Q<Label>($"upgrade-card-index-{i}");
            cardTitles[i] = root.Q<Label>($"upgrade-card-title-{i}");
            cardDescriptions[i] = root.Q<Label>($"upgrade-card-description-{i}");
            cardButtons[i] = root.Q<Button>($"upgrade-card-select-{i}");

            cardButtons[i]?.RegisterCallback<ClickEvent>(_ => Choose((byte)choice));
            cardSlots[i]?.RegisterCallback<ClickEvent>(_ =>
            {
                if (cardSlots[choice].ClassListContains("upgrade-card-empty-selectable"))
                    Choose((byte)choice);
            });
        }
        overlay?.AddToClassList("hidden");
        if (root != null)
            root.style.display = DisplayStyle.None;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    public static void ForceClose()
    {
        instance?.CloseLocal();
    }

    private void Update()
    {
        if (UpgradeManager.Instance == null || NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsListening)
        {
            CloseLocal();
            return;
        }

        MatchFlowManager flow = MatchFlowManager.Instance;
        if (flow == null || !IsSelectionPhase(flow.CurrentPhase.Value))
        {
            CloseLocal();
            return;
        }

        if (localPlayer == null)
            localPlayer = FindLocalPlayer();

        PlayerUpgradeState? state = UpgradeManager.Instance.GetState(NetworkManager.Singleton.LocalClientId);
        if (localPlayer != null && localPlayer.isDead.Value)
        {
            if (isOpen)
            {
                UpgradeManager.Instance.CancelPendingSelectionRpc();
                CloseLocal();
            }
            return;
        }

        if (!state.HasValue || state.Value.PendingSelection == UpgradeSelectionKind.None)
        {
            if (isOpen)
                CloseLocal();
            else
                ClearLocalOffers();
            return;
        }

        if (UpgradeManager.Instance.TryGetUpgradeOffers(NetworkManager.Singleton.LocalClientId, out byte[] syncedOffers) &&
            (!offersReady || !OffersMatch(syncedOffers)))
        {
            SetOffers(syncedOffers);
        }

        if (offersReady && !isOpen)
            Open(state.Value);
    }

    private void SetOffers(byte[] offers)
    {
        for (int i = 0; i < localOffers.Length; i++)
            localOffers[i] = (UpgradeCardId)offers[i];

        offersReady = true;
        isOpen = false;
    }

    private bool OffersMatch(byte[] offers)
    {
        return offers.Length == localOffers.Length &&
               (UpgradeCardId)offers[0] == localOffers[0] &&
               (UpgradeCardId)offers[1] == localOffers[1] &&
               (UpgradeCardId)offers[2] == localOffers[2];
    }

    private void Open(PlayerUpgradeState state)
    {
        if (root == null || overlay == null || cardSlots[0] == null ||
            cardSlots[1] == null || cardSlots[2] == null)
            return;

        if (localPlayer == null)
            localPlayer = FindLocalPlayer();

        isOpen = true;
        document.enabled = true;
        root.style.display = DisplayStyle.Flex;
        overlay.RemoveFromClassList("hidden");
        overlay.AddToClassList("open");
        overlay.RemoveFromClassList("villager");
        overlay.RemoveFromClassList("killer");

        bool killer = IsLocalKiller();
        overlay.AddToClassList(killer ? "killer" : "villager");
        if (localPlayer != null)
        {
            localPlayer.playerCanMove = false;
            localPlayer.cameraCanMove = false;
        }

        bool allEmpty = localOffers[0] == UpgradeCardId.None &&
                        localOffers[1] == UpgradeCardId.None &&
                        localOffers[2] == UpgradeCardId.None;

        eyebrow.text = allEmpty ? string.Empty : (killer ? "ROGUE LOADOUT" : "CREW LOADOUT");
        title.text = allEmpty ? string.Empty : "SELECT ONE PROTOCOL";
        detail.text = allEmpty ? string.Empty : "THREE DISTINCT OPTIONS // EFFECTS STACK UP TO TWO COPIES.";

        for (byte i = 0; i < localOffers.Length; i++)
            BindCard(i, localOffers[i], allEmpty);
    }

    private void BindCard(byte choice, UpgradeCardId cardId, bool allEmpty)
    {
        VisualElement card = cardSlots[choice];
        Label index = cardIndexes[choice];
        Label name = cardTitles[choice];
        Label description = cardDescriptions[choice];
        Button select = cardButtons[choice];
        if (card == null || index == null || name == null || description == null || select == null)
            return;

        bool blank = cardId == UpgradeCardId.None;
        card.RemoveFromClassList("upgrade-card-empty");
        card.RemoveFromClassList("upgrade-card-empty-selectable");
        card.RemoveFromClassList("upgrade-card-real");

        if (blank)
        {
            card.AddToClassList("upgrade-card-empty");
            if (allEmpty)
                card.AddToClassList("upgrade-card-empty-selectable");

            index.text = string.Empty;
            name.text = string.Empty;
            description.text = string.Empty;
            select.text = string.Empty;
            select.style.display = DisplayStyle.None;
            return;
        }

        card.AddToClassList("upgrade-card-real");
        index.text = $"0{choice + 1}";
        name.text = GetCardName(cardId);
        description.text = GetCardDescription(cardId);
        select.text = "INSTALL";
        select.style.display = DisplayStyle.Flex;
    }

    private void Choose(byte choice)
    {
        UpgradeManager.Instance?.ChooseUpgradeRpc(choice);
    }

    private void CloseLocal()
    {
        if (overlay != null)
        {
            overlay.RemoveFromClassList("open");
            overlay.AddToClassList("hidden");
        }
        if (root != null)
            root.style.display = DisplayStyle.None;

        isOpen = false;
        offersReady = false;
        for (int i = 0; i < localOffers.Length; i++)
            localOffers[i] = UpgradeCardId.None;

        if (localPlayer != null && !localPlayer.isDead.Value)
        {
            localPlayer.playerCanMove = true;
            localPlayer.cameraCanMove = true;
        }
    }

    private void ClearLocalOffers()
    {
        CloseLocal();
    }

    private static bool IsSelectionPhase(MatchPhase phase)
    {
        return phase == MatchPhase.BootProtection || phase == MatchPhase.Active;
    }

    private bool IsLocalKiller()
    {
        return RoleManager.Instance != null && NetworkManager.Singleton != null &&
               RoleManager.Instance.GetPlayerRole(NetworkManager.Singleton.LocalClientId) == PlayerRole.Impostor;
    }

    private string GetCardDescription(UpgradeCardId card)
    {
        ulong clientId = NetworkManager.Singleton.LocalClientId;
        int currentCount = UpgradeManager.Instance.GetUpgradeCount(clientId, card);
        int nextCount = currentCount + 1;
        switch (card)
        {
            case UpgradeCardId.OverdriveServos:
                return $"MOVEMENT SPEED +10% // TOTAL AFTER PICK: +{nextCount * 10}%.";
            case UpgradeCardId.ForensicCache:
                return "SHOWS A BROAD DEATH-AGE BAND WHEN REPORTING A BODY.";
            case UpgradeCardId.ThreatSensor:
                return $"NEARBY OFFLINE WARNING RANGE: {12 * nextCount} METERS.";
            case UpgradeCardId.PursuitProtocol:
                return $"KILL COOLDOWN -10 SEC. // NEXT COOLDOWN: {Mathf.Max(DemoBalanceConfig.MinimumKillCooldownSeconds, DemoBalanceConfig.BaseKillCooldownSeconds - (nextCount * DemoBalanceConfig.KillCooldownReductionPerUpgradeSeconds)):0} SEC.";
            case UpgradeCardId.EscapeRoutine:
                return $"AFTER A KILL, MOVE +{nextCount * 15}% FOR 5 SEC.";
            case UpgradeCardId.AmbushProtocol:
                return $"KILL RANGE +{nextCount * 1.75f:0.00} METERS TOTAL.";
            case UpgradeCardId.PriorityUplink:
                return $"AUTOMATIC BLACKOUT BYPASS // CHARGE {nextCount}/2.";
            case UpgradeCardId.IdentityAnchor:
                return $"PRESERVE TRUE COLOR DURING SCRAMBLE // CHARGE {nextCount}/2.";
            case UpgradeCardId.ValveOverride:
                return "STARTS THE THREE-VALVE KILLER EMERGENCY IMMEDIATELY.";
            case UpgradeCardId.SystemBlackout:
                return "LOCKS CREW TERMINALS FOR 15 SECONDS IMMEDIATELY.";
            case UpgradeCardId.IdentityScramble:
                return "FORCES ALL ROBOTS TO SHARE ONE COLOR FOR 30 SECONDS.";
            default:
                return string.Empty;
        }
    }

    private static string GetCardName(UpgradeCardId card)
    {
        return card switch
        {
            UpgradeCardId.OverdriveServos => "OVERDRIVE SERVOS",
            UpgradeCardId.ForensicCache => "FORENSIC CACHE",
            UpgradeCardId.ThreatSensor => "THREAT SENSOR",
            UpgradeCardId.PursuitProtocol => "PURSUIT PROTOCOL",
            UpgradeCardId.EscapeRoutine => "ESCAPE ROUTINE",
            UpgradeCardId.AmbushProtocol => "AMBUSH PROTOCOL",
            UpgradeCardId.PriorityUplink => "PRIORITY UPLINK",
            UpgradeCardId.IdentityAnchor => "IDENTITY ANCHOR",
            UpgradeCardId.ValveOverride => "VALVE OVERRIDE",
            UpgradeCardId.SystemBlackout => "SYSTEM BLACKOUT",
            UpgradeCardId.IdentityScramble => "IDENTITY SCRAMBLE",
            _ => string.Empty
        };
    }

    private static FirstPersonController FindLocalPlayer()
    {
        foreach (FirstPersonController player in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
        {
            if (player.IsOwner)
                return player;
        }
        return null;
    }

    private static UIDocument FindGameUiDocument()
    {
        foreach (UIDocument document in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
        {
            if (document.gameObject.name == "GameUI")
                return document;
        }
        return null;
    }
}
