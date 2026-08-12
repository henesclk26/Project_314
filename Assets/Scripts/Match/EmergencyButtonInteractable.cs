using UnityEngine;

public class EmergencyButtonInteractable : MonoBehaviour
{
    public float interactionRange = 3f;
    private bool isInRange = false;

    private void Update()
    {
        FirstPersonController ownerFpc = GetOwnerFpc();
        if (ownerFpc == null || ownerFpc.isDead.Value || MatchFlowManager.Instance == null || MeetingManager.Instance == null)
        {
            SetInRange(false, null);
            return;
        }

        if (MatchFlowManager.Instance.CurrentPhase.Value != MatchPhase.Active)
        {
            SetInRange(false, ownerFpc);
            return;
        }

        float distance = Vector3.Distance(transform.position, ownerFpc.transform.position);
        if (distance <= interactionRange)
        {
            bool allowed = MatchFlowManager.Instance.IsEmergencyMeetingAllowed();
            SetInRange(true, ownerFpc, allowed);

            if (allowed && Input.GetKeyDown(KeyCode.F))
            {
                CallEmergencyMeetingServerRpc(ownerFpc.OwnerClientId);
            }
        }
        else
        {
            SetInRange(false, ownerFpc);
        }
    }

    private void SetInRange(bool rangeStatus, FirstPersonController fpc, bool allowed = true)
    {
        if (isInRange != rangeStatus)
        {
            isInRange = rangeStatus;
        }

        if (isInRange && fpc != null)
        {
            if (allowed)
                fpc.SetInteractionText("[F] EMERGENCY MEETING");
            else
                fpc.SetInteractionText("COOLDOWN ACTIVE");
        }
        else if (!isInRange && fpc != null)
        {
            // FirstPersonController handles clearing the text on its own when not looking at an interactable or timer expires,
            // but we can manually clear it here if needed.
        }
    }

    [Unity.Netcode.ServerRpc(RequireOwnership = false)]
    private void CallEmergencyMeetingServerRpc(ulong callerId)
    {
        if (MatchFlowManager.Instance.IsEmergencyMeetingAllowed())
        {
            MeetingManager.Instance.CallMeeting(callerId, 0);
        }
    }

    private void OnDisable()
    {
        SetInRange(false, null);
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
            return null;
        }
        if (allFpcs.Length > 0) return allFpcs[0];
        return null;
    }
}
