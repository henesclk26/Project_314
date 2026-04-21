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
        private GameObject playerObject;
        private PlayerRole playerRole;
        private readonly List<Collider> cachedColliders = new List<Collider>();
        private Bounds cachedBounds;
        private bool hasCachedBounds;

        private void Awake()
        {
            CacheBounds();
            TryCachePlayer();
        }

        private void Start()
        {
            TryCachePlayer();
        }

        private void Update()
        {
            TryCachePlayer();

            if (playerObject == null || playerRole == null || !playerRole.IsVillager())
            {
                return;
            }

            if (!IsPlayerInRange() || isInUse)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.F) && SwitchMinigameUI.Instance != null)
            {
                isInUse = true;
                SwitchMinigameUI.Instance.OpenMinigame(this);
            }
        }

        private void OnValidate()
        {
            interactRange = Mathf.Max(0.1f, interactRange);
            CacheBounds();
        }

        public bool IsPlayerInRange()
        {
            TryCachePlayer();

            if (playerObject == null)
            {
                return false;
            }

            Vector3 playerPosition = playerObject.transform.position;
            if (hasCachedBounds)
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
            if (playerObject == null)
            {
                playerObject = GameObject.FindGameObjectWithTag("Player");
            }

            if (playerObject != null && playerRole == null)
            {
                playerRole = playerObject.GetComponent<PlayerRole>();
            }
        }

        private void CacheBounds()
        {
            cachedColliders.Clear();
            GetComponentsInChildren(true, cachedColliders);

            hasCachedBounds = false;
            for (int i = 0; i < cachedColliders.Count; i++)
            {
                Collider currentCollider = cachedColliders[i];
                if (currentCollider == null || !currentCollider.enabled)
                {
                    continue;
                }

                if (!hasCachedBounds)
                {
                    cachedBounds = currentCollider.bounds;
                    hasCachedBounds = true;
                }
                else
                {
                    cachedBounds.Encapsulate(currentCollider.bounds);
                }
            }
        }
    }
}
