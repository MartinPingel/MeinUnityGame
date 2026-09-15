using System;
using System.Collections.Generic;
using UnityEngine;
using Village.Npc;
using Village.Storage;

/// <summary>Scene adapter for the beer garden, its reserved places and shared social timeline.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
public sealed class NpcSocialMeetingPlace : MonoBehaviour
{
    [SerializeField] private GameClock clock;
    [SerializeField] private Transform roadEntrance;
    [SerializeField] private Transform aisleJunction;
    [SerializeField] private Transform[] places;
    private readonly NpcSimulationGroup group = new NpcSimulationGroup();
    private readonly List<NpcAgent> agents = new List<NpcAgent>();
    private NpcSocialVenue venue;
    [Header("Bedienung aus dem Tavernenlager")]
    [SerializeField] private NpcAgent innkeeper;
    // Each entry gets its own NpcTavernService (same class, same coworker-sharing as the
    // innkeeper's), so any number of waiters can serve the same guests from the same stock.
    [SerializeField] private NpcAgent[] additionalWaiters;
    [SerializeField] private BuildingWarehouse tavernWarehouse;
    private NpcTavernService service;
    private readonly List<NpcTavernService> waiterServices = new List<NpcTavernService>();
    public NpcTavernService Service => service ?? (service = CreateService());
    private NpcTavernService CreateService()
    {
        if (innkeeper == null || tavernWarehouse == null)
            throw new InvalidOperationException("Beer garden needs its innkeeper and tavern warehouse.");
        var primary = new NpcTavernService(
            (drink, quantity) => tavernWarehouse.Has(drink ? "Getränke" : "Lebensmittel", quantity),
            drink => tavernWarehouse.TryRemove(drink ? "Getränke" : "Lebensmittel", 1),
            guest => Point(ServicePosition(guest.SocialSlot)));
        if (additionalWaiters != null)
            foreach (NpcAgent waiterAgent in additionalWaiters)
            {
                // A null slot keeps its place in the list so later indices (by Array.IndexOf
                // on additionalWaiters) still line up with their own waiterServices entry.
                if (waiterAgent == null) { waiterServices.Add(null); continue; }
                var waiterService = new NpcTavernService(
                    (drink, quantity) => tavernWarehouse.Has(drink ? "Getränke" : "Lebensmittel", quantity),
                    drink => tavernWarehouse.TryRemove(drink ? "Getränke" : "Lebensmittel", 1),
                    guest => Point(ServicePosition(guest.SocialSlot)), true);
                primary.LinkCoworker(waiterService);
                foreach (NpcTavernService existing in waiterServices)
                    if (existing != null) existing.LinkCoworker(waiterService);
                waiterServices.Add(waiterService);
            }
        return primary;
    }
    public string GetServiceTargetName(NpcAgent worker)
    {
        int index = additionalWaiters != null ? Array.IndexOf(additionalWaiters, worker) : -1;
        NpcTavernService assigned = index >= 0 ? waiterServices[index] : Service;
        if (assigned == null || !assigned.HasReservedPortion) return "Taverne (Portion abholen)";
        foreach (NpcAgent agent in agents)
            if (agent != null && agent.Simulation == assigned.Guest)
                return agent.NpcName + " (Biergartenplatz)";
        return "Biergartenplatz";
    }


    public GameClock Clock => clock;
    public NpcSocialVenue Venue
    {
        get
        {
            if (venue == null)
            {
                if (clock == null || roadEntrance == null || aisleJunction == null || places == null || places.Length < 2)
                    throw new InvalidOperationException("Beer garden needs clock, road entrance, aisle and at least two places.");
                if (placeApproaches == null || placeApproaches.Length != places.Length)
                    throw new InvalidOperationException("Every beer garden place needs a clear bank approach.");
                if (serviceOffsets == null || serviceOffsets.Length != places.Length)
                    throw new InvalidOperationException("Every bank place needs a service approach offset.");
                var points = new NpcPoint[places.Length];
                for (int i = 0; i < places.Length; i++)
                {
                    if (places[i] == null) throw new InvalidOperationException("Missing social place.");
                    for (int j = 0; j < i; j++)
                        if (Vector3.Distance(places[i].position, places[j].position) < 0.9f)
                            throw new InvalidOperationException("Social places overlap.");
                    points[i] = Point(places[i].position);
                }
                venue = new NpcSocialVenue(Point(roadEntrance.position), points);
            }
            return venue;
        }
    }

    private void OnEnable()
    {
        if (clock != null) clock.TimeAdvanced += OnTimeAdvanced;
    }
    private void OnDisable()
    {
        if (clock != null) clock.TimeAdvanced -= OnTimeAdvanced;
        foreach (NpcAgent agent in agents)
            if (agent != null && agent.Simulation != null) agent.Simulation.ReleaseSocialPlace();
    }
    private void Start() => AdvanceAll(clock.TotalGameMinutes);

