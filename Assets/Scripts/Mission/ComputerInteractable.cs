using UnityEngine;

public class ComputerInteractable : MonoBehaviour
{
    public ComputerData data;
    public float interactionRange = 3f;

    private bool isInRange = false;

    private void Update()
    {
        if (data == null) return;

        FirstPersonController ownerFpc = GetOwnerFpc();
        if (ownerFpc == null || ownerFpc.isDead.Value) 
        {
            SetInRange(false);
            return;
        }

        float distance = Vector3.Distance(transform.position, ownerFpc.transform.position);
        if (distance <= interactionRange)
        {
            SetInRange(true);
            
            // Interaction logic
            if (Input.GetKeyDown(KeyCode.F))
            {
                if (ComputerUIManager.Instance != null && !ComputerUIManager.Instance.IsComputerOpen)
                {
                    ComputerUIManager.Instance.OpenComputer(data, ownerFpc);
                }
            }
        }
        else
        {
            SetInRange(false);
        }
    }

    private void SetInRange(bool rangeStatus)
    {
        if (isInRange != rangeStatus)
        {
            isInRange = rangeStatus;
            if (ComputerUIManager.Instance != null)
            {
                ComputerUIManager.Instance.SetPromptVisible(isInRange);
            }
        }
    }

    private void OnDisable()
    {
        SetInRange(false);
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
        if (allFpcs.Length > 0) return allFpcs[0];
        return null;
    }
}
