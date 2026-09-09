using System.Collections.Generic;
using UnityEngine;

/// <summary>One clock-driven lighting group per village, district or castle.</summary>
[DisallowMultipleComponent]
public sealed class VillageLighting : MonoBehaviour
{
    [SerializeField] private GameClock clock;
    [SerializeField, Range(0, 23)] private int switchOnHour = 20;
    [SerializeField, Range(0, 23)] private int switchOffHour = 7;
    private readonly HashSet<NightLantern> lanterns = new HashSet<NightLantern>();

    private void OnEnable()
    {
        if (clock == null)
        {
            Debug.LogError("VillageLighting needs the scene's GameClock.", this);
            enabled = false;
            return;
        }
        clock.TimeAdvanced += OnTimeAdvanced;
        foreach (NightLantern lantern in GetComponentsInChildren<NightLantern>(true))
            if (lantern.isActiveAndEnabled && lantern.GetComponentInParent<VillageLighting>() == this)
                Register(lantern);
        ApplyTime(clock.TotalGameMinutes);
    }

    private void OnDisable()
    {
        if (clock != null) clock.TimeAdvanced -= OnTimeAdvanced;
        foreach (NightLantern lantern in lanterns)
            if (lantern != null) lantern.SetLit(false);
    }

    public void Register(NightLantern lantern)
    {
        lanterns.Add(lantern);
        lantern.SetLit(isActiveAndEnabled && clock != null && IsLitAt(clock.TotalGameMinutes));
    }

    public void Unregister(NightLantern lantern)
    {
        lanterns.Remove(lantern);
        if (lantern != null) lantern.SetLit(false);
    }

    private void OnTimeAdvanced(double previous, double current) => ApplyTime(current);

    private bool IsLitAt(double totalMinutes)
    {
        double hour = totalMinutes % 1440d / 60d;
        if (switchOnHour == switchOffHour) return false;
        return switchOnHour > switchOffHour
            ? hour >= switchOnHour || hour < switchOffHour
            : hour >= switchOnHour && hour < switchOffHour;
    }

    private void ApplyTime(double totalMinutes)
    {
        bool lit = IsLitAt(totalMinutes);
        foreach (NightLantern lantern in lanterns)
            if (lantern != null) lantern.SetLit(lit);
    }
}
