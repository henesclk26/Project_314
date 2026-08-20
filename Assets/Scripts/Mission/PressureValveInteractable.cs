using Unity.Netcode;
using UnityEngine;

public class PressureValveInteractable : MonoBehaviour
{
    [SerializeField] private int valveId;
    [SerializeField] private float interactionRange = 3f;
    [SerializeField] private float turnDuration = 1f;
    [SerializeField] private float totalRotation = 360f;

    private Quaternion startingRotation;
    private bool isTurning;
    private float turnTimer;
    private float inputLockedUntil;
    private int activeDirection;
    private int lastTurnSequence = -1;

    private void Awake()
    {
        startingRotation = transform.localRotation;
    }

    private void Update()
    {
        UpdateReplicatedRotation();
        UpdateRotationAnimation();

        MissionManager mission = MissionManager.Instance;
        if (mission == null || !mission.IsPressureMissionActive.Value || mission.IsPressureMissionCompleted.Value)
            return;

        if (!GameplayInteractionGate.IsTaskInteractionPhaseOpen())
        {
            PressureMissionUIManager.Instance?.SetValveHint(this, false);
            return;
        }

        FirstPersonController fpc = GetOwnerFpc();
        if (fpc == null || fpc.playerCamera == null || fpc.isDead.Value ||
            (GameManager.Instance != null && GameManager.Instance.isGameOver))
            return;

        if (TaskManager.Instance == null ||
            (!GameplayInteractionGate.IsQuickTestMode &&
             !TaskManager.Instance.IsCooperativeTaskParticipant(fpc.OwnerClientId, "PressureTerminal")) ||
            (GameplayInteractionGate.IsQuickTestMode &&
             !TaskManager.Instance.CanUseAlibiTask(fpc.OwnerClientId, "PressureTerminal") &&
             !TaskManager.Instance.IsCooperativeTaskParticipant(fpc.OwnerClientId, "PressureTerminal")))
            return;

        Ray ray = new Ray(fpc.playerCamera.transform.position, fpc.playerCamera.transform.forward);
        bool isLookingAtValve = Physics.Raycast(ray, out RaycastHit hit, interactionRange) &&
            (hit.collider.gameObject == gameObject || hit.collider.transform.IsChildOf(transform));

        PressureMissionUIManager.Instance?.SetValveHint(this, isLookingAtValve);
        if (!isLookingAtValve || isTurning || Time.time < inputLockedUntil)
            return;

        // Every living participant may use either pressure valve. There is no
        // per-player first-valve binding; the server validates each input
        // against the shared cooperative task on every request.
        if (Input.GetKeyDown(KeyCode.E))
            RequestTurn(1);
        else if (Input.GetKeyDown(KeyCode.Q))
            RequestTurn(-1);
    }

    private void OnDisable()
    {
        PressureMissionUIManager.Instance?.SetValveHint(this, false);
    }

    private void RequestTurn(int direction)
    {
        inputLockedUntil = Time.time + turnDuration;
        MissionManager.Instance?.AdjustPressureValveRpc(valveId, direction);
    }

    private void UpdateReplicatedRotation()
    {
        MissionManager mission = MissionManager.Instance;
        if (mission == null)
            return;

        int sequence = valveId == 3 ? mission.Valve003TurnSequence.Value : mission.Valve004TurnSequence.Value;
        if (lastTurnSequence < 0)
        {
            lastTurnSequence = sequence;
            return;
        }

        if (sequence == lastTurnSequence)
            return;

        lastTurnSequence = sequence;
        activeDirection = valveId == 3 ? mission.Valve003TurnDirection.Value : mission.Valve004TurnDirection.Value;
        startingRotation = transform.localRotation;
        turnTimer = 0f;
        isTurning = true;
    }

    private void UpdateRotationAnimation()
    {
        if (!isTurning)
            return;

        turnTimer += Time.deltaTime;
        float progress = Mathf.Clamp01(turnTimer / turnDuration);
        float easedProgress = Mathf.SmoothStep(0f, 1f, progress);
        transform.localRotation = startingRotation * Quaternion.AngleAxis(
            activeDirection * totalRotation * easedProgress,
            Vector3.forward);

        if (progress >= 1f)
            isTurning = false;
    }

    private static FirstPersonController GetOwnerFpc()
    {
        return LocalPlayerResolver.Get();
    }
}
