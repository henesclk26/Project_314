using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class AutoDoor : MonoBehaviour
{
    [Header("Settings")]
    public float interactionRange = 4f;
    
    private Animator animator;
    private Animator linkedAnimator;
    private bool isOpen = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterAutomaticDoorSetup()
    {
        SceneManager.sceneLoaded -= SetupDoorPairs;
        SceneManager.sceneLoaded += SetupDoorPairs;
    }

    private static void SetupDoorPairs(Scene scene, LoadSceneMode mode)
    {
        Dictionary<string, GameObject> doorsByName = new Dictionary<string, GameObject>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (!doorsByName.ContainsKey(child.name))
                    doorsByName.Add(child.name, child.gameObject);
            }
        }

        foreach (KeyValuePair<string, GameObject> entry in doorsByName)
        {
            if (!IsPrimaryDoorName(entry.Key)
                || !doorsByName.TryGetValue(entry.Key + "-2", out GameObject linkedDoor)
                || entry.Value.GetComponent<LockedAutoDoor>() != null
                || entry.Value.GetComponent<AutoDoor>() != null
                || entry.Value.GetComponentInChildren<Animator>(true) == null
                || linkedDoor.GetComponentInChildren<Animator>(true) == null)
            {
                continue;
            }

            entry.Value.AddComponent<AutoDoor>();
        }
    }

    private static bool IsPrimaryDoorName(string name)
    {
        if (!name.StartsWith("Door") || name.Length == "Door".Length)
            return false;

        for (int i = "Door".Length; i < name.Length; i++)
        {
            if (!char.IsDigit(name[i]))
                return false;
        }

        return true;
    }

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
            var autoDoor = linkedObj.GetComponent<AutoDoor>();
            if (autoDoor != null) autoDoor.enabled = false;
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
