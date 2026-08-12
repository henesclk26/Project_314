using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

public class UpgradeUIManager : MonoBehaviour
{
    private static UpgradeUIManager instance;
    private UIDocument document;
    private VisualElement root;
    private VisualElement overlay;
    private Label eyebrow;
    private Label title;
    private Label detail;
    private VisualElement cards;
    private FirstPersonController localPlayer;
    private bool isOpen;

    public static void CreateIfNeeded(UpgradeManager manager)
    {
        if (instance != null || manager == null)
            return;

        GameObject host = new GameObject("UpgradeScreen");
        instance = host.AddComponent<UpgradeUIManager>();
        instance.CreateDocument();
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
        cards = root.Q<VisualElement>("upgrade-cards");
        root.Q<Button>("upgrade-close")?.RegisterCallback<ClickEvent>(_ => CloseWithoutReward());
        overlay?.AddToClassList("hidden");
        document.enabled = false;
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
                CloseWithoutReward();
            return;
        }

        if (!state.HasValue || state.Value.PendingSelection == UpgradeSelectionKind.None)
        {
            if (isOpen)
                CloseWithoutReward();
            return;
        }

        if (!isOpen)
            Open(state.Value);
    }

    private void Open(PlayerUpgradeState state)
    {
        if (root == null || overlay == null)
            return;

        isOpen = true;
        document.enabled = true;
        overlay.RemoveFromClassList("hidden");
        overlay.AddToClassList("open");
        if (localPlayer != null)
        {
            localPlayer.playerCanMove = false;
            localPlayer.cameraCanMove = false;
        }

        bool passive = state.PendingSelection == UpgradeSelectionKind.Passive;
        eyebrow.text = passive ? "SYSTEM EVOLUTION / PASSIVE" : "SYSTEM EVOLUTION / TOOL LOADOUT";
        title.text = passive ? "SELECT PASSIVE PROTOCOL" : "SELECT ACTIVE TOOL";
        detail.text = passive ? "ONE PASSIVE WILL REMAIN ACTIVE FOR THIS UNIT." : "SELECTED TOOL ARMS IMMEDIATELY.";
        cards.Clear();

        if (passive)
        {
            string[] names = IsLocalKiller()
                ? new[] { "PURSUIT PROTOCOL", "ESCAPE ROUTINE", "AMBUSH PROTOCOL" }
                : new[] { "OVERDRIVE SERVOS", "FORENSIC CACHE", "THREAT SENSOR" };
            string[] descriptions = IsLocalKiller()
                ? new[] { "REDUCE KILL COOLDOWN TO 25 SEC.", "MOVE 15% FASTER FOR 5 SEC. AFTER A KILL.", "EXTEND KILL RANGE TO 4.75 M." }
                : new[] { "INCREASE MOVEMENT SPEED BY 10%.", "SHOW A BROAD DEATH-AGE BAND WHEN REPORTING.", "WARN OF A NEARBY UNIT OFFLINE EVENT." };
            for (byte i = 0; i < names.Length; i++)
                AddCard(i, names[i], descriptions[i]);
        }
        else
        {
            List<ActiveToolId> tools = UpgradeManager.Instance.GetAvailableToolChoices(NetworkManager.Singleton.LocalClientId);
            for (byte i = 0; i < tools.Count; i++)
                AddCard(i, GetToolName(tools[i]), GetToolDescription(tools[i]));
        }
    }

    private void AddCard(byte choice, string cardTitle, string cardDescription)
    {
        VisualElement card = new VisualElement();
        card.AddToClassList("upgrade-card");
        Label index = new Label($"0{choice + 1}");
        index.AddToClassList("upgrade-card-index");
        Label name = new Label(cardTitle);
        name.AddToClassList("upgrade-card-title");
        Label description = new Label(cardDescription);
        description.AddToClassList("upgrade-card-description");
        Button select = new Button(() => UpgradeManager.Instance.ChooseUpgradeRpc(choice)) { text = "INSTALL" };
        select.AddToClassList("upgrade-card-button");
        card.Add(index);
        card.Add(name);
        card.Add(description);
        card.Add(select);
        cards.Add(card);
    }

    private void CloseWithoutReward()
    {
        if (UpgradeManager.Instance != null &&
            MatchFlowManager.Instance != null &&
            IsSelectionPhase(MatchFlowManager.Instance.CurrentPhase.Value))
        {
            UpgradeManager.Instance.CancelPendingSelectionRpc();
        }

        CloseLocal();
    }

    private void CloseLocal()
    {
        if (overlay != null)
        {
            overlay.RemoveFromClassList("open");
            overlay.AddToClassList("hidden");
        }

        isOpen = false;
        if (document != null)
            document.enabled = false;

        if (localPlayer != null && !localPlayer.isDead.Value)
        {
            localPlayer.playerCanMove = true;
            localPlayer.cameraCanMove = true;
        }
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

    private static string GetToolName(ActiveToolId tool)
    {
        return tool switch
        {
            ActiveToolId.PriorityUplink => "PRIORITY UPLINK",
            ActiveToolId.IdentityAnchor => "IDENTITY ANCHOR",
            ActiveToolId.ValveOverride => "VALVE OVERRIDE",
            ActiveToolId.SystemBlackout => "SYSTEM BLACKOUT",
            ActiveToolId.IdentityScramble => "IDENTITY SCRAMBLE",
            _ => "UNKNOWN TOOL"
        };
    }

    private static string GetToolDescription(ActiveToolId tool)
    {
        return tool switch
        {
            ActiveToolId.PriorityUplink => "AUTOMATICALLY BYPASSES THE NEXT SYSTEM BLACKOUT.",
            ActiveToolId.IdentityAnchor => "PRESERVES THIS UNIT'S ORIGINAL COLOR DURING SCRAMBLE.",
            ActiveToolId.ValveOverride => "STARTS THE THREE-VALVE KILLER EMERGENCY.",
            ActiveToolId.SystemBlackout => "LOCKS CREW TERMINALS FOR 15 SECONDS.",
            ActiveToolId.IdentityScramble => "FORCES ALL ROBOTS TO SHARE ONE COLOR FOR 30 SECONDS.",
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
