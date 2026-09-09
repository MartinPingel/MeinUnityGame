using System;
using UnityEngine;
using Village.Npc;

/// <summary>Reusable scene adapter: identity, locations, road travel and a work/rest simulation.</summary>
[DisallowMultipleComponent]
public sealed class NpcAgent : MonoBehaviour
{
    [Header("Identitaet")]
    [SerializeField] private string npcName = "Hans";
    [SerializeField] private string profession = "Bauer";

    [Header("Bestehende Szene")]
    [SerializeField] private GameClock clock;
    [SerializeField] private Transform roadRoot;
    [SerializeField] private Transform home;
    [SerializeField] private Transform workplace;
    [SerializeField] private string homeLabel = "Zuhause";
    [SerializeField] private string workplaceLabel = "Arbeitsplatz";

    [Header("Arbeit und Muedigkeit - vor Play einstellen")]
    [SerializeField] private WorkSchedule work = new WorkSchedule();
    [SerializeField] private FatigueSettings fatigue = new FatigueSettings();
    [SerializeField, Min(0.1f)] private float walkingMetresPerSecond = 1.5f;
    [SerializeField] private float bodyHeightOffset = 1f;

    private Vector3[] route;
    private NpcSimulation simulation;

    public string NpcName => npcName;
    public string Profession => profession;
    public GameClock Clock => clock;
    public NpcSimulation Simulation => simulation;
    public string WorkHours => $"{work.startHour:00}:00–{work.endHour:00}:00";
    public string TargetName => simulation == null ? "Nicht bereit" :
        simulation.State == NpcState.Working || simulation.State == NpcState.GoingToWork
            ? workplaceLabel : homeLabel;

    private void OnEnable()
    {
        try
        {
            if (clock == null || home == null || workplace == null || roadRoot == null)
                throw new InvalidOperationException("NPC needs clock, road network, home and workplace references.");
            if (walkingMetresPerSecond <= 0f || float.IsNaN(walkingMetresPerSecond) ||
                float.IsInfinity(walkingMetresPerSecond))
                throw new InvalidOperationException("NPC walking speed must be finite and positive.");

            if (simulation == null)
            {
                Physics.SyncTransforms();
                route = RoadRouter.FindRoute(roadRoot, home.position, workplace.position);
                double length = 0d;
                for (int i = 1; i < route.Length; i++) length += Vector3.Distance(route[i - 1], route[i]);
                simulation = new NpcSimulation(length, work, fatigue);
            }
            // Initialization and re-enabling catch up from the same baseline; nothing is reset.
            Advance(clock.TotalGameMinutes);
            clock.TimeAdvanced += OnTimeAdvanced;
        }
        catch (Exception exception)
        {
            Debug.LogError($"{npcName}: {exception.Message}", this);
            enabled = false;
        }
    }

    private void OnDisable()
    {
        if (clock != null) clock.TimeAdvanced -= OnTimeAdvanced;
    }

    private void OnTimeAdvanced(double previous, double current)
    {
        Advance(current);
    }

    private void Advance(double targetMinutes)
    {
        // Match ordinary movement speed at the current clock rate. A wait jump consumes
        // the same travel time as ordinary ticking; it never teleports straight across roads.
        double metresPerGameMinute = walkingMetresPerSecond * (double)clock.RealMinutesPerDay / 24d;
        simulation.AdvanceTo(targetMinutes, metresPerGameMinute);
        ApplyPose();
    }

    private void ApplyPose()
    {
        double remaining = simulation.DistanceFromHome;
        Vector3 position = route[0], direction = Vector3.forward;
        for (int i = 1; i < route.Length; i++)
        {
            Vector3 segment = route[i] - route[i - 1];
            double length = segment.magnitude;
            direction = segment.normalized;
            if (remaining <= length || i == route.Length - 1)
            {
                position = Vector3.Lerp(route[i - 1], route[i], (float)(remaining / length));
                break;
            }
            remaining -= length;
        }
        if (simulation.State == NpcState.GoingHome) direction = -direction;
        transform.position = position + Vector3.up * bodyHeightOffset;
        if (direction.sqrMagnitude > 0f) transform.rotation = Quaternion.LookRotation(direction);
    }
}
