using Unity.Netcode;
using UnityEngine;

public class WaveFrequencyTerminalInteractable : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float interactionRange = 3f;

    private bool completedLocally;

    private void Update()
    {
        if (AreBothMissionsCompleted())
            return;

        WaveFrequencyUIManager ui = WaveFrequencyUIManager.Instance;
        if (ui != null && ui.IsOpen)
            return;

        FirstPersonController fpc = GetOwnerFpc();
        if (fpc == null || fpc.playerCamera == null || fpc.isDead.Value ||
            (GameManager.Instance && GameManager.Instance.isGameOver))
            return;

        Ray ray = new Ray(fpc.playerCamera.transform.position, fpc.playerCamera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, interactionRange))
            return;

        if (hit.collider.gameObject != gameObject && !hit.collider.transform.IsChildOf(transform))
            return;

        fpc.SetInteractionText("[F] Frekans Terminalini Aç");

        if (Input.GetKeyDown(KeyCode.F) && ui != null)
            ui.Open(this, fpc);
    }

    public void MarkCompleted()
    {
        completedLocally = true;
    }

    private bool AreBothMissionsCompleted()
    {
        if (MissionManager.Instance == null)
            return false;

        bool normalCompleted =
            completedLocally ||
            MissionManager.Instance.IsWaveFrequencyMissionCompleted.Value;
        return normalCompleted &&
               MissionManager.Instance.IsWaveSatelliteSabotageCompleted.Value;
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
