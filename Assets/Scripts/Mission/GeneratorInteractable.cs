using UnityEngine;

public class GeneratorInteractable : MonoBehaviour
{
    [Header("Settings")]
    public float interactionRange = 3f;
    public Transform rotorObject; // Battery1
    public float rotationSpeed = 360f; // degrees per second

    private void Start()
    {
        if (rotorObject != null)
        {
            rotorObject.gameObject.SetActive(false); // Hide initially
        }
    }

    private float activationTime = -1f;
    private bool wasActive = false;

    private void Update()
    {
        if (MissionManager.Instance == null) return;

        bool isActive = MissionManager.Instance.IsGeneratorActive.Value;
        
        if (isActive && !wasActive)
        {
            wasActive = true;
            activationTime = Time.time;
        }

        if (isActive)
        {
            if (rotorObject != null)
            {
                if (!rotorObject.gameObject.activeSelf) rotorObject.gameObject.SetActive(true);
                
                float timeSinceActivation = Time.time - activationTime;
                
                if (timeSinceActivation > 1f)
                {
                    // 1 saniye bekledikten sonra, 2 saniye boyunca hızlanma (spin-up)
                    float spinUpProgress = Mathf.Clamp01((timeSinceActivation - 1f) / 2f);
                    float currentSpeed = rotationSpeed * Mathf.SmoothStep(0f, 1f, spinUpProgress);
                    
                    rotorObject.Rotate(Vector3.forward, currentSpeed * Time.deltaTime, Space.Self);
                }
            }
            return; // No interaction needed anymore
        }

        if (!GameplayInteractionGate.IsTaskInteractionPhaseOpen())
            return;

        FirstPersonController fpc = GetOwnerFpc();
        if (fpc == null || fpc.isDead.Value || (GameManager.Instance && GameManager.Instance.isGameOver)) return;

        Ray ray = new Ray(fpc.playerCamera.transform.position, fpc.playerCamera.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, interactionRange))
        {
            if (hit.collider.gameObject == gameObject || hit.collider.transform.IsChildOf(transform))
            {
                // Prevent interaction if we actually hit the Battery2 pickup object (which might be a child by mistake)
                if (hit.collider.GetComponent<BatteryPickupInteractable>() != null) return;
                bool hasBattery = MissionManager.Instance.IsBatteryCollected.Value;
                if (hasBattery)
                {
                    fpc.SetInteractionText("[F] Bataryayı Tak");
                    if (Input.GetKeyDown(KeyCode.F))
                    {
                        MissionManager.Instance.ActivateGeneratorServerRpc();
                    }
                }
                else
                {
                    fpc.SetInteractionText("[Batarya Gerekiyor]");
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
