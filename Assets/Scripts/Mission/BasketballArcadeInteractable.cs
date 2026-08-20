using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Repeatable basketball arcade mini-game. The station is intentionally
/// usable by both roles; the one-time personal task-point award is validated
/// by TaskManager on the server.
/// </summary>
public sealed class BasketballArcadeInteractable : MonoBehaviour
{
    public const string TaskId = "BasketballArcade";

    [Header("Challenge")]
    [SerializeField, Min(5f)] private float attemptDuration = 30f;
    [SerializeField, Min(1)] private int targetScore = 10;
    [SerializeField, Min(0.25f)] private float interactionRange = 3.2f;
    [SerializeField, Min(0.1f)] private float shotCooldown = 0.28f;
    [SerializeField, Min(0f)] private float rimMoveAmplitude = 0.65f;
    [SerializeField, Min(0.1f)] private float rimMoveSpeed = 1.15f;
    [SerializeField, Range(0.25f, 0.8f)] private float scoreZoneSizeRatio = 0.42f;

    [Header("Ball")]
    [SerializeField, Min(1f)] private float throwSpeed = 10.5f;
    [SerializeField, Range(0f, 0.6f)] private float throwArc = 0.2f;
    [SerializeField, Min(1f)] private float ballLifetime = 2.5f;
    [SerializeField, Min(0.05f)] private float ballScale = 0.33f;
    [SerializeField, Min(0.1f)] private float ballMass = 0.85f;
    [SerializeField, Min(0.1f)] private float ballSettleSpeed = 0.6f;
    [SerializeField, Min(0.1f)] private float ballSettleDuration = 0.35f;

    private readonly List<BasketballBall> activeBalls = new List<BasketballBall>();
    private Transform rim;
    private Transform startButton;
    private FirstPersonController localPlayer;
    private UIDocument gameUiDocument;
    private VisualElement taskPanel;
    private Label timeLabel;
    private VisualElement timeFill;
    private Label scoreLabel;
    private Label inputHint;
    // Keep the actual inline display style for each temporarily hidden
    // element. The resolved value can be `None` simply because the element
    // currently has the USS `hidden` class; restoring that resolved value as
    // an inline style would permanently hide the normal interaction prompts.
    private readonly Dictionary<VisualElement, StyleEnum<DisplayStyle>> hiddenHudElements =
        new Dictionary<VisualElement, StyleEnum<DisplayStyle>>();
    private bool uiReady;
    private bool promptOwned;
    private bool attemptActive;
    private float attemptEndsAt;
    private float nextShotAt;
    private int score;
    private int displayedSeconds = -1;
    private int displayedScore = -1;
    private bool previousPlayerCanMove;
    private bool previousCameraCanMove;
    private BasketballScoreZone scoreZone;
    private Vector3 rimInitialLocalPosition;

    public bool IsAttemptActive => attemptActive;
    public int Score => score;

