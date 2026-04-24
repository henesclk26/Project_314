using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

namespace SwitchToggleMission
{
    public class InteractableObject : MonoBehaviour
    {
        private const string BusyText = "Meşgul";

        public string promptText = "Etkileşim [F]";
        public float interactRange = 3f;

        private bool isInUse;
        private GameObject cachedPlayer;
        private PlayerRole cachedRole;
        private readonly List<Collider> cachedColliders = new List<Collider>();
        private Bounds cachedBounds;
        private bool hasBounds;

        private void Awake()
        {
            CacheBounds();
            InteractionPromptUI.EnsureInstance();
            SwitchMinigameUI.EnsureInstance();
        }

        private void OnValidate()
        {
            interactRange = Mathf.Max(0.25f, interactRange);
            CacheBounds();
        }

        private void Update()
        {
            TryCachePlayer();
            if (cachedPlayer == null || !CanLocalPlayerUse())
            {
                return;
            }

            if (!IsPlayerInRange() || isInUse)
            {
                return;
            }

            if (GameManager.Instance != null && GameManager.Instance.isGameOver) return;

            if (Input.GetKeyDown(KeyCode.F))
            {
                isInUse = true;
                SwitchMinigameUI.EnsureInstance().OpenMinigame(this);
            }
        }

        public bool IsPlayerInRange()
        {
            TryCachePlayer();
            if (cachedPlayer == null)
            {
                return false;
            }

            // Hard gate: non-villagers should never be considered "in range" for computers,
            // so other systems (like prompt selection) won't surface interaction either.
            if (!CanLocalPlayerUse())
            {
                return false;
            }

            Vector3 playerPosition = cachedPlayer.transform.position;
            if (hasBounds)
            {
                Vector3 closestPoint = cachedBounds.ClosestPoint(playerPosition);
                return Vector3.Distance(playerPosition, closestPoint) <= interactRange;
            }

            return Vector3.Distance(playerPosition, transform.position) <= interactRange;
        }

        public string GetPromptText()
        {
            return isInUse ? BusyText : promptText;
        }

        public void ReleaseObject()
        {
            isInUse = false;
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

        private bool CanLocalPlayerUse()
        {
            // If the project uses RoleManager, treat only Crewmate as allowed.
            if (RoleManager.Instance != null)
            {
                return RoleManager.Instance.GetLocalPlayerRole() == global::PlayerRole.Crewmate;
            }

            // Fallback to local component role (if used in some scenes/tests).
            return cachedRole != null && cachedRole.IsVillager();
        }

        private void CacheBounds()
        {
            cachedColliders.Clear();
            GetComponentsInChildren(true, cachedColliders);

            hasBounds = false;
            for (int i = 0; i < cachedColliders.Count; i++)
            {
                Collider currentCollider = cachedColliders[i];
                if (currentCollider == null || !currentCollider.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    cachedBounds = currentCollider.bounds;
                    hasBounds = true;
                }
                else
                {
                    cachedBounds.Encapsulate(currentCollider.bounds);
                }
            }
        }
    }
}
