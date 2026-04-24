using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    public NetworkVariable<int> totalMissions = new NetworkVariable<int>(0);
    public NetworkVariable<int> completedMissions = new NetworkVariable<int>(0);

    private Dictionary<ulong, int> playerSwitchMissions = new Dictionary<ulong, int>();
    private Dictionary<ulong, int> playerCollectorMissions = new Dictionary<ulong, int>();
    private Dictionary<ulong, bool> playerAliveState = new Dictionary<ulong, bool>();
    private Dictionary<ulong, PlayerRole> playerRoles = new Dictionary<ulong, PlayerRole>();

    public Image progressFill;
    private TextMeshProUGUI personalMissionsText;

    [Header("UI Panels")]
    public GameObject katilWinPanel;
    public GameObject koyluWinPanel;

    public bool isGameOver = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        totalMissions.OnValueChanged += OnProgressChanged;
        completedMissions.OnValueChanged += OnProgressChanged;
        
        if (IsClient)
        {
            EnsureUI();
        }
    }

    private void Start()
    {
        if (progressFill != null)
        {
            progressFill.fillAmount = 0f;
        }
    }

    private void OnProgressChanged(int prev, int next)
    {
        UpdateProgressBarUI();
    }

    private void UpdateProgressBarUI()
    {
        if (progressFill != null && totalMissions.Value > 0)
        {
            progressFill.fillAmount = (float)completedMissions.Value / totalMissions.Value;
        }
    }

    public void StartGame(List<ulong> crewmateIds, List<ulong> impostorIds)
    {
        if (!IsServer) return;

        playerSwitchMissions.Clear();
        playerCollectorMissions.Clear();
        playerAliveState.Clear();
        playerRoles.Clear();

        int totalMissionsToComplete = 0;

        foreach (var id in crewmateIds)
        {
            playerSwitchMissions[id] = 2;
            playerCollectorMissions[id] = 2;
            playerAliveState[id] = true;
            playerRoles[id] = PlayerRole.Crewmate;
            totalMissionsToComplete += 4;
            
            UpdatePersonalUIClientRpc(id, 2, 2, CreateTargetRpcParams(id));
        }

        foreach (var id in impostorIds)
        {
            playerAliveState[id] = true;
            playerRoles[id] = PlayerRole.Impostor;
            UpdatePersonalUIClientRpc(id, 0, 0, CreateTargetRpcParams(id));
        }

        totalMissions.Value = totalMissionsToComplete;
        completedMissions.Value = 0;
    }

    [ServerRpc(RequireOwnership = false)]
    public void CompleteMissionServerRpc(ulong clientId, string missionType)
    {
        if (!IsServer) return;

        if (playerRoles.TryGetValue(clientId, out var role) && role == PlayerRole.Crewmate && playerAliveState.TryGetValue(clientId, out var isAlive) && isAlive)
        {
            if (missionType == "Switch" && playerSwitchMissions[clientId] > 0)
            {
                playerSwitchMissions[clientId]--;
                completedMissions.Value++;
                UpdatePersonalUIClientRpc(clientId, playerSwitchMissions[clientId], playerCollectorMissions[clientId], CreateTargetRpcParams(clientId));
            }
            else if (missionType == "Collector" && playerCollectorMissions[clientId] > 0)
            {
                playerCollectorMissions[clientId]--;
                completedMissions.Value++;
                UpdatePersonalUIClientRpc(clientId, playerSwitchMissions[clientId], playerCollectorMissions[clientId], CreateTargetRpcParams(clientId));
            }

            CheckWinConditions();
        }
    }

    public void OnPlayerDied(ulong clientId)
    {
        if (!IsServer) return;

        if (playerAliveState.ContainsKey(clientId) && playerAliveState[clientId])
        {
            playerAliveState[clientId] = false;

            if (playerRoles[clientId] == PlayerRole.Crewmate)
            {
                int remainingSwitch = playerSwitchMissions[clientId];
                int remainingCollector = playerCollectorMissions[clientId];
                int totalRemaining = remainingSwitch + remainingCollector;

                if (totalRemaining > 0)
                {
                    totalMissions.Value -= totalRemaining;
                }
            }
            
            CheckWinConditions();
        }
    }

    private void CheckWinConditions()
    {
        if (!IsServer) return;

        int aliveCrewmates = 0;
        int aliveImpostors = 0;

        foreach (var kvp in playerAliveState)
        {
            if (kvp.Value)
            {
                if (playerRoles[kvp.Key] == PlayerRole.Crewmate) aliveCrewmates++;
                else if (playerRoles[kvp.Key] == PlayerRole.Impostor) aliveImpostors++;
            }
        }

        if (aliveCrewmates <= aliveImpostors && aliveImpostors > 0)
        {
            EndGameClientRpc(false); 
            return;
        }

        if (aliveImpostors == 0 && aliveCrewmates > 0)
        {
            EndGameClientRpc(true); 
            return;
        }

        if (totalMissions.Value > 0 && completedMissions.Value >= totalMissions.Value)
        {
            EndGameClientRpc(true); 
        }
    }

    [ClientRpc]
    private void EndGameClientRpc(bool villagersWin)
    {
        isGameOver = true;

        SwitchToggleMission.SwitchMinigameUI minigameUI = FindFirstObjectByType<SwitchToggleMission.SwitchMinigameUI>();
        if (minigameUI != null) minigameUI.CloseMinigame();

        if (personalMissionsText != null) personalMissionsText.gameObject.SetActive(false);
        if (progressFill != null && progressFill.transform.parent != null) 
            progressFill.transform.parent.gameObject.SetActive(false);
        
        if (villagersWin)
        {
            if (koyluWinPanel != null) koyluWinPanel.SetActive(true);
        }
        else
        {
            if (katilWinPanel != null) katilWinPanel.SetActive(true);
        }
        
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        FirstPersonController[] fpcs = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
        foreach (var fpc in fpcs)
        {
            fpc.playerCanMove = false;
            fpc.cameraCanMove = false;
        }
    }

    public void ReturnToMainMenu()
    {
        StartCoroutine(ReturnToMainMenuCoroutine());
    }

    private System.Collections.IEnumerator ReturnToMainMenuCoroutine()
    {
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (NetworkManager.Singleton != null)
        {
            GameObject networkManagerObject = NetworkManager.Singleton.gameObject;
            NetworkManager.Singleton.Shutdown();
            
            yield return new WaitForSecondsRealtime(0.1f);
            
            if (networkManagerObject != null)
                Destroy(networkManagerObject);
        }

        if (MultiplayerManager.Instance != null)
        {
            try { _ = MultiplayerManager.Instance.LeaveLobby(); } catch { }
        }

        yield return new WaitForSecondsRealtime(0.1f);
        SceneManager.LoadScene("MainMenu");
    }

    [ClientRpc]
    private void UpdatePersonalUIClientRpc(ulong clientId, int switchRem, int colRem, ClientRpcParams rpcParams = default)
    {
        EnsureUI();
        if (personalMissionsText != null)
        {
            if (switchRem == 0 && colRem == 0)
            {
                personalMissionsText.text = "Gorevler Tamamlandi!";
                personalMissionsText.color = Color.green;
            }
            else
            {
                personalMissionsText.text = $"Kalan Gorevler:\n- Switch Toggle Mission x{switchRem}\n- Collector Mission x{colRem}";
                personalMissionsText.color = Color.white;
            }
        }
    }

    private void EnsureUI()
    {
        if (personalMissionsText == null)
        {
            Canvas canvas = GameObject.Find("Canvas") != null ? GameObject.Find("Canvas").GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
            if (canvas != null)
            {
                GameObject txtObj = new GameObject("PersonalMissionsText", typeof(RectTransform), typeof(TextMeshProUGUI));
                txtObj.transform.SetParent(canvas.transform, false);
                RectTransform rect = txtObj.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0, 1f);
                rect.anchorMax = new Vector2(0, 1f);
                rect.pivot = new Vector2(0, 1f);
                rect.anchoredPosition = new Vector2(20f, -20f);
                rect.sizeDelta = new Vector2(400f, 150f);

                personalMissionsText = txtObj.GetComponent<TextMeshProUGUI>();
                personalMissionsText.fontSize = 24f;
                personalMissionsText.alignment = TextAlignmentOptions.Left;
                personalMissionsText.text = "";
                
                personalMissionsText.font = TMP_Settings.defaultFontAsset != null ? TMP_Settings.defaultFontAsset : Resources.Load<TMP_FontAsset>("LiberationSans SDF");
            }
        }
    }


    private ClientRpcParams CreateTargetRpcParams(ulong clientId)
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { clientId }
            }
        };
    }
}
