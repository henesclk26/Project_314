using Unity.Netcode;
using UnityEngine;

public class PressureTerminalInteractable : MonoBehaviour
{
    [SerializeField] private float interactionRange = 3f;

    private void Update()
    {
        PressureMissionUIManager ui = PressureMissionUIManager.Instance;
        if (ui != null && ui.IsOpen)
        {
            SetInRange(false, ui);
            return;
        }

        FirstPersonController fpc = GetOwnerFpc();
        if (fpc == null || fpc.isDead.Value ||
            (GameManager.Instance != null && GameManager.Instance.isGameOver))
        {
            SetInRange(false, ui);
            return;
        }

        if (!GameplayInteractionGate.IsTaskInteractionPhaseOpen())
        {
            SetInRange(false, ui);
            return;
        }

        bool inRange = Vector3.Distance(transform.position, fpc.transform.position) <= interactionRange;
        
        if (inRange)
        {
            if (UpgradeManager.Instance != null && UpgradeManager.Instance.IsSystemBlackoutBlocking(fpc.OwnerClientId))
            {
                SetInRange(false, ui);
                fpc.SetInteractionText("SYSTEM OFFLINE");
                return;
            }
            if (TaskManager.Instance == null) return;

            MissionManager mission = MissionManager.Instance;
            if (mission != null &&
                mission.SharedValveSession.Value == SharedValveSessionState.ValveOverrideActive)
            {
                SetInRange(false, ui);
                fpc.SetInteractionText("SYSTEM BUSY / OFFLINE");
                return;
            }
            
            bool isAvailable = TaskManager.Instance.IsTerminalAvailable("PressureTerminal", fpc.OwnerClientId);
            var activeTask = TaskManager.Instance.GetActiveTaskForPlayer(fpc.OwnerClientId);
            bool hasTask = activeTask.HasValue && activeTask.Value.TaskID.ToString() == "PressureTerminal";

            if (!isAvailable)
            {
                SetInRange(false, ui);
                fpc.SetInteractionText("SYSTEM BUSY / OFFLINE");
            }
            else if (hasTask)
            {
                // Every living participant assigned to the cooperative
                // PressureTerminal task may open the shared computer and use
                // the stabilization hold. The remote valves remain assigned
                // to their existing cooperative role slots.
                SetInRange(true, ui);
                if (Input.GetKeyDown(KeyCode.F) && ui != null)
                {
                    TaskManager.Instance.RequestStartTaskRpc("PressureTerminal");
                    MissionManager.Instance?.ActivatePressureMissionRpc();
                    ui.Open(fpc);
                }
            }
            else
            {
                SetInRange(false, ui);
            }
        }
        else
        {
            SetInRange(false, ui);
        }
    }

    private void SetInRange(bool value, PressureMissionUIManager ui)
    {
        ui?.SetPromptVisible(value);
    }

    private void OnDisable()
    {
        PressureMissionUIManager.Instance?.SetPromptVisible(false);
    }

    private static FirstPersonController GetOwnerFpc()
    {
        FirstPersonController[] players = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
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
