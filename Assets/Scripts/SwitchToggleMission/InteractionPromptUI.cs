using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

namespace SwitchToggleMission
{
    public class InteractionPromptUI : MonoBehaviour
    {
        public TextMeshProUGUI promptText;

        private static InteractionPromptUI instance;

        private InteractableObject[] interactables;
        private GameObject cachedPlayer;
        private PlayerRole cachedRole;

        public static InteractionPromptUI EnsureInstance()
        {
            if (instance != null)
            {
                return instance;
            }

            instance = FindFirstObjectByType<InteractionPromptUI>();
            if (instance != null)
            {
                instance.EnsureUi();
                return instance;
            }

            GameObject root = new GameObject("InteractionPromptUI");
            instance = root.AddComponent<InteractionPromptUI>();
            instance.EnsureUi();
            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            EnsureUi();
        }

        private void Start()
        {
            interactables = FindObjectsByType<InteractableObject>(FindObjectsSortMode.None);
            TryCachePlayer();
            HidePrompt();
        }

        private void Update()
        {
            if (interactables == null || interactables.Length == 0)
            {
                interactables = FindObjectsByType<InteractableObject>(FindObjectsSortMode.None);
            }

            TryCachePlayer();
            if (promptText == null || cachedRole == null || !cachedRole.IsVillager() || SwitchMinigameUI.HasOpenInstance())
            {
                HidePrompt();
                return;
            }

            InteractableObject activeInteractable = FindClosestInteractable();
            if (activeInteractable == null)
            {
                HidePrompt();
                return;
            }

            promptText.gameObject.SetActive(true);
            promptText.text = activeInteractable.GetPromptText();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        private void EnsureUi()
        {
            if (promptText != null)
            {
                return;
            }

            Transform canvas = SwitchMinigameUI.GetOrCreateCanvasRoot();

            Transform existing = canvas.Find("PromptText");
            GameObject promptObject = existing != null
                ? existing.gameObject
                : new GameObject("PromptText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));

            if (existing == null)
            {
                promptObject.transform.SetParent(canvas, false);
            }

            promptText = promptObject.GetComponent<TextMeshProUGUI>();
            promptText.font = SwitchMinigameUI.GetDefaultFont();
            promptText.fontSize = 30f;
            promptText.alignment = TextAlignmentOptions.Center;
            promptText.color = Color.white;
            promptText.raycastTarget = false;

            RectTransform rectTransform = promptObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0f);
            rectTransform.anchorMax = new Vector2(0.5f, 0f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = new Vector2(0f, 110f);
            rectTransform.sizeDelta = new Vector2(700f, 70f);

            HidePrompt();
        }

        private void TryCachePlayer()
        {
            if (cachedPlayer == null)
            {
                cachedPlayer = GameObject.FindGameObjectWithTag("Player");
            }

            if (cachedPlayer != null && cachedRole == null)
            {
                cachedRole = cachedPlayer.GetComponent<PlayerRole>();
            }
        }

        private InteractableObject FindClosestInteractable()
        {
            if (interactables == null || cachedPlayer == null)
            {
                return null;
            }

            float closestDistance = float.MaxValue;
            InteractableObject closest = null;

            for (int i = 0; i < interactables.Length; i++)
            {
                InteractableObject interactable = interactables[i];
                if (interactable == null || !interactable.isActiveAndEnabled || !interactable.IsPlayerInRange())
                {
                    continue;
                }

                float distance = Vector3.Distance(cachedPlayer.transform.position, interactable.transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = interactable;
                }
            }

            return closest;
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
