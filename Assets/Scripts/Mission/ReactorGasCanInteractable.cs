using Unity.Netcode;
using UnityEngine;

public class ReactorGasCanInteractable : MonoBehaviour
{
    [SerializeField] private int gasCanId;
    [SerializeField] private float interactionRange = 3f;

    private Renderer[] renderers;
    private Collider[] colliders;

    private void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider>(true);
    }

    private void Update()
    {
        ReactorMissionManager mission = ReactorMissionManager.Instance;
        bool visible = mission != null &&
                       mission.IsMissionActive.Value &&
                       !mission.IsMissionCompleted.Value &&
                       mission.IsGasCanAvailable(gasCanId);
        bool interactable = visible &&
                             mission.Phase.Value == ReactorMissionPhase.Fueling &&
                             GameplayInteractionGate.IsTaskInteractionPhaseOpen();
        SetVisualState(visible, interactable);

        if (!interactable)
            return;

        FirstPersonController fpc = GetOwnerFpc();
        if (!CanPlayerInteract(fpc))
            return;

        Ray ray = new(fpc.playerCamera.transform.position, fpc.playerCamera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, interactionRange) ||
            (hit.collider.gameObject != gameObject && !hit.collider.transform.IsChildOf(transform)))
        {
            return;
        }

        if (mission.IsClientCarrying(fpc.OwnerClientId))
        {
            fpc.SetInteractionText("[Zaten Yakit Tasiyorsun]");
            return;
        }

        fpc.SetInteractionText("[F] Yakit Bidonunu Al");
        if (Input.GetKeyDown(KeyCode.F))
            mission.PickupGasCanRpc(gasCanId);
    }

    private void SetVisualState(bool visible, bool interactable)
    {
        foreach (Renderer itemRenderer in renderers)
            itemRenderer.enabled = visible;
        foreach (Collider itemCollider in colliders)
            itemCollider.enabled = interactable;
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
        return LocalPlayerResolver.Get();
    }
}
