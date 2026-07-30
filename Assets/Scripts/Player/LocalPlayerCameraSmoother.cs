using UnityEngine;

/// <summary>
/// Smooths only the local player's rendered camera between physics ticks.
/// It never changes Rigidbody position, velocity, collision or network state.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class LocalPlayerCameraSmoother : MonoBehaviour
{
    [SerializeField, Min(0.5f)] private float teleportSnapDistance = 1.5f;

    private Rigidbody body;
    private FirstPersonController controller;
    private Transform cameraTransform;
    private Transform cameraParent;
    private Vector3 cameraBaseLocalPosition;
    private Vector3 previousPhysicsPosition;
    private Vector3 currentPhysicsPosition;
    private bool physicsHistoryReady;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        controller = GetComponent<FirstPersonController>();
        BindCamera();
        ResetPhysicsHistory();
    }

    private void OnEnable()
    {
        physicsHistoryReady = false;
    }

    private void FixedUpdate()
    {
        if (!CanSmooth())
            return;

        CapturePhysicsPose();
    }

    private void LateUpdate()
    {
        if (!CanSmooth() || !BindCamera())
        {
            physicsHistoryReady = false;
            return;
        }

        Vector3 physicsPosition = body.position;
        float snapDistanceSqr = teleportSnapDistance * teleportSnapDistance;

        // Network spawn/teleport must be immediate; never interpolate from the old room.
        if (!physicsHistoryReady || (physicsPosition - currentPhysicsPosition).sqrMagnitude > snapDistanceSqr)
        {
            previousPhysicsPosition = physicsPosition;
            currentPhysicsPosition = physicsPosition;
            physicsHistoryReady = true;
        }

        float alpha = Time.fixedDeltaTime > 0f
            ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime)
            : 1f;

        Vector3 renderedBodyPosition = Vector3.Lerp(
            previousPhysicsPosition,
            currentPhysicsPosition,
            alpha);

        // Rebuild the unsmoothed camera target from its parent so head-bob and mouse look remain intact.
        Vector3 targetCameraWorldPosition = cameraParent.TransformPoint(cameraBaseLocalPosition);
        cameraTransform.position = targetCameraWorldPosition + (renderedBodyPosition - physicsPosition);
    }

    private bool CanSmooth()
    {
        if (body == null || controller == null || controller.playerCamera == null)
            return false;

        if (controller.IsSpawned && !controller.IsOwner)
            return false;

        if (controller.isDead.Value)
            return false;

        return controller.playerCamera.gameObject.activeInHierarchy;
    }

    private bool BindCamera()
    {
        if (controller == null || controller.playerCamera == null)
            return false;

        Transform nextCamera = controller.playerCamera.transform;
        if (nextCamera.parent == null)
            return false;

        if (cameraTransform != nextCamera)
        {
            cameraTransform = nextCamera;
            cameraParent = nextCamera.parent;
            cameraBaseLocalPosition = nextCamera.localPosition;
        }

        return true;
    }

    private void CapturePhysicsPose()
    {
        Vector3 position = body.position;
        float snapDistanceSqr = teleportSnapDistance * teleportSnapDistance;

        if (!physicsHistoryReady || (position - currentPhysicsPosition).sqrMagnitude > snapDistanceSqr)
        {
            previousPhysicsPosition = position;
            currentPhysicsPosition = position;
            physicsHistoryReady = true;
            return;
        }

        previousPhysicsPosition = currentPhysicsPosition;
        currentPhysicsPosition = position;
    }

    private void ResetPhysicsHistory()
    {
        if (body == null)
            return;

        previousPhysicsPosition = body.position;
        currentPhysicsPosition = body.position;
        physicsHistoryReady = true;
    }
}
