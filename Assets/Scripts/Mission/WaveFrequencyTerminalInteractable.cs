using Unity.Netcode;
using UnityEngine;

public class WaveFrequencyTerminalInteractable : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float interactionRange = 3f;

    private void Update()
    {
        WaveFrequencyUIManager ui = WaveFrequencyUIManager.Instance;
        if (ui != null && ui.IsOpen)
            return;

        FirstPersonController fpc = GetOwnerFpc();
        if (fpc == null || fpc.playerCamera == null || fpc.isDead.Value ||
            (GameManager.Instance && GameManager.Instance.isGameOver))
            return;

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

        bool isAvailable = TaskManager.Instance.IsTerminalAvailable("WaveFrequency", fpc.OwnerClientId);
        bool isKiller = RoleManager.Instance != null &&
                        RoleManager.Instance.GetPlayerRole(fpc.OwnerClientId) == PlayerRole.Impostor;
        var activeTask = TaskManager.Instance.GetActiveTaskForPlayer(fpc.OwnerClientId);
        bool hasTask = activeTask.HasValue && activeTask.Value.TaskID.ToString() == "WaveFrequency";
        bool canUseRogueTask = isKiller &&
                               TaskManager.Instance.CanUseRogueTask(fpc.OwnerClientId, "WaveFrequency");
        bool isHackPreparing = isKiller &&
                               TaskManager.Instance.GetTerminalHackPhase("WaveFrequency") == TerminalHackPhase.Preparing;
        bool canUseNormalAlibi = TaskManager.Instance.CanUseAlibiTask(fpc.OwnerClientId, "WaveFrequency");

        if (!isAvailable)
        {
            fpc.SetInteractionText("SYSTEM BUSY / OFFLINE");
            return;
        }

        bool canUseAssignedTask = hasTask || canUseNormalAlibi ||
                                  (canUseRogueTask && !isHackPreparing);
        if (canUseAssignedTask)
        {
            fpc.SetInteractionText("[F] Frekans Terminalini Aç");
            if (canUseRogueTask && !isHackPreparing)
                fpc.SetInteractionText("[F] TERMINALI HACKLE");
            if (Input.GetKeyDown(KeyCode.F) && ui != null)
            {
                if (canUseAssignedTask)
                    TaskManager.Instance.RequestStartTaskRpc("WaveFrequency");
                ui.Open(this, fpc);
            }
        }
        else if (isHackPreparing)
        {
            fpc.SetInteractionText("TERMINAL HACK HAZIRLANIYOR");
        }
        else
        {
            fpc.SetInteractionText(string.Empty);
        }
    }

    public void MarkCompleted()
    {
    }

    private static FirstPersonController GetOwnerFpc()
    {
        FirstPersonController[] allFpcs = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);

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
