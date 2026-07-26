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

        if (MissionManager.Instance != null && MissionManager.Instance.IsPressureMissionCompleted.Value)
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

        bool inRange = Vector3.Distance(transform.position, fpc.transform.position) <= interactionRange;
        SetInRange(inRange, ui);
        if (!inRange)
            return;

        if (Input.GetKeyDown(KeyCode.F) && ui != null)
        {
            MissionManager.Instance?.ActivatePressureMissionRpc();
            ui.Open(fpc);
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
        }

        return players.Length > 0 ? players[0] : null;
    }
}
