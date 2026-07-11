using UnityEngine;

public class BatteryPickupInteractable : MonoBehaviour
{
    [Header("Settings")]
    public float interactionRange = 3f;

    private Renderer[] renderers;
    private Collider[] colliders;

    private void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>();
        colliders = GetComponentsInChildren<Collider>();
    }

    private void Update()
    {
        if (MissionManager.Instance == null) return;

        bool isCollected = MissionManager.Instance.IsBatteryCollected.Value;
        SetVisualsActive(!isCollected);

        if (isCollected) return; // Already collected

        FirstPersonController fpc = GetOwnerFpc();
        if (fpc == null || fpc.isDead.Value || (GameManager.Instance && GameManager.Instance.isGameOver)) return;

        // Only allow pickup if room is unlocked (just in case they clip through the door)
        if (!MissionManager.Instance.IsBatteryRoomUnlocked.Value) return;

        Ray ray = new Ray(fpc.playerCamera.transform.position, fpc.playerCamera.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, interactionRange))
        {
            if (hit.collider.gameObject == gameObject || hit.collider.transform.IsChildOf(transform))
            {
                fpc.SetInteractionText("[F] Bataryayı Al");

                if (Input.GetKeyDown(KeyCode.F))
                {
                    MissionManager.Instance.CollectBatteryServerRpc();
                }
            }
        }
    }

    private void SetVisualsActive(bool active)
    {
        foreach (var r in renderers) r.enabled = active;
        foreach (var c in colliders) c.enabled = active;
    }

    private FirstPersonController GetOwnerFpc()
    {
        var allFpcs = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
        if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening)
        {
            foreach (var f in allFpcs) if (f.IsOwner) return f;
        }
        if (allFpcs.Length > 0) return allFpcs[0];
        return null;
    }
}
