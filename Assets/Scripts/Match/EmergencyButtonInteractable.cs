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

        float distance = Vector3.Distance(transform.position, ownerFpc.transform.position);
        MatchPhase phase = MatchFlowManager.Instance.CurrentPhase.Value;
        if (distance > interactionRange || phase == MatchPhase.Lobby || phase == MatchPhase.Ended)
        {
            SetInRange(false, ownerFpc);
            return;
        }

        // Keep the lock feedback visible from the moment the match enters
        // BootProtection. Once all locks expire, this becomes the normal F
        // interaction without changing the button or its range.
        if (phase != MatchPhase.Active)
        {
            SetInRange(true, ownerFpc, false);
            return;
        }

        bool allowed = MatchFlowManager.Instance.IsEmergencyMeetingAllowed();
        SetInRange(true, ownerFpc, allowed);

        if (allowed && Input.GetKeyDown(KeyCode.F))
        {
            MeetingManager.Instance.RequestEmergencyMeetingServerRpc(ownerFpc.OwnerClientId);
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
                fpc.SetInteractionText(GetCooldownPrompt());
        }
        else if (!isInRange && fpc != null)
        {
            // FirstPersonController handles clearing the text on its own when not looking at an interactable or timer expires,
            // but we can manually clear it here if needed.
        }
    }

    private string GetCooldownPrompt()
    {
        MatchFlowManager flow = MatchFlowManager.Instance;
        if (flow == null || Unity.Netcode.NetworkManager.Singleton == null)
            return "COOLDOWN ACTIVE";

        double now = Unity.Netcode.NetworkManager.Singleton.LocalTime.Time;
        double nextMeetingTime = System.Math.Max(
            flow.FirstEmergencyLockEndTime.Value,
            flow.EmergencyCooldownEndTime.Value);
        int remainingSeconds = Mathf.Max(
            0,
            Mathf.CeilToInt((float)(nextMeetingTime - now)));

        return remainingSeconds > 0
            ? $"COOLDOWN ACTIVE\nREADY IN {remainingSeconds:00}s"
            : "COOLDOWN ACTIVE";
    }

    public bool IsPlayerInRange(ulong clientId)
    {
        foreach (FirstPersonController player in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
        {
            if (player.OwnerClientId != clientId)
                continue;

            return !player.isDead.Value &&
                   Vector3.Distance(transform.position, player.transform.position) <= interactionRange;
        }

        return false;
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
