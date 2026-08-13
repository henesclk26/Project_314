using Unity.Netcode;
using UnityEngine;

public class CircuitMissionInteractable : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float interactionRange = 3f;

    private void Update()
    {
        CircuitMissionUIManager ui = CircuitMissionUIManager.Instance;
        if (ui != null && ui.IsOpen)
            return;

        FirstPersonController fpc = GetOwnerFpc();
        if (fpc == null || fpc.playerCamera == null || fpc.isDead.Value ||
            (GameManager.Instance && GameManager.Instance.isGameOver))
        {
            return;
        }

        if (!GameplayInteractionGate.IsTaskInteractionPhaseOpen())
            return;

        Ray ray = new Ray(fpc.playerCamera.transform.position, fpc.playerCamera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, interactionRange))
            return;

        if (hit.collider.gameObject != gameObject && !hit.collider.transform.IsChildOf(transform))
            return;

        if (UpgradeManager.Instance != null && UpgradeManager.Instance.IsSystemBlackoutBlocking(fpc.OwnerClientId))
        {
            fpc.SetInteractionText("SYSTEM OFFLINE");
            return;
        }

        if (TaskManager.Instance == null) return;

        bool isAvailable = TaskManager.Instance.IsTerminalAvailable("CircuitMission", fpc.OwnerClientId);
        var activeTask = TaskManager.Instance.GetActiveTaskForPlayer(fpc.OwnerClientId);
        bool hasTask = activeTask.HasValue && activeTask.Value.TaskID.ToString() == "CircuitMission";
        bool canUseRogueTask = TaskManager.Instance.CanUseRogueTask(fpc.OwnerClientId, "CircuitMission");
        bool isHackPreparing = TaskManager.Instance.GetTerminalHackPhase("CircuitMission") == TerminalHackPhase.Preparing;
        bool canUseNormalAlibi = TaskManager.Instance.CanUseAlibiTask(fpc.OwnerClientId, "CircuitMission");

        if (!isAvailable)
        {
            fpc.SetInteractionText("SYSTEM BUSY / OFFLINE");
            return;
        }

        if (!isHackPreparing && (canUseRogueTask || hasTask || canUseNormalAlibi))
        {
            fpc.SetInteractionText(canUseRogueTask ? "TERMINALI HACKLE" : "[F] Devre Panelini Ac");
            if (Input.GetKeyDown(KeyCode.F) && ui != null)
            {
                if (canUseRogueTask || hasTask || canUseNormalAlibi)
                    TaskManager.Instance.RequestStartTaskRpc("CircuitMission");
                ui.Open(this, fpc);
            }
        }
        else if (isHackPreparing)
        {
            fpc.SetInteractionText("TERMINAL HACK HAZIRLANIYOR");
        }
    }

    public void MarkCompleted()
    {
    }

    private static FirstPersonController GetOwnerFpc()
    {
        FirstPersonController[] allFpcs =
            FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            foreach (FirstPersonController fpc in allFpcs)
            {
                if (fpc.IsOwner)
                    return fpc;
            }

            return null;
        }

        return allFpcs.Length > 0 ? allFpcs[0] : null;
    }
}
