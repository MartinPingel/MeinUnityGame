using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Village.Npc;

/// <summary>Development-only NPC inspector overlay. F3 toggles it; never drawn in release builds.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NpcAgent))]
public sealed class NpcDebugDisplay : MonoBehaviour
{
    [SerializeField] private bool showDebug = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private NpcAgent agent;
    private GameObject uiRoot;
    private RectTransform panel;
    private Text values;

    private void OnEnable()
    {
        agent = GetComponent<NpcAgent>();
        if (uiRoot == null) BuildUI();
        RefreshDisplay();
        uiRoot.SetActive(showDebug);
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
        {
            showDebug = !showDebug;
            RefreshDisplay();
            uiRoot.SetActive(showDebug);
        }
    }

    private void LateUpdate()
    {
        // Read after clock/wait updates; toggling never advances or resets Hans.
        if (uiRoot.activeSelf != showDebug) uiRoot.SetActive(showDebug);
        if (showDebug) RefreshDisplay();
    }

    private void OnDisable()
    {
        if (uiRoot != null) uiRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        if (uiRoot != null) Destroy(uiRoot);
    }

    private void BuildUI()
    {
        uiRoot = new GameObject("Hans Debug UI", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler));
        uiRoot.SetActive(false);
        uiRoot.layer = 5;
        SceneManager.MoveGameObjectToScene(uiRoot, gameObject.scene);
        Canvas canvas = uiRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.worldCamera = null;
        canvas.targetDisplay = 0;
        canvas.sortingOrder = 90; // Keep the existing clock/wait canvas above debug UI.
        CanvasScaler scaler = uiRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        GameObject panelObject = new GameObject("Debug Panel", typeof(RectTransform), typeof(Image));
        panelObject.layer = 5;
        panel = panelObject.GetComponent<RectTransform>();
        panel.SetParent(uiRoot.transform, false);
        panel.anchorMin = panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 1f);
        panel.anchoredPosition = new Vector2(12f, -76f);
        panel.sizeDelta = new Vector2(340f, 310f);
        Image background = panelObject.GetComponent<Image>();
        background.color = new Color(0.04f, 0.05f, 0.07f, 0.96f);
        background.raycastTarget = false;

        GameObject textObject = new GameObject("Hans Values", typeof(RectTransform), typeof(Text));
        textObject.layer = 5;
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(panel, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 10f);
        textRect.offsetMax = new Vector2(-12f, -10f);
        values = textObject.GetComponent<Text>();
        values.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        values.fontSize = 16;
        values.color = Color.white;
        values.alignment = TextAnchor.UpperLeft;
        values.horizontalOverflow = HorizontalWrapMode.Wrap;
        values.verticalOverflow = VerticalWrapMode.Overflow;
        values.supportRichText = false;
        values.raycastTarget = false;
        // No GraphicRaycaster: this read-only panel cannot intercept game/wait input.
    }

    private void RefreshDisplay()
    {
        if (agent == null)
        {
            values.text = "NPC-Debug [F3]\nNpcAgent fehlt.";
            return;
        }
        NpcSimulation model = agent.Simulation;
        string text = $"{agent.NpcName} – {agent.Profession}  [F3]\n";
        if (agent.Clock != null)
            text += $"Tag {agent.Clock.CurrentDay} – {agent.Clock.CurrentHour:00}:{agent.Clock.CurrentMinute:00}\n";
        else
            text += "GameClock nicht zugewiesen.\n";

        if (model == null)
            text += "NPC-Simulation noch nicht bereit – Console prüfen.";
        else
        {
            text += $"Zustand: {StateName(model.State)}\nZiel: {agent.TargetName}\n";
            text += $"Müdigkeit: {model.Fatigue:0.0} / 100\n";
            text += $"Arbeitszeit: {agent.WorkHours}\n";
            text += $"Arbeitsstatus: {(model.State == NpcState.Working ? "Arbeitet" : model.IsWorkTime ? "Arbeitszeit, derzeit abwesend" : "Feierabend")}\n";
            text += $"Ruhebedarf: {(model.NeedsRest ? "Ja" : "Nein")}\n";
            text += $"Gearbeitet: {model.WorkedMinutes / 60d:0.00} h gesamt\n";
            text += $"Geschlafen: {model.SleptMinutes / 60d:0.00} h gesamt";
            if (!agent.enabled) text += "\nNPC-Komponente deaktiviert.";
        }
        values.text = text;
        // Leave room for wrapped targets/status instead of clipping their values.
        panel.sizeDelta = new Vector2(340f, Mathf.Max(310f, values.preferredHeight + 20f));
    }

    private static string StateName(NpcState state)
    {
        switch (state)
        {
            case NpcState.Home: return "Zuhause";
            case NpcState.GoingToWork: return "Geht zur Arbeit";
            case NpcState.Working: return "Arbeitet";
            case NpcState.GoingHome: return "Geht nach Hause";
            case NpcState.Sleeping: return "Schläft";
            default: return state.ToString();
        }
    }
#endif
}
