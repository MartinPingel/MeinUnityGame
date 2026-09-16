using System;
using UnityEngine;
using Village.Npc;
using Village.Storage;

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
    [SerializeField] private BuildingWarehouse tavernWarehouse;
    [SerializeField] private Transform well;
    [SerializeField] private string homeLabel = "Zuhause";
    [SerializeField] private string workplaceLabel = "Arbeitsplatz";
    [SerializeField] private WorkDeliveryJob deliveryJob;
    [SerializeField] private BuildingWarehouse workToolWarehouse;
    [SerializeField] private BuildingWarehouse toolSourceWarehouse;
    [SerializeField] private Transform toolPickupPoint;
    [Header("Sozialkontakt")]
    [SerializeField] private NpcSocialMeetingPlace socialMeeting;
    [SerializeField] private NpcSocialSettings social = new NpcSocialSettings();

    [Header("Besuchsrunde (optional, z.B. Pfarrer)")]
    [Tooltip("Wenn gesetzt, ist die Arbeitsstelle dynamisch: erst jeden Stop hier der Reihe " +
        "nach fuer visitStopMinutes besuchen, danach fuer den Rest der Schicht an workplace bleiben.")]
    [SerializeField] private Transform[] visitStops;
    [SerializeField, Min(0f)] private float visitStopMinutes = 10f;

    [Header("Gottesdienst (optional)")]
    [Tooltip("Gemeinsame Kirchenbank-Platzverwaltung und Sonntagszeit. Fuer alle Teilnehmer gleich.")]
    [SerializeField] private NpcChurchAssembly churchAssembly;
    [Tooltip("Nur beim Pfarrer gesetzt: sein fester Platz an der Kanzel statt eines Kirchenbank-Platzes.")]
    [SerializeField] private Transform churchOwnSpot;

    public WorkplaceTools WorkTools => workToolWarehouse != null ? workToolWarehouse.Tools : null;

    [Header("Arbeit und Versorgung - vor Play einstellen")]
    [SerializeField] private WorkSchedule work = new WorkSchedule();
    [SerializeField] private SupplySettings supplies = new SupplySettings();
    [SerializeField, Min(0.1f)] private float walkingMetresPerSecond = 1.5f;
    [SerializeField] private float bodyHeightOffset = 1f;

    private NpcSimulation simulation;

    public string NpcName => npcName;
    public string Profession => profession;
    public GameClock Clock => clock;
    public NpcSimulation Simulation => simulation;
    public double GameMetresPerMinute => walkingMetresPerSecond * (double)clock.RealMinutesPerDay / 24d;
    public string CargoName => deliveryJob != null ? deliveryJob.CargoName : "Ware";
    public string WorkHours => $"{work.startHour:00}:00–{work.endHour:00}:00";
    public string TargetName => simulation == null ? "Nicht bereit" :
        simulation.Target == NpcPlace.Service ? socialMeeting.GetServiceTargetName(this) :
        simulation.Target == NpcPlace.Social ? "Biergarten" :
        simulation.Target == NpcPlace.Church ? "Kirche" :
        simulation.Target == NpcPlace.ToolPickup ? "Marktstand (Werkzeug)" :
        simulation.Target == NpcPlace.Tavern ? "Taverne" :
        simulation.Target == NpcPlace.Well ? "Brunnen" :
        simulation.Target == NpcPlace.Delivery ? deliveryJob.DestinationName :
        simulation.Target == NpcPlace.Pickup ? deliveryJob.PickupName :
        simulation.Target == NpcPlace.Work ? workplaceLabel : homeLabel;

    private void OnEnable()
    {
        try
        {
            if (clock == null || home == null || workplace == null || roadRoot == null ||
                tavern == null || well == null || tavernWarehouse == null)
                throw new InvalidOperationException("NPC needs clock, roads, home, workplace, tavern, tavern warehouse and well references.");
            if (walkingMetresPerSecond <= 0f || float.IsNaN(walkingMetresPerSecond) ||
                float.IsInfinity(walkingMetresPerSecond))
                throw new InvalidOperationException("NPC walking speed must be finite and positive.");

            if (simulation == null)
            {
                Physics.SyncTransforms();
                if (deliveryJob != null) deliveryJob.Validate();
                if (workToolWarehouse != null && workToolWarehouse.Tools == null)
                    throw new InvalidOperationException("Assigned workplace warehouse must enable work tools.");
                if (toolSourceWarehouse != null && (toolPickupPoint == null || workToolWarehouse == null))
                    throw new InvalidOperationException("Tool supply needs a workplace and market road point.");
                if (socialMeeting == null)
                    foreach (NpcSocialMeetingPlace place in FindObjectsByType<NpcSocialMeetingPlace>(
                        FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                        if (place.Clock == clock) { socialMeeting = place; break; }
                INpcVisitRoute visitRoute = visitStops != null && visitStops.Length > 0
                    ? new VisitRoute(visitStops, workplace, visitStopMinutes) : null;
                // Ground-level regardless of the marker's own height (e.g. a raised Kanzel
                // platform), so the point lands exactly on the church's floor-level aisle strip.
                NpcSocialVenue churchVenue = churchAssembly == null ? null
                    : churchOwnSpot != null
                        ? new NpcSocialVenue(churchAssembly.EntrancePoint, new[] { ToChurchFloorPoint(churchOwnSpot) })
                        : churchAssembly.Venue;
                WorkSchedule churchSchedule = churchAssembly != null ? churchAssembly.Schedule : null;
                var navigation = new SceneNavigation(roadRoot, home, workplace, tavern, well,
                    deliveryJob != null ? deliveryJob.DeliveryPoint : null,
                    deliveryJob != null ? deliveryJob.PickupPoint : null, toolPickupPoint, socialMeeting,
                    visitRoute, churchVenue != null);
                simulation = new NpcSimulation(navigation, work, supplies,
                    null, null, // Stock is consumed only by NpcTavernService at handover.
                    deliveryJob, deliveryJob != null ? deliveryJob.Settings : null,
                    workToolWarehouse != null ? new WorkEquipment(workToolWarehouse, toolSourceWarehouse) : null,
                    socialMeeting != null ? socialMeeting.Venue : null,
                    socialMeeting != null ? social : null,
                    visitRoute,
                    churchVenue, churchSchedule);
            }
            // Initialization and re-enabling catch up from the same baseline; nothing is reset.
            if (socialMeeting != null) socialMeeting.Register(this);
            else
            {
                Advance(clock.TotalGameMinutes);
                clock.TimeAdvanced += OnTimeAdvanced;
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"{npcName}: {exception.Message}", this);
            enabled = false;
        }
    }

    private void OnDisable()
    {
        if (socialMeeting != null && simulation != null) socialMeeting.Unregister(this);
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

    public void ApplySimulationPose() => ApplyPose();

    private void ApplyPose()
    {
        Vector3 position = ToVector(simulation.Position), direction = ToVector(simulation.Facing);
        transform.position = position + Vector3.up * bodyHeightOffset;
        if (direction.sqrMagnitude > 0f) transform.rotation = Quaternion.LookRotation(direction);
    }

    private static Vector3 ToVector(NpcPoint point) => new Vector3((float)point.X, (float)point.Y, (float)point.Z);
    private static NpcPoint ToPoint(Vector3 point) => new NpcPoint(point.x, point.y, point.z);
    // The church's own floor-level walkway sits at y=0.04 (matching every other outdoor path
    // point); a spot marker positioned above ground (e.g. standing on a raised Kanzel) is
    // projected back down onto that walkway, so it lands exactly on the routable aisle strip.
    private const float ChurchFloorHeight = 0.04f;
    private static NpcPoint ToChurchFloorPoint(Transform t) =>
        new NpcPoint(t.position.x, ChurchFloorHeight, t.position.z);

    private sealed class WorkEquipment : INpcToolSupply
    {
        private readonly BuildingWarehouse destination, source;
        private readonly WorkplaceTools tools;
        public WorkEquipment(BuildingWarehouse destination, BuildingWarehouse source)
        {
            this.destination = destination;
            this.source = source;
            tools = destination.Tools;
        }
        public bool CanWork => tools.HasUsableTool || !tools.HasEverHadTool;
        public double MinutesUntilBreak => tools.MinutesUntilBreak;
        public void Wear(double workMinutes) => tools.Wear(workMinutes);
        public bool NeedsDelivery => !tools.HasUsableTool && source != null &&
            source != destination && source.Has(WorkplaceTools.GoodsType, 1);
        public bool TryCollect() => NeedsDelivery && source.TryRemove(WorkplaceTools.GoodsType, 1);
        public bool TryDeposit()
        {
            try { destination.Add(WorkplaceTools.GoodsType, 1); return true; }
            catch (OverflowException) { return false; }
        }
    }

    /// <summary>Timed sequence of stops (e.g. a visiting round), then a steady base point for
    /// the rest of the shift. Reused as-is by NpcSimulation's opt-in INpcVisitRoute hook.</summary>
    private sealed class VisitRoute : INpcVisitRoute
    {
        private readonly NpcPoint[] stops;
        private readonly NpcPoint basePoint;
        private readonly double stopMinutes;
        private int index; // stops.Length once settled at basePoint for the rest of the shift.
        private double remaining = double.PositiveInfinity;

        public VisitRoute(Transform[] stopTransforms, Transform basePlace, float stopMinutes)
        {
            stops = new NpcPoint[stopTransforms.Length];
            for (int i = 0; i < stopTransforms.Length; i++)
            {
                if (stopTransforms[i] == null) throw new InvalidOperationException("Missing visit stop.");
                stops[i] = ToPoint(stopTransforms[i].position);
            }
            basePoint = ToPoint(basePlace.position);
            this.stopMinutes = stopMinutes;
            index = stops.Length; // Settled at basePoint until the first shift starts.
        }

        public NpcPoint CurrentStop => index < stops.Length ? stops[index] : basePoint;
        public double MinutesUntilNextStop => index < stops.Length ? remaining : double.PositiveInfinity;

        public void Consume(double minutes)
        {
            if (index >= stops.Length) return;
            remaining -= minutes;
            if (remaining <= 1e-9)
            {
                index++;
                remaining = stopMinutes;
            }
        }

        public void ResetForNewShift()
        {
            index = 0;
            remaining = stops.Length > 0 ? stopMinutes : double.PositiveInfinity;
        }
    }

    /// <summary>Read-only adapter to the unchanged village road router.</summary>
    private sealed class SceneNavigation : INpcNavigation, INpcSocialNavigation, INpcServiceNavigation,
        INpcRestockNavigation, INpcChurchNavigation
    {
        private readonly Transform roads;
        private readonly NpcPoint[] places;
        private readonly NpcSocialMeetingPlace meeting;
        private readonly INpcVisitRoute visitRoute;
        public void SetRestockPickup(NpcPoint point) => places[(int)NpcPlace.Pickup] = point;
        public void SetServiceDestination(NpcPoint point) => places[(int)NpcPlace.Service] = point;
        public void SetSocialDestination(NpcPoint point) => places[(int)NpcPlace.Social] = point;
        public void SetChurchDestination(NpcPoint point) => places[(int)NpcPlace.Church] = point;

        public SceneNavigation(Transform roads, Transform home, Transform work, Transform tavern, Transform well,
            Transform deliveryPoint = null, Transform pickupPoint = null, Transform toolPoint = null,
            NpcSocialMeetingPlace meeting = null, INpcVisitRoute visitRoute = null, bool hasChurch = false)
        {
            this.roads = roads;
            this.meeting = meeting;
            this.visitRoute = visitRoute;
            places = new[] { ToPoint(home.position), ToPoint(work.position),
                ToPoint(tavern.position), ToPoint(well.position),
                ToPoint(deliveryPoint != null ? deliveryPoint.position : work.position),
                ToPoint(pickupPoint != null ? pickupPoint.position : work.position),
                ToPoint(toolPoint != null ? toolPoint.position : work.position),
                meeting != null ? meeting.Venue.Entrance : ToPoint(tavern.position), ToPoint(tavern.position),
                ToPoint(work.position) };
            // Fail at initialization if any required place is disconnected.
            foreach (NpcPlace place in Enum.GetValues(typeof(NpcPlace)))
            {
                if (place == NpcPlace.Delivery && deliveryPoint == null) continue;
                if (place == NpcPlace.Pickup && pickupPoint == null) continue;
                if (place == NpcPlace.ToolPickup && toolPoint == null) continue;
                if (place == NpcPlace.Social && meeting == null) continue;
                if (place == NpcPlace.Church && !hasChurch) continue;
                FindRoute(places[0], place);
            }
        }

        // Opt-in: a visit route makes the work destination dynamic; every other NPC keeps
        // its single static workplace point from the places array, unchanged.
        public NpcPoint GetPlace(NpcPlace place) =>
            place == NpcPlace.Work && visitRoute != null ? visitRoute.CurrentStop : places[(int)place];

        public NpcPoint[] FindRoute(NpcPoint from, NpcPlace destination)
        {
            Vector3[] path = meeting != null
                ? meeting.FindRoute(roads, ToVector(from), ToVector(GetPlace(destination)), destination == NpcPlace.Social, destination == NpcPlace.Service)
                : RoadRouter.FindRoute(roads, ToVector(from), ToVector(GetPlace(destination)));
            var result = new NpcPoint[path.Length];
            for (int i = 0; i < path.Length; i++) result[i] = ToPoint(path[i]);
            return result;
        }
    }
}



