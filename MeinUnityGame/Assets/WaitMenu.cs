using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Test controls for waiting in whole game hours, using the project's Input System.
/// GameClock.TimeAdvanced reports the before/after interval for future NPC schedules,
/// fatigue, hunger and thirst. GameClock.DayChanged reports the destination day.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(GameClock))]
public sealed class WaitMenu : MonoBehaviour
{
    [SerializeField] private GameClock clock;

    private bool isOpen;
    private bool wasPaused;
    private int pendingHours;
    private GameObject uiRoot;
    private GameObject panel;
    private Button openButton;
    private Button waitButton;
    private InputField hoursInput;
    private Text preview;
    private Text status;
    private Text clockDisplay;
    private Font font;

    private void Awake()
    {
        if (clock == null)
            clock = GetComponent<GameClock>();
        BuildUI();
    }

    private void OnEnable()
    {
        if (uiRoot == null)
            BuildUI();
        uiRoot.SetActive(true);
        RefreshClockDisplay();
    }

    private void Update()
    {
        // Consume each request once, before DayNightCycle.LateUpdate.
        if (pendingHours > 0)
        {
            int hours = pendingHours;
            pendingHours = 0;
            if (TryWaitHours(hours))
            {
                Close();
                status.text = hours == 1 ? "1 Spielstunde gewartet." : $"{hours} Spielstunden gewartet.";
            }
            else
            {
                status.text = "Diese Wartezeit ist nicht möglich.";
            }
        }

        if (isOpen)
            RefreshPreview();
    }

    private void LateUpdate()
    {
        // Read after clock ticks and wait requests, including jumps across midnight.
        RefreshClockDisplay();
    }

    private void RefreshClockDisplay()
    {
        if (clockDisplay != null && clock != null)
            clockDisplay.text = FormatTime(clock.TotalGameMinutes);
    }

    /// <summary>
    /// Reusable entry point for future wait/sleep controls. Preserves clock speed
    /// and pause state; invalid requests do not advance time.
    /// </summary>
    public bool TryWaitHours(int hours)
    {
        if (!TryGetTarget(hours, out _))
            return false;

        clock.AdvanceHours(hours);
        return true;
    }

    private bool TryGetTarget(int hours, out double targetMinutes)
    {
        targetMinutes = 0d;
        if (clock == null || hours < 1)
            return false;

        targetMinutes = clock.TotalGameMinutes + hours * 60d;
        // Match GameClock's supported day range, with no artificial one-day limit.
        return targetMinutes < (double)int.MaxValue * 1440d;
    }

    private void Open()
    {
        if (isOpen || clock == null)
            return;

        wasPaused = clock.IsPaused;
        clock.IsPaused = true;
        isOpen = true;
        status.text = "";
        panel.SetActive(true);
        openButton.gameObject.SetActive(false);
        RefreshPreview();
        hoursInput.Select();
        hoursInput.ActivateInputField();
    }

    private void Close()
    {
        if (!isOpen)
            return;

        if (clock != null)
            clock.IsPaused = wasPaused;
        isOpen = false;
        pendingHours = 0;
        panel.SetActive(false);
        openButton.gameObject.SetActive(true);
    }

    private void RequestWait()
    {
        if (pendingHours == 0 && int.TryParse(hoursInput.text, out int hours) &&
            TryGetTarget(hours, out _))
        {
            pendingHours = hours;
            waitButton.interactable = false;
        }
    }

    private void RefreshPreview()
    {
        double target = 0d;
        bool valid = int.TryParse(hoursInput.text, out int hours) && TryGetTarget(hours, out target);
        preview.text = valid ? "Ziel: " + FormatTime(target) : "Bitte eine positive ganze Zahl eingeben.";
        waitButton.interactable = valid && pendingHours == 0;
    }

    private void OnDisable()
    {
        Close();
        if (uiRoot != null)
            uiRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        if (uiRoot != null)
            Destroy(uiRoot);
    }

    private static string FormatTime(double minutes)
    {
        int day = (int)System.Math.Floor(minutes / 1440d) + 1;
        int hour = (int)(minutes % 1440d / 60d);
        int minute = (int)(minutes % 60d);
        return $"Tag {day} – {hour:00}:{minute:00}";
    }

