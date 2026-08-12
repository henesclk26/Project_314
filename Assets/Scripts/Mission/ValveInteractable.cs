using UnityEngine;

public class ValveInteractable : MonoBehaviour
{
    [Header("Settings")]
    public float interactionRange = 3f;
    public float turnDuration = 2f;
    public float totalRotation = 720f; // Spin 2 times

    private bool isTurned = false;
    private bool isTurning = false;
    private float turnTimer = 0f;
    private bool wasMissionActive;

    private void Update()
    {
        if (isTurning)
        {
            turnTimer += Time.deltaTime;
            float step = (totalRotation / turnDuration) * Time.deltaTime;
            transform.Rotate(Vector3.forward, step, Space.Self);
            
            if (turnTimer >= turnDuration)
            {
                isTurning = false;
            }
        }

        if (MissionManager.Instance == null) return;
        
        bool isMissionActive = MissionManager.Instance.IsValveMissionActive.Value;
        if (isMissionActive && !wasMissionActive)
            isTurned = false;
        wasMissionActive = isMissionActive;
        
        if (isTurned || !isMissionActive || !GameplayInteractionGate.IsTaskInteractionPhaseOpen())
            return;

        FirstPersonController fpc = GetOwnerFpc();
        if (fpc == null || fpc.isDead.Value || (GameManager.Instance && GameManager.Instance.isGameOver)) return;
        if (!MissionManager.Instance.IsValveOverrideParticipant(fpc.OwnerClientId)) return;

        Ray ray = new Ray(fpc.playerCamera.transform.position, fpc.playerCamera.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, interactionRange))
        {
            if (hit.collider.gameObject == gameObject)
            {
                fpc.SetInteractionText("[F] Vanayı Döndür");
                if (Input.GetKeyDown(KeyCode.F))
                {
                    isTurned = true;
                    isTurning = true;
                    turnTimer = 0f;
                    MissionManager.Instance.TurnValveServerRpc();
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
            return null;
        }
        if (allFpcs.Length > 0) return allFpcs[0];
        return null;
    }
}
