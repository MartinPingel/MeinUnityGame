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
        panel.sizeDelta = new Vector2(340f, Mathf.Max(360f, values.preferredHeight + 24f));
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