    private void BuildUI()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        // A Screen Space Overlay canvas must remain a scene root, not a child
        // of GameClock, the player or a camera. OnDestroy still owns its cleanup.
        uiRoot = new GameObject("GameClock UI", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        uiRoot.layer = 5;
        SceneManager.MoveGameObjectToScene(uiRoot, gameObject.scene);
        RectTransform rootRect = uiRoot.GetComponent<RectTransform>();
        rootRect.localScale = Vector3.one;
        rootRect.localRotation = Quaternion.identity;
        Canvas canvas = uiRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.worldCamera = null;
        canvas.targetDisplay = 0;
        canvas.sortingOrder = 100;
        canvas.enabled = true;

        // Keep the complete top-anchored controls inside smaller Game windows too.
        CanvasScaler scaler = uiRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        RectTransform clockPanel = MakeRect("Clock", uiRoot.transform, 0f, 12f, 320f, 48f);
        Image clockBackground = clockPanel.gameObject.AddComponent<Image>();
        clockBackground.color = new Color(0.04f, 0.05f, 0.07f, 0.96f);
        clockBackground.raycastTarget = false;
        clockDisplay = MakeText("", clockPanel, 0f, 0f, 320f, 48f);
        clockDisplay.fontSize = 24;
        clockDisplay.fontStyle = FontStyle.Bold;
        RefreshClockDisplay();

        // Create input routing only if this scene does not already have it.
        if (EventSystem.current == null)
        {
            GameObject events = new GameObject("Wait UI Events", typeof(EventSystem));
            events.transform.SetParent(uiRoot.transform, false);
            // Default UI actions are assigned automatically by the module's OnEnable.
            events.AddComponent<InputSystemUIInputModule>();
        }

        openButton = MakeButton("Warten", uiRoot.transform, 0f, 72f, 140f, 36f, Open);
        panel = MakeRect("Wait Panel", uiRoot.transform, 0f, 76f, 400f, 226f).gameObject;
        panel.AddComponent<Image>().color = new Color(0.04f, 0.05f, 0.07f, 0.96f);
        MakeText("Warten – Spieluhr pausiert", panel.transform, 0f, 12f, 368f, 30f);
        MakeText("Ganze Spielstunden:", panel.transform, -60f, 54f, 248f, 32f);

        RectTransform field = MakeRect("Hours", panel.transform, 130f, 54f, 108f, 34f);
        Image fieldBackground = field.gameObject.AddComponent<Image>();
        fieldBackground.color = new Color(0.16f, 0.19f, 0.24f, 1f);
        hoursInput = field.gameObject.AddComponent<InputField>();
        hoursInput.targetGraphic = fieldBackground;
        hoursInput.textComponent = MakeText("", field, 0f, 2f, 92f, 30f);
        hoursInput.contentType = InputField.ContentType.IntegerNumber;
        hoursInput.characterLimit = 10;
        hoursInput.text = "1";
        hoursInput.onValueChanged.AddListener(_ => RefreshPreview());

        preview = MakeText("", panel.transform, 0f, 106f, 368f, 44f);
        waitButton = MakeButton("Warten", panel.transform, -96f, 174f, 176f, 36f, RequestWait);
        MakeButton("Abbrechen", panel.transform, 96f, 174f, 176f, 36f, Close);
        status = MakeText("", uiRoot.transform, 0f, 312f, 400f, 40f);
        panel.SetActive(false);
    }

    private static RectTransform MakeRect(string name, Transform parent, float x, float top, float width, float height)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.layer = 5; // UI
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(x, -top);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    private Text MakeText(string value, Transform parent, float x, float top, float width, float height)
    {
        Text text = MakeRect("Label", parent, x, top, width, height).gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = 18;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        text.text = value;
        return text;
    }

    private Button MakeButton(string title, Transform parent, float x, float top, float width, float height,
        UnityAction action)
    {
        RectTransform rect = MakeRect(title, parent, x, top, width, height);
        Image background = rect.gameObject.AddComponent<Image>();
        background.color = new Color(0.2f, 0.26f, 0.34f, 1f);
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(action);
        MakeText(title, rect, 0f, 0f, width, height);
        return button;
    }
}
