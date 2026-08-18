using Unity.Netcode;
using UnityEngine;

public class ReactorTerminalInteractable : MonoBehaviour
{
    [SerializeField] private float interactionRange = 3f;

    private void Update()
    {
        ReactorMissionUIManager ui = ReactorMissionUIManager.Instance;
        ReactorMissionManager mission = ReactorMissionManager.Instance;
        if (ui != null && ui.IsOpen)
        {
            ui.SetPromptVisible(false);
            return;
        }

        FirstPersonController fpc = GetOwnerFpc();
        if (!CanPlayerInteract(fpc))
        {
            ui?.SetPromptVisible(false);
            return;
        }

        if (!GameplayInteractionGate.IsTaskInteractionPhaseOpen())
        {
            ui?.SetPromptVisible(false);
            return;
        }

        bool inRange = Vector3.Distance(transform.position, fpc.transform.position) <= interactionRange;
        
        if (inRange)
        {
            if (UpgradeManager.Instance != null && UpgradeManager.Instance.IsSystemBlackoutBlocking(fpc.OwnerClientId))
            {
                ui?.SetPromptVisible(false);
                fpc.SetInteractionText("SYSTEM OFFLINE");
                return;
            }

            if (TaskManager.Instance == null) return;
            
            bool isAvailable = TaskManager.Instance.IsTerminalAvailable("ReactorTerminal", fpc.OwnerClientId);
            var activeTask = TaskManager.Instance.GetActiveTaskForPlayer(fpc.OwnerClientId);
            bool hasTask = activeTask.HasValue && activeTask.Value.TaskID.ToString() == "ReactorTerminal";
            bool canUseQuickTestTask =
                TaskManager.Instance.CanUseAlibiTask(fpc.OwnerClientId, "ReactorTerminal");

            if (!isAvailable)
            {
                ui?.SetPromptVisible(false);
                fpc.SetInteractionText("SYSTEM BUSY / OFFLINE");
            }
            else if (hasTask || canUseQuickTestTask)
            {
                ui?.SetPromptVisible(true);
                if (Input.GetKeyDown(KeyCode.F) && ui != null)
                {
                    TaskManager.Instance.RequestStartTaskRpc("ReactorTerminal");
                    mission?.ActivateMissionRpc();
                    ui.Open(fpc);
                }
            }
            else
            {
                ui?.SetPromptVisible(false);
            }
        }
        else
        {
            ui?.SetPromptVisible(false);
        }
    }

    private void OnDisable()
    {
        ReactorMissionUIManager.Instance?.SetPromptVisible(false);
    }

    private static bool CanPlayerInteract(FirstPersonController fpc)
    {
        return fpc != null &&
               !fpc.isDead.Value &&
               (GameManager.Instance == null || !GameManager.Instance.isGameOver);
    }

    private static FirstPersonController GetOwnerFpc()
    {
        FirstPersonController[] players =
            FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            foreach (FirstPersonController player in players)
            {
                if (player.IsOwner)
                    return player;
            }

            return null;
        }

        return players.Length > 0 ? players[0] : null;
    }
}
