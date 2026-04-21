using System.Collections.Generic;
using UnityEngine;

public class VillageMissionInteractable : MonoBehaviour
{
    public enum MissionInteractableType
    {
        GarbageBin,
        Shelf
    }

    private static readonly List<VillageMissionInteractable> RegisteredInteractables = new List<VillageMissionInteractable>();

    [SerializeField] private MissionInteractableType interactableType = MissionInteractableType.GarbageBin;
    [SerializeField] private string interactableId = "";
    [SerializeField] private float interactionRange = 3f;
    [SerializeField] private Sprite itemSprite;
    [SerializeField] private string itemDisplayName = "Görev Eşyası";

    private readonly List<Collider> cachedColliders = new List<Collider>();
    private Bounds interactionBounds;
    private bool hasBounds;

    public static IReadOnlyList<VillageMissionInteractable> All => RegisteredInteractables;

    public MissionInteractableType InteractableType => interactableType;
    public string InteractableId => string.IsNullOrWhiteSpace(interactableId) ? gameObject.name : interactableId;
    public float InteractionRange => interactionRange;
    public Sprite ItemSprite => itemSprite;
    public string ItemDisplayName => itemDisplayName;

    private void Awake()
    {
        EnsureId();
        CacheBounds();
    }

    private void OnEnable()
    {
        if (!RegisteredInteractables.Contains(this))
        {
            RegisteredInteractables.Add(this);
        }

        CacheBounds();
    }

    private void OnDisable()
    {
        RegisteredInteractables.Remove(this);
    }

    private void OnValidate()
    {
        EnsureId();
        interactionRange = Mathf.Max(0.5f, interactionRange);
        CacheBounds();
    }

    public float DistanceTo(Vector3 worldPosition)
    {
        if (hasBounds)
        {
            Vector3 closestPoint = interactionBounds.ClosestPoint(worldPosition);
            return Vector3.Distance(worldPosition, closestPoint);
        }

        return Vector3.Distance(worldPosition, transform.position);
    }

    private void EnsureId()
    {
        if (string.IsNullOrWhiteSpace(interactableId))
        {
            interactableId = gameObject.name;
        }
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
                interactionBounds = currentCollider.bounds;
                hasBounds = true;
            }
            else
            {
                interactionBounds.Encapsulate(currentCollider.bounds);
            }
        }
    }
}
