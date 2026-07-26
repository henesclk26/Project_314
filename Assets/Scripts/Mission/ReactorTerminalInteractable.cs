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

        if (mission != null && mission.IsMissionCompleted.Value)
        {
            ui?.SetPromptVisible(false);
            return;
        }

        FirstPersonController fpc = GetOwnerFpc();
        if (!CanPlayerInteract(fpc))
        {
            ui?.SetPromptVisible(false);
            return;
        }

        bool inRange = Vector3.Distance(transform.position, fpc.transform.position) <= interactionRange;
        ui?.SetPromptVisible(inRange);
        if (!inRange || !Input.GetKeyDown(KeyCode.F) || ui == null)
            return;

        mission?.ActivateMissionRpc();
        ui.Open(fpc);
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
