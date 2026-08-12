using Unity.Netcode;
using UnityEngine;

public class ReactorInteractable : MonoBehaviour
{
    [SerializeField] private float interactionRange = 4f;

    private void Update()
    {
        ReactorMissionManager mission = ReactorMissionManager.Instance;
        if (mission == null || !mission.IsMissionActive.Value ||
            mission.IsMissionCompleted.Value ||
            mission.Phase.Value != ReactorMissionPhase.Fueling ||
            !GameplayInteractionGate.IsTaskInteractionPhaseOpen())
        {
            return;
        }

        FirstPersonController fpc = GetOwnerFpc();
        if (!CanPlayerInteract(fpc))
            return;

        Ray ray = new(fpc.playerCamera.transform.position, fpc.playerCamera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, interactionRange) ||
            (hit.collider.gameObject != gameObject && !hit.collider.transform.IsChildOf(transform)))
        {
            return;
        }

        if (!mission.IsClientCarrying(fpc.OwnerClientId))
        {
            fpc.SetInteractionText("[Yakit Bidonu Gerekiyor]");
            return;
        }

        fpc.SetInteractionText("[F] Yakiti Reaktore Aktar");
        if (Input.GetKeyDown(KeyCode.F))
            mission.DepositGasCanRpc();
    }

    private static bool CanPlayerInteract(FirstPersonController fpc)
    {
        return fpc != null &&
               fpc.playerCamera != null &&
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
