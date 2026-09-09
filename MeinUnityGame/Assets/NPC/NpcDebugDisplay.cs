using UnityEngine;
using UnityEngine.InputSystem;
using Village.Npc;

/// <summary>Development-only NPC inspector overlay. F3 toggles it; never drawn in release builds.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NpcAgent))]
public sealed class NpcDebugDisplay : MonoBehaviour
{
    [SerializeField] private bool showDebug = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private NpcAgent agent;
    private GUIStyle style;

    private void Awake() { agent = GetComponent<NpcAgent>(); }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
            showDebug = !showDebug;
    }

    private void OnGUI()
    {
        if (!showDebug || agent == null) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true };
            style.normal.textColor = Color.white;
        }
        NpcSimulation model = agent.Simulation;
        string text = $"{agent.NpcName} – {agent.Profession}  [F3]\n";
        if (model == null || !agent.enabled)
            text += "NPC nicht bereit – Console prüfen.";
        else
        {
            text += $"Tag {agent.Clock.CurrentDay} – {agent.Clock.CurrentHour:00}:{agent.Clock.CurrentMinute:00}\n";
            text += $"Zustand: {StateName(model.State)}\nZiel: {agent.TargetName}\n";
            text += $"Müdigkeit: {model.Fatigue:0.0} / 100\n";
            text += $"Arbeitszeit: {agent.WorkHours}\n";
            text += $"Arbeitsstatus: {(model.State == NpcState.Working ? "Arbeitet" : model.IsWorkTime ? "Arbeitszeit, derzeit abwesend" : "Feierabend")}\n";
            text += $"Ruhebedarf: {(model.NeedsRest ? "Ja" : "Nein")}\n";
            text += $"Gearbeitet: {model.WorkedMinutes / 60d:0.00} h gesamt\n";
            text += $"Geschlafen: {model.SleptMinutes / 60d:0.00} h gesamt";
        }
        GUI.Box(new Rect(12f, 76f, 340f, 310f), GUIContent.none);
        GUI.Label(new Rect(24f, 86f, 316f, 290f), text, style);
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
