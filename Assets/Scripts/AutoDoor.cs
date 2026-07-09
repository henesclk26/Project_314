using UnityEngine;

public class AutoDoor : MonoBehaviour
{
    [Header("Settings")]
    public float interactionRange = 4f;
    
    private Animator animator;
    private bool isOpen = false;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }
    }

    private void Update()
    {
        if (animator == null) return;

        FirstPersonController ownerFpc = GetOwnerFpc();
        if (ownerFpc == null || ownerFpc.isDead.Value) 
        {
            SetDoorState(false);
            return;
        }

        float distance = Vector3.Distance(transform.position, ownerFpc.transform.position);
        SetDoorState(distance <= interactionRange);
    }

    private void SetDoorState(bool state)
    {
        if (isOpen != state)
        {
            isOpen = state;
            animator.SetBool("IsOpen", isOpen);
        }
    }

    private FirstPersonController GetOwnerFpc()
    {
        var allFpcs = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
        if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening)
        {
            foreach (var f in allFpcs)
            {
                if (f.IsOwner) return f;
            }
        }
        if (allFpcs.Length > 0) return allFpcs[0]; // Fallback if networking is not active
        return null;
    }
}
