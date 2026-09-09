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
    [SerializeField] private Transform tavern;
    [SerializeField] private Transform well;
    [SerializeField] private string homeLabel = "Zuhause";
    [SerializeField] private string workplaceLabel = "Arbeitsplatz";

    [Header("Arbeit, Energie und Versorgung - vor Play einstellen")]
    [SerializeField] private WorkSchedule work = new WorkSchedule();
    [SerializeField] private FatigueSettings fatigue = new FatigueSettings();
    [SerializeField] private SupplySettings supplies = new SupplySettings();
    [SerializeField, Min(0.1f)] private float walkingMetresPerSecond = 1.5f;
    [SerializeField] private float bodyHeightOffset = 1f;

    private NpcSimulation simulation;

    public string NpcName => npcName;
    public string Profession => profession;
    public GameClock Clock => clock;
    public NpcSimulation Simulation => simulation;
    public string WorkHours => $"{work.startHour:00}:00–{work.endHour:00}:00";
    public string TargetName => simulation == null ? "Nicht bereit" :
        simulation.Target == NpcPlace.Tavern ? "Taverne" :
        simulation.Target == NpcPlace.Well ? "Brunnen" :
        simulation.Target == NpcPlace.Work ? workplaceLabel : homeLabel;

    private void OnEnable()
    {
        try
        {
            if (clock == null || home == null || workplace == null || roadRoot == null ||
                tavern == null || well == null)
                throw new InvalidOperationException("NPC needs clock, roads, home, workplace, tavern and well references.");
            if (walkingMetresPerSecond <= 0f || float.IsNaN(walkingMetresPerSecond) ||
                float.IsInfinity(walkingMetresPerSecond))
                throw new InvalidOperationException("NPC walking speed must be finite and positive.");

            if (simulation == null)
            {
                Physics.SyncTransforms();
                var navigation = new SceneNavigation(roadRoot, home, workplace, tavern, well);
                simulation = new NpcSimulation(navigation, work, fatigue, supplies);
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
        Vector3 position = ToVector(simulation.Position), direction = ToVector(simulation.Facing);
        transform.position = position + Vector3.up * bodyHeightOffset;
        if (direction.sqrMagnitude > 0f) transform.rotation = Quaternion.LookRotation(direction);
    }

    private static Vector3 ToVector(NpcPoint point) => new Vector3((float)point.X, (float)point.Y, (float)point.Z);
    private static NpcPoint ToPoint(Vector3 point) => new NpcPoint(point.x, point.y, point.z);

    /// <summary>Read-only adapter to the unchanged village road router.</summary>
    private sealed class SceneNavigation : INpcNavigation
    {
        private readonly Transform roads;
        private readonly NpcPoint[] places;

        public SceneNavigation(Transform roads, Transform home, Transform work, Transform tavern, Transform well)
        {
            this.roads = roads;
            places = new[] { ToPoint(home.position), ToPoint(work.position),
                ToPoint(tavern.position), ToPoint(well.position) };
            // Fail at initialization if any required place is disconnected.
            foreach (NpcPlace place in Enum.GetValues(typeof(NpcPlace)))
                FindRoute(places[0], place);
        }

        public NpcPoint GetPlace(NpcPlace place) => places[(int)place];

        public NpcPoint[] FindRoute(NpcPoint from, NpcPlace destination)
        {
            Vector3[] path = RoadRouter.FindRoute(roads, ToVector(from), ToVector(GetPlace(destination)));
            var result = new NpcPoint[path.Length];
            for (int i = 0; i < path.Length; i++) result[i] = ToPoint(path[i]);
            return result;
        }
    }
}
