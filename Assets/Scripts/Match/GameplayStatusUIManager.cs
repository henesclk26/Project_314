using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

public class GameplayStatusUIManager : MonoBehaviour
{
    private static GameplayStatusUIManager instance;
    private UIDocument document;
    private VisualElement root;
    private VisualElement alert;
    private Label alertTitle;
    private Label alertDetail;
    private Label passive;
    private Label tool;
    private string localAlertTitle;
    private string localAlertDetail;
    private float localAlertEndTime;

    public static void ShowLocalAlert(string title, string detail, float durationSeconds)
    {
        CreateIfNeeded();
        if (instance == null)
            return;

        instance.localAlertTitle = title;
        instance.localAlertDetail = detail;
        instance.localAlertEndTime = Time.unscaledTime + Mathf.Max(0f, durationSeconds);
    }

    public static void CreateIfNeeded()
    {
        if (instance != null)
            return;

        GameObject host = new GameObject("GameplayStatusScreen");
        instance = host.AddComponent<GameplayStatusUIManager>();
        instance.CreateDocument();
    }

    private void CreateDocument()
    {
        document = gameObject.AddComponent<UIDocument>();
        foreach (UIDocument candidate in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
        {
            if (candidate.gameObject.name == "GameUI")
            {
                document.panelSettings = candidate.panelSettings;
                break;
            }
        }

        document.visualTreeAsset = Resources.Load<VisualTreeAsset>("GameplayStatusScreen");
        root = document.rootVisualElement;
        SetPickingModeRecursive(root, PickingMode.Ignore);
        StyleSheet styleSheet = Resources.Load<StyleSheet>("GameplayStatusScreen");
        if (styleSheet != null)
            root.styleSheets.Add(styleSheet);
        alert = root.Q<VisualElement>("gameplay-alert");
        alertTitle = root.Q<Label>("gameplay-alert-title");
        alertDetail = root.Q<Label>("gameplay-alert-detail");
        passive = root.Q<Label>("gameplay-passive");
        tool = root.Q<Label>("gameplay-tool");
    }

    private static void SetPickingModeRecursive(VisualElement element, PickingMode pickingMode)
    {
        if (element == null)
            return;

        element.pickingMode = pickingMode;
        element.Query<VisualElement>().ForEach(child => child.pickingMode = pickingMode);
    }

    private void Update()
    {
        if (root == null || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            return;

        UpdateLoadout();
        UpdateAlert();
    }

    private void UpdateLoadout()
    {
        PlayerUpgradeState? state = UpgradeManager.Instance?.GetState(NetworkManager.Singleton.LocalClientId);
        passive.text = !state.HasValue || state.Value.Passive == PassiveUpgradeId.None
            ? string.Empty : $"PASSIVE // {state.Value.Passive}";
        tool.text = !state.HasValue || state.Value.Tool == ActiveToolId.None
            ? string.Empty : $"TOOL // {state.Value.Tool} // {(state.Value.ToolConsumed ? "EXPENDED" : "ARMED")}";
    }

    private void UpdateAlert()
    {
        string title = null;
        string detail = null;
        double now = NetworkManager.Singleton.ServerTime.Time;

        if (Time.unscaledTime < localAlertEndTime)
        {
            title = localAlertTitle;
            detail = localAlertDetail;
        }

        if (title == null && MissionManager.Instance != null && MissionManager.Instance.IsValveMissionActive.Value)
        {
            title = "VALVE EMERGENCY";
            detail = $"COORDINATE THREE VALVES // {Mathf.CeilToInt(MissionManager.Instance.ValveOverrideRemainingSeconds.Value):00} SEC";
        }
        else if (title == null && UpgradeManager.Instance != null && UpgradeManager.Instance.SystemBlackoutActive.Value)
        {
            title = "SYSTEM OFFLINE";
            detail = $"TERMINAL LOCK // {Mathf.CeilToInt((float)(UpgradeManager.Instance.SystemBlackoutEndTime.Value - now)):00} SEC";
        }
        else if (title == null && UpgradeManager.Instance != null && UpgradeManager.Instance.IdentityScrambleActive.Value)
        {
            title = "IDENTITY SIGNAL DESYNC";
            detail = $"VISUAL IDENTITY COMPROMISED // {Mathf.CeilToInt((float)(UpgradeManager.Instance.IdentityScrambleEndTime.Value - now)):00} SEC";
        }
        else if (title == null && MatchFlowManager.Instance != null && MatchFlowManager.Instance.CurrentPhase.Value == MatchPhase.Ended)
        {
            title = MatchFlowManager.Instance.Winner.Value == MatchWinner.Villagers ? "CREW VICTORY" : "ROGUE VICTORY";
            detail = "MATCH COMPLETE";
        }

        bool visible = !string.IsNullOrEmpty(title);
        alert.EnableInClassList("is-hidden", !visible);
        if (visible)
        {
            alertTitle.text = title;
            alertDetail.text = detail;
        }
    }
}
