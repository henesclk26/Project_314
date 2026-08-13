using UnityEngine;
using UnityEngine.UIElements;

public class RoleRevealUIManager : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float revealDurationSeconds = 2f;

    private UIDocument uiDocument;
    private VisualElement overlay;
    private VisualElement frame;
    private VisualElement progressFill;
    private Label roleTitle;
    private Label roleSubtitle;
    private Label timerLabel;
    private Label statusLabel;
    private FirstPersonController lockedPlayer;
    private bool previousPlayerCanMove;
    private bool previousCameraCanMove;
    private CursorLockMode previousCursorLockState;
    private bool previousCursorVisible;
    private bool controlsCaptured;
    private bool revealVisible;
    private float revealEndTime;
    private PlayerRole shownRole = PlayerRole.None;

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null) return;

        VisualElement root = uiDocument.rootVisualElement;
        overlay = root.Q<VisualElement>("role-reveal-overlay");
        frame = root.Q<VisualElement>("role-reveal-frame");
        progressFill = root.Q<VisualElement>("role-reveal-progress-fill");
        roleTitle = root.Q<Label>("role-reveal-title");
        roleSubtitle = root.Q<Label>("role-reveal-subtitle");
        timerLabel = root.Q<Label>("role-reveal-timer");
        statusLabel = root.Q<Label>("role-reveal-status");
    }

    private void Update()
    {
        RoleManager roleManager = RoleManager.Instance;
        if (roleManager == null) return;
        if (!roleManager.AreRolesDistributed())
        {
            if (revealVisible) FinishReveal();
            shownRole = PlayerRole.None;
            return;
        }

        PlayerRole localRole = roleManager.GetLocalPlayerRole();
        if (localRole == PlayerRole.None) return;
        if (!revealVisible && shownRole == PlayerRole.None)
            BeginReveal(localRole);

        if (revealVisible)
        {
            CaptureAndLockPlayer();
            float remaining = Mathf.Max(0f, revealEndTime - Time.unscaledTime);
            float progress = revealDurationSeconds > 0f
                ? Mathf.Clamp01(remaining / revealDurationSeconds)
                : 0f;

            if (progressFill != null)
                progressFill.style.width = Length.Percent(progress * 100f);
            if (timerLabel != null)
                timerLabel.text = $"{remaining:0.0} SEC";
        }

        if (revealVisible && Time.unscaledTime >= revealEndTime)
            FinishReveal();
    }

    private void BeginReveal(PlayerRole role)
    {
        shownRole = role;
        revealVisible = true;
        revealEndTime = Time.unscaledTime + revealDurationSeconds;

        bool impostor = role == PlayerRole.Impostor;
        if (frame != null) frame.EnableInClassList("impostor", impostor);
        if (roleTitle != null) roleTitle.text = impostor ? "KATİL" : "KÖYLÜ";
        if (roleSubtitle != null)
            roleSubtitle.text = impostor
                ? "IMPOSTOR // MÜRETTEBATI ORTADAN KALDIR"
                : "CREW MEMBER // GÖREVLERİNİ TAMAMLA";
        if (timerLabel != null) timerLabel.text = $"{revealDurationSeconds:0.0} SEC";
        if (statusLabel != null) statusLabel.text = "MATCH INITIALIZING...";
        if (progressFill != null) progressFill.style.width = Length.Percent(100f);
        if (overlay != null)
        {
            overlay.RemoveFromClassList("hidden");
            overlay.style.display = DisplayStyle.Flex;
        }
        CaptureAndLockPlayer();
    }

    private void FinishReveal()
    {
        revealVisible = false;
        if (overlay != null)
        {
            overlay.AddToClassList("hidden");
            overlay.style.display = DisplayStyle.None;
        }

        if (lockedPlayer != null && controlsCaptured)
        {
            lockedPlayer.playerCanMove = previousPlayerCanMove;
            lockedPlayer.cameraCanMove = previousCameraCanMove;
        }

        if (controlsCaptured)
        {
            UnityEngine.Cursor.lockState = previousCursorLockState;
            UnityEngine.Cursor.visible = previousCursorVisible;
        }

        lockedPlayer = null;
        controlsCaptured = false;
    }

    private void CaptureAndLockPlayer()
    {
        FirstPersonController player = FindLocalPlayer();
        if (player == null) return;

        if (!controlsCaptured)
        {
            lockedPlayer = player;
            previousPlayerCanMove = player.playerCanMove;
            previousCameraCanMove = player.cameraCanMove;
            previousCursorLockState = UnityEngine.Cursor.lockState;
            previousCursorVisible = UnityEngine.Cursor.visible;
            controlsCaptured = true;
        }

        lockedPlayer.playerCanMove = false;
        lockedPlayer.cameraCanMove = false;
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
    }

    private FirstPersonController FindLocalPlayer()
    {
        FirstPersonController[] players = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
        foreach (FirstPersonController player in players)
        {
            if (player != null && player.IsOwner)
                return player;
        }

        return null;
    }
}
