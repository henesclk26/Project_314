using UnityEngine;

public class PuzzleTerminalInteractable : MonoBehaviour
{
    [Header("Settings")]
    public float interactionRange = 3f;

    private void Update()
    {
        FirstPersonController fpc = GetOwnerFpc();
        if (fpc == null || fpc.isDead.Value || (GameManager.Instance && GameManager.Instance.isGameOver)) return;

        // Don't show interaction if it's already unlocked
        if (MissionManager.Instance != null && MissionManager.Instance.IsBatteryRoomUnlocked.Value)
            return;

        // Check if player is looking at this object within range
        Ray ray = new Ray(fpc.playerCamera.transform.position, fpc.playerCamera.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, interactionRange))
        {
            if (hit.collider.gameObject == gameObject || hit.collider.transform.IsChildOf(transform))
            {
                // Show interaction text via FPC
                fpc.SetInteractionText("[F] Güvenlik Panelini Aç");

                if (Input.GetKeyDown(KeyCode.F))
                {
                    if (PuzzleUIManager.Instance != null && !PuzzleUIManager.Instance.IsPuzzleOpen)
                    {
                        PuzzleUIManager.Instance.OpenPuzzle();
                    }
                }
            }
        }
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
