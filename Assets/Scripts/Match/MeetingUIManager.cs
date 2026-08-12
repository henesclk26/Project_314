using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

public class MeetingUIManager : MonoBehaviour
{
    public static MeetingUIManager Instance { get; private set; }

    private UIDocument uiDocument;
    private VisualElement root;
    private VisualElement meetingPanel;
    private Label titleLabel;
    private Label timerLabel;
    private VisualElement playerListContainer;
    private Button abstainButton;
    private VisualElement resultOverlay;
    private Label resultLabel;
    private Label bodyIntelLabel;

    private MeetingState lastState = MeetingState.None;
    private bool hasVoted = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        uiDocument = GetComponent<UIDocument>();
        if (uiDocument != null)
        {
            root = uiDocument.rootVisualElement;
            meetingPanel = root.Q<VisualElement>(className: "meeting-panel");
            titleLabel = root.Q<Label>("TitleLabel");
            timerLabel = root.Q<Label>("TimerLabel");
            playerListContainer = root.Q<VisualElement>("PlayerListContainer");
            abstainButton = root.Q<Button>("AbstainButton");
            resultOverlay = root.Q<VisualElement>("ResultOverlay");
            resultLabel = root.Q<Label>("ResultLabel");
            bodyIntelLabel = root.Q<Label>("BodyIntelLabel");

            abstainButton.clicked += OnAbstainClicked;
            
            // Hide initially
            root.style.display = DisplayStyle.None;
        }
    }

    private void Update()
    {
        if (MeetingManager.Instance == null || root == null) return;

        MeetingState currentState = MeetingManager.Instance.State.Value;

        if (currentState != lastState)
        {
            HandleStateChange(currentState);
            lastState = currentState;
        }

        if (currentState != MeetingState.None)
        {
            UpdateTimer();
        }
    }

    private void HandleStateChange(MeetingState newState)
    {
        if (newState == MeetingState.None)
        {
            root.style.display = DisplayStyle.None;
            if (bodyIntelLabel != null)
                bodyIntelLabel.style.display = DisplayStyle.None;
            // Restore player controls
            SetPlayerControls(true);
            return;
        }

        root.style.display = DisplayStyle.Flex;
        SetPlayerControls(false);

        if (newState == MeetingState.Discussion)
        {
            titleLabel.text = "DISCUSSION";
            resultOverlay.style.display = DisplayStyle.None;
            abstainButton.SetEnabled(false);
            hasVoted = false;
            UpdateForensicIntel();
            RefreshPlayerCards();
        }
        else if (newState == MeetingState.Voting)
        {
            titleLabel.text = "VOTING";
            if (!IsLocalPlayerDead())
            {
                abstainButton.SetEnabled(!hasVoted);
            }
            RefreshPlayerCards(); // Enable vote buttons
        }
        else if (newState == MeetingState.Results)
        {
            titleLabel.text = "RESULTS";
            abstainButton.SetEnabled(false);
            ShowResults();
        }
    }

    private void UpdateTimer()
    {
        double remaining = MeetingManager.Instance.StateEndTime.Value - NetworkManager.Singleton.LocalTime.Time;
        if (remaining < 0) remaining = 0;
        timerLabel.text = Mathf.CeilToInt((float)remaining) + "s";
    }

    private void UpdateForensicIntel()
    {
        if (bodyIntelLabel == null || MeetingManager.Instance == null || NetworkManager.Singleton == null)
            return;

        bool eligible = UpgradeManager.Instance != null &&
            UpgradeManager.Instance.HasPassive(NetworkManager.Singleton.LocalClientId, PassiveUpgradeId.ForensicCache);
        byte band = MeetingManager.Instance.ReportedBodyAgeBand.Value;
        if (!eligible || band == 0)
        {
            bodyIntelLabel.style.display = DisplayStyle.None;
            return;
        }

        bodyIntelLabel.style.display = DisplayStyle.Flex;
        bodyIntelLabel.text = band == 1
            ? "FORENSIC CACHE // DEATH AGE: 0-10 SEC"
            : band == 2 ? "FORENSIC CACHE // DEATH AGE: 10-25 SEC" :
            "FORENSIC CACHE // DEATH AGE: 25+ SEC";
    }

    private void RefreshPlayerCards()
    {
        playerListContainer.Clear();
        MeetingState currentState = MeetingManager.Instance.State.Value;
        bool canVote = currentState == MeetingState.Voting && !hasVoted && !IsLocalPlayerDead();

        foreach (var fpc in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
        {
            VisualElement card = new VisualElement();
            card.AddToClassList("player-card");

            if (fpc.isDead.Value)
            {
                card.AddToClassList("dead");
            }

            VisualElement colorBox = new VisualElement();
            colorBox.AddToClassList("player-color-box");
            // Placeholder color logic
            colorBox.style.backgroundColor = GetPlayerColor(fpc.playerColorIndex.Value);

            VisualElement info = new VisualElement();
            info.AddToClassList("player-info");

            Label nameLabel = new Label(string.IsNullOrEmpty(fpc.playerName.Value.ToString()) ? $"Player {fpc.OwnerClientId}" : fpc.playerName.Value.ToString());
            nameLabel.AddToClassList("player-name");
            info.Add(nameLabel);

            Label statusLabel = new Label(fpc.isDead.Value ? "DEAD" : "ALIVE");
            statusLabel.AddToClassList("player-status");
            info.Add(statusLabel);

            card.Add(colorBox);
            card.Add(info);

            if (!fpc.isDead.Value)
            {
                Button voteBtn = new Button();
                voteBtn.text = "VOTE";
                voteBtn.AddToClassList("vote-button");
                voteBtn.SetEnabled(canVote);
                
                ulong targetId = fpc.OwnerClientId;
                voteBtn.clicked += () => OnVoteClicked(targetId);

                card.Add(voteBtn);
            }

            playerListContainer.Add(card);
        }
    }

    private Color GetPlayerColor(int index)
    {
        // Dummy colors for now
        Color[] colors = { Color.red, Color.blue, Color.green, Color.yellow, Color.cyan, Color.magenta, Color.white, Color.gray };
        return colors[index % colors.Length];
    }

    private void OnVoteClicked(ulong targetId)
    {
        if (hasVoted) return;
        hasVoted = true;
        MeetingManager.Instance.CastVoteServerRpc(targetId);
        RefreshPlayerCards(); // Disable buttons
        abstainButton.SetEnabled(false);
    }

    private void OnAbstainClicked()
    {
        if (hasVoted) return;
        hasVoted = true;
        MeetingManager.Instance.CastVoteServerRpc(ulong.MaxValue); // Abstain
        RefreshPlayerCards(); // Disable buttons
        abstainButton.SetEnabled(false);
    }

    private void ShowResults()
    {
        resultOverlay.style.display = DisplayStyle.Flex;
        if (MeetingManager.Instance.WasTie.Value)
        {
            resultLabel.text = "No one was ejected. (Tie)";
        }
        else
        {
            ulong ejectedId = MeetingManager.Instance.EjectedPlayerId.Value;
            string ejectedName = $"Player {ejectedId}";
            
            // Find name
            foreach (var fpc in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
            {
                if (fpc.OwnerClientId == ejectedId)
                {
                    ejectedName = string.IsNullOrEmpty(fpc.playerName.Value.ToString()) ? $"Player {ejectedId}" : fpc.playerName.Value.ToString();
                    break;
                }
            }

            resultLabel.text = $"{ejectedName} was ejected.";
        }
    }

    private void SetPlayerControls(bool enabled)
    {
        foreach (var fpc in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
        {
            if (fpc.IsOwner)
            {
                fpc.playerCanMove = enabled;
                fpc.cameraCanMove = enabled;

                if (!enabled)
                {
                    UnityEngine.Cursor.lockState = CursorLockMode.None;
                    UnityEngine.Cursor.visible = true;
                }
                else
                {
                    UnityEngine.Cursor.lockState = CursorLockMode.Locked;
                    UnityEngine.Cursor.visible = false;
                }
            }
        }
    }

    private bool IsLocalPlayerDead()
    {
        foreach (var fpc in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
        {
            if (fpc.IsOwner) return fpc.isDead.Value;
        }
        return false;
    }
}
