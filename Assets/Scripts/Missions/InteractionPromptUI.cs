using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

namespace SwitchToggleMission
{
    public class InteractionPromptUI : MonoBehaviour
    {
        public TextMeshProUGUI promptText;

        private InteractableObject[] interactableObjects;
        private GameObject playerObject;
        private PlayerRole playerRole;

        private void Start()
        {
            interactableObjects = FindObjectsByType<InteractableObject>(FindObjectsSortMode.None);
            TryCachePlayer();
            HidePrompt();
        }

        private void Update()
        {
            TryCachePlayer();

            if (promptText == null || playerRole == null || !playerRole.IsVillager())
            {
                HidePrompt();
                return;
            }

            InteractableObject nearbyObject = FindClosestInteractableInRange();
            if (nearbyObject == null)
            {
                HidePrompt();
                return;
            }

            promptText.gameObject.SetActive(true);
            promptText.text = nearbyObject.GetPromptText();
        }

        private void TryCachePlayer()
        {
            if (playerObject == null)
            {
                playerObject = GameObject.FindGameObjectWithTag("Player");
            }

            if (playerObject != null && playerRole == null)
            {
                playerRole = playerObject.GetComponent<PlayerRole>();
            }
        }

        private InteractableObject FindClosestInteractableInRange()
        {
            if (interactableObjects == null || playerObject == null)
            {
                return null;
            }

            float closestDistance = float.MaxValue;
            InteractableObject closestObject = null;

            for (int i = 0; i < interactableObjects.Length; i++)
            {
                InteractableObject interactable = interactableObjects[i];
                if (interactable == null || !interactable.isActiveAndEnabled || !interactable.IsPlayerInRange())
                {
                    continue;
                }

                float distance = Vector3.Distance(playerObject.transform.position, interactable.transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestObject = interactable;
                }
            }

            return closestObject;
        }

        private void HidePrompt()
        {
            if (promptText != null)
            {
                promptText.gameObject.SetActive(false);
            }
        }
    }
}
