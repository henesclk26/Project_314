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

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ReportBodyServerRpc(RpcParams rpcParams = default)
    {
        if (MatchFlowManager.Instance == null ||
            MatchFlowManager.Instance.CurrentPhase.Value != MatchPhase.Active)
            return;

        ulong reporterClientId = rpcParams.Receive.SenderClientId;
        FirstPersonController reporter = FindPlayer(reporterClientId);
        if (reporter == null || reporter.isDead.Value ||
            Vector3.Distance(transform.position, reporter.transform.position) > reportRange)
            return;

        Debug.Log($"[ReportableBody] Body reported by {reporterClientId}");
        if (MeetingManager.Instance != null)
        {
            MeetingManager.Instance.CallMeeting(reporterClientId, VictimClientId.Value);
        }
    }

    private FirstPersonController FindPlayer(ulong clientId)
    {
        foreach (FirstPersonController player in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
        {
            if (player.OwnerClientId == clientId)
                return player;
        }

        return null;
    }

    private FirstPersonController GetLocalFpc()
    {
        return LocalPlayerResolver.Get();
    }
}
