using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Jammo karakter modelini FPS multiplayer'a entegre eder.
/// - Kendi ekraninda modeli gizler (LocalPlayerModel katmanina tasir)
/// - Diger oyuncularin ekraninda modeli gosterir
/// - Oyuncu oldugunde modelini ve collider'larini kapatir
/// - Hareket durumuna gore animasyonlari tetikler
/// </summary>
public class PlayerModelController : NetworkBehaviour
{
    [Header("Model Referansi")]
    [Tooltip("Jammo karakterinin gorsel kok objesi")]
    public GameObject playerModel;

    [Header("Katman Ayari")]
    [Tooltip("Kendi ekranimizda modeli gizlemek icin kullanilan Layer numarasi (LocalPlayerModel)")]
    public int localPlayerLayer = 8;

    private Animator animator;
    private Rigidbody rb;
    private FirstPersonController fpc;
    private Renderer[] cachedRenderers;
    private Collider[] cachedColliders;

    // Animator parametre hash'leri (performans icin)
    private static readonly int IsWalkingHash = Animator.StringToHash("isWalking");
    private static readonly int IsRunningHash = Animator.StringToHash("isRunning");

    private void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        rb = GetComponent<Rigidbody>();
        cachedColliders = GetComponentsInChildren<Collider>(true);

        if (playerModel != null)
        {
            animator = playerModel.GetComponentInChildren<Animator>();
            cachedRenderers = playerModel.GetComponentsInChildren<Renderer>(true);
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (fpc != null)
        {
            fpc.isDead.OnValueChanged += OnDeadStateChanged;
            ApplyPresentationState(fpc.isDead.Value);
        }
        else
        {
            ApplyPresentationState(false);
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (fpc != null)
        {
            fpc.isDead.OnValueChanged -= OnDeadStateChanged;
        }
    }

    private void Update()
    {
        if (!IsOwner || animator == null || rb == null) return;
        if (fpc != null && fpc.isDead.Value) return;

        UpdateAnimations();
    }

    private void UpdateAnimations()
    {
        // Yatay hizi hesapla (Y eksenini haric tutuyoruz, dusme/ziplama hiz olarak sayilmasin)
        Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        float speed = horizontalVelocity.magnitude;

        bool isMoving = speed > 0.1f;
        bool isRunning = isMoving && fpc != null && speed > fpc.walkSpeed + 0.5f;

        animator.SetBool(IsWalkingHash, isMoving && !isRunning);
        animator.SetBool(IsRunningHash, isRunning);
    }

    private void OnDeadStateChanged(bool oldValue, bool newValue)
    {
        ApplyPresentationState(newValue);
    }

    private void ApplyPresentationState(bool isDead)
    {
        if (playerModel != null && IsOwner && !isDead)
        {
            // Kendi modelimizi kendi kameramizdan gizle.
            SetLayerRecursively(playerModel, localPlayerLayer);
        }

        if (cachedRenderers != null)
        {
            foreach (Renderer rendererComponent in cachedRenderers)
            {
                if (rendererComponent != null)
                {
                    rendererComponent.enabled = !isDead;
                }
            }
        }

        if (cachedColliders != null)
        {
            foreach (Collider colliderComponent in cachedColliders)
            {
                if (colliderComponent != null)
                {
                    colliderComponent.enabled = !isDead;
                }
            }
        }
    }

    /// <summary>
    /// Objenin ve tum cocuklarinin Layer'ini degistirir
    /// </summary>
    private void SetLayerRecursively(GameObject obj, int newLayer)
    {
        obj.layer = newLayer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, newLayer);
        }
    }
}
