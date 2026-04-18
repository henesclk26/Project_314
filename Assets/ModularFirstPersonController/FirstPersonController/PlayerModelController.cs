using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Jammo karakter modelini FPS multiplayer'a entegre eder.
/// - Kendi ekranında modeli gizler (LocalPlayerModel katmanına taşır)
/// - Diğer oyuncuların ekranında modeli gösterir
/// - Hareket durumuna göre animasyonları tetikler
/// - NetworkAnimator ile animasyonları senkronize eder
/// </summary>
public class PlayerModelController : NetworkBehaviour
{
    [Header("Model Referansı")]
    [Tooltip("Jammo karakterinin görsel kök objesi")]
    public GameObject playerModel;

    [Header("Katman Ayarı")]
    [Tooltip("Kendi ekranımızda modeli gizlemek için kullanılan Layer numarası (LocalPlayerModel)")]
    public int localPlayerLayer = 8;

    private Animator animator;
    private Rigidbody rb;
    private FirstPersonController fpc;

    // Animator parametre hash'leri (performans için)
    private static readonly int IsWalkingHash = Animator.StringToHash("isWalking");
    private static readonly int IsRunningHash = Animator.StringToHash("isRunning");

    private void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        rb = GetComponent<Rigidbody>();

        if (playerModel != null)
        {
            animator = playerModel.GetComponentInChildren<Animator>();
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (playerModel == null) return;

        if (IsOwner)
        {
            // Kendi modelimizi kendi kameramızdan gizle
            // (LocalPlayerModel katmanına taşıyoruz, kamera bu katmanı render etmeyecek)
            SetLayerRecursively(playerModel, localPlayerLayer);
        }
        // Başkalarının modeli Default katmanda kalacak → herkes onları görecek
    }

    private void Update()
    {
        if (!IsOwner || animator == null || rb == null) return;

        UpdateAnimations();
    }

    private void UpdateAnimations()
    {
        // Yatay hızı hesapla (Y eksenini hariç tutuyoruz, düşme/zıplama hız olarak sayılmasın)
        Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        float speed = horizontalVelocity.magnitude;

        bool isMoving = speed > 0.1f;
        // walkSpeed'in üstündeyse koşuyor demektir
        bool isRunning = isMoving && fpc != null && speed > fpc.walkSpeed + 0.5f;

        animator.SetBool(IsWalkingHash, isMoving && !isRunning);
        animator.SetBool(IsRunningHash, isRunning);
    }

    /// <summary>
    /// Objenin ve tüm çocuklarının Layer'ını değiştirir
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