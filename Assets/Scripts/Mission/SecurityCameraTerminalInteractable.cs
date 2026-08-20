using UnityEngine;
using Unity.Netcode;

public class SecurityCameraTerminalInteractable : MonoBehaviour
{
    [SerializeField] private float interactionRange = 4f;

    private bool promptVisible;

    private void Update()
    {
        SecurityCameraUIManager ui = SecurityCameraUIManager.Instance;
        FirstPersonController fpc = GetOwnerFpc();

        if (ui == null || fpc == null || fpc.playerCamera == null ||
            fpc.isDead.Value || (GameManager.Instance != null && GameManager.Instance.isGameOver))
        {
            SetPrompt(false);
            return;
        }

        if (!GameplayInteractionGate.IsTaskInteractionPhaseOpen())
        {
            SetPrompt(false);
            return;
        }

        if (ui.IsOpen)
        {
            SetPrompt(false);
            return;
        }

        bool canInteract = IsLookingAtTerminal(fpc);
        SetPrompt(canInteract);

        if (canInteract && Input.GetKeyDown(KeyCode.F))
            ui.Open(fpc);
    }

    private bool IsLookingAtTerminal(FirstPersonController fpc)
    {
        if (Vector3.Distance(transform.position, fpc.transform.position) > interactionRange)
            return false;

        Ray ray = new Ray(fpc.playerCamera.transform.position, fpc.playerCamera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, interactionRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return false;

        Transform hitTransform = hit.collider.transform;
        return hitTransform == transform ||
               hitTransform.IsChildOf(transform) ||
               transform.IsChildOf(hitTransform);
    }

    private void SetPrompt(bool visible)
    {
        if (promptVisible == visible)
            return;

        promptVisible = visible;
        if (SecurityCameraUIManager.Instance != null)
            SecurityCameraUIManager.Instance.SetPromptVisible(visible);
    }

    private void OnDisable()
    {
        SetPrompt(false);
    }

    private static FirstPersonController GetOwnerFpc()
    {
        return LocalPlayerResolver.Get();
    }
}