    private void Awake()
    {
        rim = transform.Find("rim");
        if (rim == null)
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "rim")
                {
                    rim = child;
                    break;
                }
            }
        }

        if (rim != null)
            rimInitialLocalPosition = rim.localPosition;

        startButton = transform.Find("Basketball_StartButton");

        CreateScoreZone();
    }

    private void Update()
    {
        EnsureUi();

        if (localPlayer == null)
            localPlayer = LocalPlayerResolver.Get();

        if (attemptActive)
        {
            if (!CanContinueAttempt())
            {
                EndAttempt(false);
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                EndAttempt(false);
                return;
            }

            SetBasketballHudOnly(true);
            UpdateRimMotion();
            UpdateTaskUi();
            if (!attemptActive)
                return;

            if ((Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space)) &&
                Time.unscaledTime >= nextShotAt)
            {
                ThrowBall();
            }

            return;
        }

        if (!CanInteract())
        {
            ClearOwnedPrompt();
            return;
        }

        // FirstPersonController keeps interaction prompts alive for a short
        // interval. Refresh this prompt while the player remains in range;
        // otherwise it can disappear even though F still starts the game.
        localPlayer.SetInteractionText("[F] START BASKETBALL");
        promptOwned = true;

        if (Input.GetKeyDown(KeyCode.F))
            BeginAttempt();
    }

    private void LateUpdate()
    {
        // The regular HUD controllers refresh their own panels every frame.
        // Re-apply the basketball-only layout after those updates so the
        // killer task and voice status cannot flash back into the challenge.
        if (attemptActive)
            SetBasketballHudOnly(true);
    }

    private bool CanInteract()
    {
        if (localPlayer == null || localPlayer.playerCamera == null || localPlayer.isDead.Value ||
            !GameplayInteractionGate.IsTaskInteractionPhaseOpen() ||
            UpgradeUIManager.IsSelectionOpen)
            return false;

        Vector3 interactionPosition = startButton != null ? startButton.position : transform.position;
        if (Vector3.Distance(interactionPosition, localPlayer.transform.position) > interactionRange)
            return false;

        // The arcade start control follows the same proximity interaction
        // pattern as the emergency button: the player only needs to enter
        // the button's range; looking directly at the mesh is not required.
        return true;
    }

    private bool CanContinueAttempt()
    {
        return localPlayer != null && !localPlayer.isDead.Value &&
               GameplayInteractionGate.IsTaskInteractionPhaseOpen() &&
               (GameManager.Instance == null || !GameManager.Instance.isGameOver);
    }

    private void BeginAttempt()
    {
        if (attemptActive || localPlayer == null || !CanInteract())
            return;

        attemptActive = true;
        score = 0;
        attemptEndsAt = Time.unscaledTime + attemptDuration;
        nextShotAt = 0f;
        displayedSeconds = -1;
        displayedScore = -1;
        previousPlayerCanMove = localPlayer.playerCanMove;
        previousCameraCanMove = localPlayer.cameraCanMove;
        localPlayer.playerCanMove = false;
        localPlayer.cameraCanMove = false;
        ClearOwnedPrompt();
        ShowTaskUi();
        UpdateTaskUi();
    }

    private void ThrowBall()
    {
        if (localPlayer == null || localPlayer.playerCamera == null || HasLiveBall())
            return;

        nextShotAt = Time.unscaledTime + shotCooldown;
        Transform cameraTransform = localPlayer.playerCamera.transform;
        Vector3 spawnPosition = cameraTransform.position + cameraTransform.forward * 0.6f - cameraTransform.up * 0.14f;
        Vector3 throwDirection = (cameraTransform.forward + Vector3.up * throwArc).normalized;

        GameObject ballObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ballObject.name = "Basketball_LocalShot";
        ballObject.transform.SetPositionAndRotation(spawnPosition, Quaternion.identity);
        ballObject.transform.localScale = Vector3.one * ballScale;

        Rigidbody body = ballObject.AddComponent<Rigidbody>();
        body.mass = ballMass;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.linearVelocity = throwDirection * throwSpeed;

        Renderer renderer = ballObject.GetComponent<Renderer>();
        if (renderer != null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader != null)
            {
                Material material = new Material(shader)
                {
                    color = new Color(0.86f, 0.25f, 0.035f, 1f)
                };
                renderer.sharedMaterial = material;
            }
        }

        BasketballBall ball = ballObject.AddComponent<BasketballBall>();
        ball.Initialize(this, body, ballLifetime, ballSettleSpeed, ballSettleDuration);
        activeBalls.Add(ball);
        UpdateTaskUi();
    }

    internal void NotifyBallReturned(BasketballBall ball)
    {
        if (ball != null)
            activeBalls.Remove(ball);

        if (attemptActive)
            UpdateTaskUi();
    }

    internal bool TryRegisterBasket(BasketballBall ball)
    {
        if (!attemptActive || ball == null || ball.Owner != this)
            return false;

        score = Mathf.Min(targetScore, score + 1);
        UpdateTaskUi();
        if (score >= targetScore)
            EndAttempt(true);
        return true;
    }

    private void EndAttempt(bool succeeded)
    {
        if (!attemptActive)
            return;

        attemptActive = false;
        ResetRimMotion();
        HideTaskUi();
        ClearOwnedPrompt();
        ClearBalls();

        if (localPlayer != null)
        {
            localPlayer.playerCanMove = previousPlayerCanMove;
            localPlayer.cameraCanMove = previousCameraCanMove;
        }

        if (succeeded && TaskManager.Instance != null && TaskManager.Instance.IsSpawned)
            TaskManager.Instance.ReportBasketballTaskCompletedRpc();
    }

    private void ClearBalls()
    {
        for (int i = activeBalls.Count - 1; i >= 0; i--)
        {
            if (activeBalls[i] != null)
                Destroy(activeBalls[i].gameObject);
        }
        activeBalls.Clear();
    }

    private bool HasLiveBall()
    {
        for (int i = activeBalls.Count - 1; i >= 0; i--)
        {
            if (activeBalls[i] == null)
                activeBalls.RemoveAt(i);
        }

        return activeBalls.Count > 0;
    }

    private void CreateScoreZone()
    {
        if (rim == null)
            return;

        Renderer rimRenderer = rim.GetComponent<Renderer>();
        if (rimRenderer == null)
            return;

        GameObject zoneObject = new GameObject("Basketball_ScoreZone");
        zoneObject.transform.SetParent(transform, true);
        zoneObject.transform.localScale = Vector3.one;
        Bounds bounds = rimRenderer.bounds;
        zoneObject.transform.position = bounds.center;
        BoxCollider trigger = zoneObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        // Keep the scoring volume inside the rim opening. The old volume was
        // nearly as wide as the rim itself, so a ball could graze the side
        // and still trigger a score.
        trigger.size = new Vector3(
            Mathf.Max(0.2f, bounds.size.x * scoreZoneSizeRatio),
            0.2f,
            Mathf.Max(0.2f, bounds.size.z * scoreZoneSizeRatio));
        scoreZone = zoneObject.AddComponent<BasketballScoreZone>();
        scoreZone.Initialize(this, bounds.center.y);
    }

    private void UpdateRimMotion()
    {
        if (rim == null || scoreZone == null)
            return;

        Vector3 localPosition = rimInitialLocalPosition;
        localPosition.x += Mathf.Sin(Time.unscaledTime * rimMoveSpeed) * rimMoveAmplitude;
        rim.localPosition = localPosition;

        Renderer rimRenderer = rim.GetComponent<Renderer>();
        if (rimRenderer != null)
        {
            scoreZone.transform.position = rimRenderer.bounds.center;
            scoreZone.SetZoneHeight(rimRenderer.bounds.center.y);
        }
    }

    private void ResetRimMotion()
    {
        if (rim == null)
            return;

        rim.localPosition = rimInitialLocalPosition;
        Renderer rimRenderer = rim.GetComponent<Renderer>();
        if (rimRenderer != null && scoreZone != null)
        {
            scoreZone.transform.position = rimRenderer.bounds.center;
            scoreZone.SetZoneHeight(rimRenderer.bounds.center.y);
        }
    }

    private void EnsureUi()
    {
        if (uiReady && taskPanel != null)
            return;

        UIDocument[] documents = FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (UIDocument document in documents)
        {
            if (document == null || document.rootVisualElement == null)
                continue;

            VisualElement panel = document.rootVisualElement.Q<VisualElement>("basketball-task-panel");
            if (panel == null)
                continue;

            gameUiDocument = document;
            taskPanel = panel;
            timeLabel = panel.Q<Label>("basketball-time-label");
            timeFill = panel.Q<VisualElement>("basketball-time-fill");
            scoreLabel = panel.Q<Label>("basketball-score-label");
            inputHint = panel.Q<Label>("basketball-input-hint");
            taskPanel.pickingMode = PickingMode.Ignore;
            uiReady = true;
            HideTaskUi();
            return;
        }
    }

    private void ShowTaskUi()
    {
        EnsureUi();
        if (taskPanel != null)
        {
            taskPanel.style.display = DisplayStyle.Flex;
            SetBasketballHudOnly(true);
        }
    }

    private void HideTaskUi()
    {
        SetBasketballHudOnly(false);
        if (taskPanel != null)
            taskPanel.style.display = DisplayStyle.None;
    }

    private void SetBasketballHudOnly(bool basketballOnly)
    {
        if (gameUiDocument == null || gameUiDocument.rootVisualElement == null)
            return;

        if (basketballOnly)
        {
            string[] hudElementNames =
            {
                "game-version-label",
                "hud-top-left",
                "killer-hack-panel",
                "role-badge",
                "valve-warning-image",
                "game-prompt-container"
            };

            foreach (string elementName in hudElementNames)
            {
                VisualElement element = gameUiDocument.rootVisualElement.Q<VisualElement>(elementName);
                HideHudElement(element);
            }

            // GameplayStatusUIManager owns a separate runtime UIDocument for
            // Vivox, loadout, and alert status. Those panels are also hidden
            // during the mini-game so only timer and score remain visible.
            UIDocument[] documents = FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (UIDocument document in documents)
            {
                if (document == null || document.rootVisualElement == null || document == gameUiDocument)
                    continue;

                HideHudElement(document.rootVisualElement.Q<VisualElement>("gameplay-voice-status"));
                HideHudElement(document.rootVisualElement.Q<VisualElement>("gameplay-loadout"));
                HideHudElement(document.rootVisualElement.Q<VisualElement>("gameplay-alert"));
            }
        }
        else
        {
            foreach (KeyValuePair<VisualElement, StyleEnum<DisplayStyle>> item in hiddenHudElements)
            {
                if (item.Key != null)
                    item.Key.style.display = item.Value;
            }
            hiddenHudElements.Clear();
        }
    }

    private void HideHudElement(VisualElement element)
    {
        if (element == null || element == taskPanel)
            return;

        if (!hiddenHudElements.ContainsKey(element))
            hiddenHudElements[element] = element.style.display;

        element.style.display = DisplayStyle.None;
    }

    private void UpdateTaskUi()
    {
        if (!attemptActive)
            return;

        float remaining = Mathf.Max(0f, attemptEndsAt - Time.unscaledTime);
        int seconds = Mathf.CeilToInt(remaining);
        if (timeLabel != null && displayedSeconds != seconds)
        {
            timeLabel.text = $"{seconds:00} SEC";
            displayedSeconds = seconds;
        }
        if (scoreLabel != null && displayedScore != score)
        {
            scoreLabel.text = $"{score} / {targetScore}";
            displayedScore = score;
        }
        if (inputHint != null)
        {
            inputHint.text = HasLiveBall()
                ? "WAITING FOR BALL   //   [ESC] EXIT"
                : "[LMB] THROW   //   [ESC] EXIT";
        }
        if (timeFill != null)
            timeFill.style.width = Length.Percent(Mathf.Clamp01(remaining / attemptDuration) * 100f);

        if (remaining <= 0f)
            EndAttempt(false);
    }

    private void ClearOwnedPrompt()
    {
        if (!promptOwned || localPlayer == null)
            return;

        localPlayer.SetInteractionText(string.Empty);
        promptOwned = false;
    }

    private void OnDisable()
    {
        EndAttempt(false);
        ClearOwnedPrompt();
    }
}