    public void Register(NpcAgent agent)
    {
        if (agent.Clock != clock) throw new InvalidOperationException("Social group and NPC must use the same GameClock.");
        if (agents.Contains(agent)) return;
        int waiterIndex = additionalWaiters != null ? Array.IndexOf(additionalWaiters, agent) : -1;
        Service.Register(agent.Simulation, agent == innkeeper, waiterIndex < 0);
        if (waiterIndex >= 0)
            waiterServices[waiterIndex].Register(agent.Simulation, true, true);
        group.Add(agent.Simulation, () => agent.GameMetresPerMinute);
        group.PrepareServices = UpdateServices;
        agents.Add(agent);
    }
    public void Unregister(NpcAgent agent)
    {
        if (agents.Remove(agent))
        {
            Service.Remove(agent.Simulation);
            int waiterIndex = additionalWaiters != null ? Array.IndexOf(additionalWaiters, agent) : -1;
            if (waiterIndex >= 0) waiterServices[waiterIndex].Remove(agent.Simulation);
            group.Remove(agent.Simulation);
        }
    }
    private void UpdateServices()
    {
        Service.Update();
        foreach (NpcTavernService waiterService in waiterServices) waiterService.Update();
    }
    private void OnTimeAdvanced(double previous, double current) => AdvanceAll(current);
    private void AdvanceAll(double current)
    {
        group.AdvanceTo(current);
        foreach (NpcAgent agent in agents) if (agent != null) agent.ApplySimulationPose();
    }

    private static NpcPoint Point(Vector3 p) => new NpcPoint(p.x, p.y, p.z);

    // Each place has an explicit clear approach beside its table/bench.
    // Reverse the same approach when leaving, including interruptions mid-segment.
    [SerializeField] private Vector3[] placeApproaches;
    [SerializeField] private float[] serviceOffsets;

    private Vector3 ServicePosition(int index)
    {
        Vector3 seat = places[index].position;
        // Approach from the clear outer side of the bench, without overlapping its guest.
        seat.z += serviceOffsets[index];
        return seat;
    }

    private Vector3[] LocalPath(int index, bool serving = false)
    {
        Vector3 seat = serving ? ServicePosition(index) : places[index].position;
        Vector3 approach = placeApproaches[index];
        if (serving) approach.z = seat.z;
        Vector3 junction = aisleJunction.position;
        return new[] { roadEntrance.position, junction,
            new Vector3(approach.x, junction.y, junction.z), approach, seat };
    }

    private bool ExitLocal(Vector3 from, List<Vector3> route)
    {
        for (int i = 0; i < places.Length; i++)
        {
            for (int variant = 0; variant < 2; variant++)
            {
                Vector3[] path = LocalPath(i, variant == 1);
                for (int j = 1; j < path.Length; j++)
                {
                    Vector3 a = path[j - 1], b = path[j], segment = b - a;
                    float t = segment.sqrMagnitude < 0.000001f ? 0f :
                        Mathf.Clamp01(Vector3.Dot(from - a, segment) / segment.sqrMagnitude);
                    if (Vector3.Distance(from, a + t * segment) > 0.01f) continue;
                    for (int k = j - 1; k >= 0; k--) route.Add(path[k]);
                    return true;
                }
            }
        }
        return false;
    }

    public Vector3[] FindRoute(Transform roads, Vector3 from, Vector3 destination, bool socialDestination, bool serviceDestination = false)
    {
        var route = new List<Vector3> { from };
        Vector3 entry = roadEntrance.position;
        bool local = ExitLocal(from, route);
        int serviceIndex = -1;
        bool ownSeat = false;
        if (serviceDestination)
            for (int i = 0; i < places.Length; i++)
            {
                if (Vector3.Distance(ServicePosition(i), destination) < 0.001f) { serviceIndex = i; break; }
                if (Vector3.Distance(places[i].position, destination) < 0.001f)
                { serviceIndex = i; ownSeat = true; break; }
            }
        if ((socialDestination && Vector3.Distance(destination, entry) >= 0.001f) || serviceIndex >= 0)
        {
            if (!local) route.AddRange(RoadRouter.FindRoute(roads, from, entry));
            int index = serviceIndex >= 0 ? serviceIndex :
                Array.FindIndex(places, p => Vector3.Distance(p.position, destination) < 0.001f);
            if (index < 0) throw new InvalidOperationException("Unknown beer garden seat.");
            route.AddRange(LocalPath(index, serviceIndex >= 0 && !ownSeat));
        }
        else route.AddRange(RoadRouter.FindRoute(roads, local ? entry : from, destination));
        // Road and local segments share endpoints.
        for (int i = route.Count - 1; i > 0; i--)
            if (Vector3.Distance(route[i], route[i - 1]) < 0.00001f) route.RemoveAt(i);
        return route.ToArray();
    }
}




