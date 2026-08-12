using Unity.Netcode;
using UnityEngine;

public class ReportableBody : NetworkBehaviour
{
    public NetworkVariable<ulong> VictimClientId = new NetworkVariable<ulong>(0);
    public NetworkVariable<double> DeathTime = new NetworkVariable<double>(0);

    public float reportRange = 4f;

    private void Update()
    {
        if (FirstPersonController.LocalPlayerIsDead ||
            MatchFlowManager.Instance == null ||
            MatchFlowManager.Instance.CurrentPhase.Value != MatchPhase.Active)
            return;

        FirstPersonController localFpc = GetLocalFpc();
        if (localFpc == null) return;

        float distance = Vector3.Distance(transform.position, localFpc.transform.position);
        if (distance <= reportRange)
        {
            localFpc.SetInteractionText("[F] REPORT UNIT");
            if (Input.GetKeyDown(KeyCode.F))
            {
                ReportBodyServerRpc();
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReportBodyServerRpc(ServerRpcParams rpcParams = default)
    {
        if (MatchFlowManager.Instance == null ||
            MatchFlowManager.Instance.CurrentPhase.Value != MatchPhase.Active)
            return;

        Debug.Log($"[ReportableBody] Body reported by {rpcParams.Receive.SenderClientId}");
        if (MeetingManager.Instance != null)
        {
            MeetingManager.Instance.CallMeeting(rpcParams.Receive.SenderClientId, VictimClientId.Value);
        }
    }

    private FirstPersonController GetLocalFpc()
    {
        foreach (var f in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
        {
            if (f.IsOwner) return f;
        }
        return null;
    }
}