public sealed class BasketballBall : MonoBehaviour
{
    public BasketballArcadeInteractable Owner { get; private set; }
    public Rigidbody Body { get; private set; }
    public Vector3 PreviousPosition { get; private set; }
    public bool HasScored { get; private set; }

    private float destroyAt;
    private float settledFor;
    private float settleSpeed = 0.6f;
    private float settleDuration = 0.35f;

    public void Initialize(
        BasketballArcadeInteractable owner,
        Rigidbody body,
        float lifetime,
        float returnSpeed,
        float returnDuration)
    {
        Owner = owner;
        Body = body;
        PreviousPosition = transform.position;
        destroyAt = Time.unscaledTime + lifetime;
        settleSpeed = Mathf.Max(0.1f, returnSpeed);
        settleDuration = Mathf.Max(0.1f, returnDuration);
    }

    private void FixedUpdate()
    {
        PreviousPosition = transform.position;
        if (Body != null && Body.linearVelocity.sqrMagnitude < settleSpeed * settleSpeed)
            settledFor += Time.fixedDeltaTime;
        else
            settledFor = 0f;

        if (Time.unscaledTime >= destroyAt || settledFor >= settleDuration)
        {
            Owner?.NotifyBallReturned(this);
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        Owner?.NotifyBallReturned(this);
    }

    internal bool TryMarkScored(float zoneHeight)
    {
        if (HasScored || Body == null || Body.linearVelocity.y >= -0.05f)
            return false;

        if (PreviousPosition.y < zoneHeight - 0.05f || transform.position.y > zoneHeight + 0.12f)
            return false;

        HasScored = true;
        return true;
    }
}

public sealed class BasketballScoreZone : MonoBehaviour
{
    private BasketballArcadeInteractable arcade;
    private float zoneHeight;

    public void Initialize(BasketballArcadeInteractable owner, float height)
    {
        arcade = owner;
        zoneHeight = height;
    }

    public void SetZoneHeight(float height)
    {
        zoneHeight = height;
    }

    private void OnTriggerEnter(Collider other)
    {
        TryScore(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryScore(other);
    }

    private void TryScore(Collider other)
    {
        BasketballBall ball = other.GetComponentInParent<BasketballBall>();
        if (ball != null && IsBallCenterInsideOpening(ball) && ball.TryMarkScored(zoneHeight))
            arcade?.TryRegisterBasket(ball);
    }

    private bool IsBallCenterInsideOpening(BasketballBall ball)
    {
        BoxCollider opening = GetComponent<BoxCollider>();
        if (opening == null || ball == null)
            return false;

        Bounds bounds = opening.bounds;
        Vector3 center = ball.transform.position;
        return center.x > bounds.min.x && center.x < bounds.max.x &&
               center.z > bounds.min.z && center.z < bounds.max.z;
    }
}
