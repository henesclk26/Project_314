using UnityEngine;

public class SecurityCameraFeed : MonoBehaviour
{
    [Header("Feed Identity")]
    [SerializeField] private string feedId = "CAM-01";
    [SerializeField] private string displayName = "CAMERA 01";
    [SerializeField] private int displayOrder;

    [Header("Lens")]
    [SerializeField] private Vector3 cameraLocalPosition = Vector3.zero;
    [SerializeField] private Vector3 cameraLocalEuler = Vector3.zero;
    [SerializeField, Range(20f, 100f)] private float fieldOfView = 60f;
    [SerializeField] private float nearClipPlane = 0.05f;
    [SerializeField] private float farClipPlane = 200f;
    [SerializeField] private int textureWidth = 640;
    [SerializeField] private int textureHeight = 360;

    private Camera feedCamera;
    private RenderTexture outputTexture;
    private Transform cameraMount;

    public string FeedId => feedId;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? gameObject.name : displayName;
    public int DisplayOrder => displayOrder;
    public RenderTexture OutputTexture => outputTexture;

    private void Awake()
    {
        EnsureCamera();
        SetStreaming(false);
    }

    public void Configure(string id, string label, int order)
    {
        feedId = id;
        displayName = label;
        displayOrder = order;
    }

    public RenderTexture PrepareStream()
    {
        EnsureCamera();

        int width = Mathf.Max(320, textureWidth);
        int height = Mathf.Max(180, textureHeight);
        if (outputTexture == null || outputTexture.width != width || outputTexture.height != height)
        {
            ReleaseTexture();
            outputTexture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32)
            {
                name = "SecurityFeed_" + feedId,
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            outputTexture.Create();
        }

        feedCamera.targetTexture = outputTexture;
        return outputTexture;
    }

    public void SetStreaming(bool streaming)
    {
        EnsureCamera();
        if (streaming)
            PrepareStream();

        feedCamera.enabled = streaming;
    }

    private void EnsureCamera()
    {
        if (cameraMount == null)
        {
            Transform existing = transform.Find("__LiveFeedCamera");
            if (existing != null)
            {
                cameraMount = existing;
            }
            else
            {
                GameObject cameraObject = new GameObject("__LiveFeedCamera");
                cameraMount = cameraObject.transform;
                cameraMount.SetParent(transform, false);
            }
        }

        cameraMount.localPosition = cameraLocalPosition;
        cameraMount.localEulerAngles = cameraLocalEuler;

        if (feedCamera == null)
            feedCamera = cameraMount.GetComponent<Camera>();
        if (feedCamera == null)
            feedCamera = cameraMount.gameObject.AddComponent<Camera>();

        feedCamera.fieldOfView = fieldOfView;
        feedCamera.nearClipPlane = Mathf.Max(0.01f, nearClipPlane);
        feedCamera.farClipPlane = Mathf.Max(feedCamera.nearClipPlane + 1f, farClipPlane);
        feedCamera.depth = -20f;
        feedCamera.allowHDR = false;
        feedCamera.allowMSAA = false;
        feedCamera.enabled = false;
    }

    private void OnDisable()
    {
        if (feedCamera != null)
            feedCamera.enabled = false;
    }

    private void OnDestroy()
    {
        if (feedCamera != null)
            feedCamera.targetTexture = null;
        ReleaseTexture();
    }

    private void ReleaseTexture()
    {
        if (outputTexture == null)
            return;

        if (outputTexture.IsCreated())
            outputTexture.Release();
        Destroy(outputTexture);
        outputTexture = null;
    }
}
