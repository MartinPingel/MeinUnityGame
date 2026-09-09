using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using Village.Npc;

/// <summary>Development-only, scene-bound Hans display. Visibility follows the Hans observer camera.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NpcAgent))]
public sealed class NpcDebugDisplay : MonoBehaviour
{
    [SerializeField] private HansObserverCamera observerCamera;
    [SerializeField] private RectTransform panel;
    [SerializeField] private Text values;

    private void OnEnable()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        agent = GetComponent<NpcAgent>();
        if (panel == null || values == null || !values.transform.IsChildOf(panel))
        {
            Debug.LogError("Hans Debug: Panel/Text in SampleScene nicht korrekt zugewiesen.", this);
            if (panel != null) panel.gameObject.SetActive(false);
            enabled = false;
            return;
        }
        // Explicit runtime font, independent of GUI skins or TMP resource imports.
        values.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RefreshDisplay();
        panel.gameObject.SetActive(IsObserved);
#else
        if (panel != null) panel.gameObject.SetActive(false);
#endif
    }

    private void OnDisable()
    {
        if (panel != null) panel.gameObject.SetActive(false);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private NpcAgent agent;
    private static readonly CultureInfo GermanNumbers = CultureInfo.GetCultureInfo("de-DE");

    private bool IsObserved => observerCamera != null && observerCamera.IsObserving(transform);

    private void LateUpdate()
    {
        bool visible = IsObserved;
        if (panel.gameObject.activeSelf != visible)
            panel.gameObject.SetActive(visible);
        if (!visible) return;
        // Draw this panel last on the shared canvas, including after WaitMenu.Awake.
        panel.SetAsLastSibling();
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        if (agent == null)
        {
            values.text = "NPC-Debug\nNpcAgent fehlt.";
            UpdateLayout();
            return;
        }
        NpcSimulation model = agent.Simulation;
        string text = $"{agent.NpcName} – {agent.Profession}\n";
        if (agent.Clock != null)
            text += $"Tag {agent.Clock.CurrentDay} – {agent.Clock.CurrentHour:00}:{agent.Clock.CurrentMinute:00}\n";
        else
            text += "GameClock nicht zugewiesen.\n";

        if (model == null)
            text += "NPC-Simulation noch nicht bereit – Console prüfen.";
        else
        {
            text += $"Zustand: {StateName(model.State)}\nZiel: {agent.TargetName}\n";
            text += $"Sättigung: {model.Satiation.ToString("0.0", GermanNumbers)} / 100\n";
            text += $"Flüssigkeit: {model.Hydration.ToString("0.0", GermanNumbers)} / 100\n";
            text += $"Energie: {model.Energy.ToString("0.0", GermanNumbers)} / 100\n";
            text += $"Arbeitszeit: {agent.WorkHours}\n";
            text += $"Arbeitsstatus: {(model.State == NpcState.Working ? "Arbeitet" : model.IsWorkTime ? "Arbeitszeit, derzeit abwesend" : "Feierabend")}\n";
            text += $"Ruhebedarf: {(model.NeedsRest ? "Ja" : "Nein")}\n";
            text += $"Gearbeitet: {model.WorkedMinutes / 60d:0.00} h gesamt\n";
            text += $"Geschlafen: {model.SleptMinutes / 60d:0.00} h gesamt";
            if (!agent.enabled) text += "\nNPC-Komponente deaktiviert.";
        }
        values.text = text;
        UpdateLayout();
    }

    private void UpdateLayout()
    {
        RectTransform parent = panel.parent as RectTransform;
        if (parent == null || parent.rect.width <= 0f || parent.rect.height <= 0f)
            return; // Canvas may not have its screen size yet during OnEnable.

        const float width = 340f;
        const float padding = 12f;
        Vector2 topLeft = new Vector2(0f, 1f);
        panel.anchorMin = panel.anchorMax = topLeft;
        panel.pivot = topLeft;
        panel.localRotation = Quaternion.identity;

        RectTransform textRect = values.rectTransform;
        textRect.anchorMin = textRect.anchorMax = topLeft;
        textRect.pivot = topLeft;
        textRect.localRotation = Quaternion.identity;
        textRect.localScale = Vector3.one;
        textRect.anchoredPosition3D = new Vector3(padding, -padding, 0f);
        textRect.sizeDelta = new Vector2(width - padding * 2f, 1f);
        values.alignment = TextAnchor.UpperLeft;
        values.horizontalOverflow = HorizontalWrapMode.Wrap;
        values.verticalOverflow = VerticalWrapMode.Truncate;

        float height = Mathf.Max(360f, Mathf.Ceil(values.preferredHeight) + padding * 2f);
        panel.sizeDelta = new Vector2(width, height);
        textRect.sizeDelta = new Vector2(width - padding * 2f, height - padding * 2f);

        // Measure in the parent canvas' units, not Screen pixels. Fit only this
        // panel, preserving the shared CanvasScaler and all other UI layouts.
        float margin = Mathf.Min(12f, Mathf.Min(parent.rect.width, parent.rect.height) * 0.05f);
        float scale = Mathf.Min(1f, Mathf.Min(
            (parent.rect.width - margin * 2f) / width,
            (parent.rect.height - margin * 2f) / height));
        panel.localScale = Vector3.one * scale;
        panel.anchoredPosition3D = new Vector3(margin, -margin, 0f);
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
            case NpcState.GoingToEat: return "Geht zur Taverne";
            case NpcState.Eating: return "Isst";
            case NpcState.GoingToDrink: return "Geht zum Brunnen";
            case NpcState.Drinking: return "Trinkt";
            default: return state.ToString();
        }
    }
#endif
}
