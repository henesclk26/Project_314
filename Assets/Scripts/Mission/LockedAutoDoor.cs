using UnityEngine;

public class LockedAutoDoor : MonoBehaviour
{
    [Header("Settings")]
    public float interactionRange = 4f;
    
    private Animator animator;
    private Animator linkedAnimator;
    private bool isOpen = false;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        // Check for a linked -2 door
        GameObject linkedObj = GameObject.Find(gameObject.name + "-2");
        if (linkedObj != null)
        {
            linkedAnimator = linkedObj.GetComponent<Animator>();
            if (linkedAnimator == null) linkedAnimator = linkedObj.GetComponentInChildren<Animator>();
            
            // Disable independent scripts on the linked door so it only listens to this one
            var lockedDoor = linkedObj.GetComponent<LockedAutoDoor>();
            if (lockedDoor != null) lockedDoor.enabled = false;
            var autoDoor = linkedObj.GetComponent<AutoDoor>();
            if (autoDoor != null) autoDoor.enabled = false;
        }
    }

    private void Update()
    {
        if (animator == null) return;

        // Is it unlocked globally?
        bool isUnlocked = MissionManager.Instance != null && MissionManager.Instance.IsBatteryRoomUnlocked.Value;

        FirstPersonController ownerFpc = GetOwnerFpc();
        if (ownerFpc == null || ownerFpc.isDead.Value || !isUnlocked) 
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
            if (animator != null) animator.SetBool("IsOpen", isOpen);
            if (linkedAnimator != null) linkedAnimator.SetBool("IsOpen", isOpen);
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
