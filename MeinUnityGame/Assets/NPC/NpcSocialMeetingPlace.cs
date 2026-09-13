using System;
using System.Collections.Generic;
using UnityEngine;
using Village.Npc;

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

    public GameClock Clock => clock;
    public NpcSocialVenue Venue
    {
        get
        {
            if (venue == null)
            {
                if (clock == null || roadEntrance == null || aisleJunction == null || places == null || places.Length < 2)
                    throw new InvalidOperationException("Beer garden needs clock, road entrance, aisle and at least two places.");
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
        group.Add(agent.Simulation, () => agent.GameMetresPerMinute);
        agents.Add(agent);
    }
    public void Unregister(NpcAgent agent)
    {
        if (agents.Remove(agent)) group.Remove(agent.Simulation);
    }
    private void OnTimeAdvanced(double previous, double current) => AdvanceAll(current);
    private void AdvanceAll(double current)
    {
        group.AdvanceTo(current);
        foreach (NpcAgent agent in agents) if (agent != null) agent.ApplySimulationPose();
    }

    private static NpcPoint Point(Vector3 p) => new NpcPoint(p.x, p.y, p.z);

    // Local aisle routing is separate from the unmodified village road graph.
    // All assigned standing places lie on this clear aisle in front of the tables.
    private bool OnAisle(Vector3 p)
    {
        Vector3 a = roadEntrance.position, j = aisleJunction.position;
        float minX = j.x, maxX = j.x;
        foreach (Transform place in places) { minX = Mathf.Min(minX, place.position.x); maxX = Mathf.Max(maxX, place.position.x); }
        return Mathf.Abs(p.y - j.y) < 0.01f &&
            ((Mathf.Abs(p.x - j.x) < 0.01f && p.z >= Mathf.Min(a.z, j.z) - 0.01f && p.z <= Mathf.Max(a.z, j.z) + 0.01f) ||
             (Mathf.Abs(p.z - j.z) < 0.01f && p.x >= minX - 0.01f && p.x <= maxX + 0.01f));
    }
    public Vector3[] FindRoute(Transform roads, Vector3 from, Vector3 destination, bool socialDestination)
    {
        var route = new List<Vector3> { from };
        Vector3 entry = roadEntrance.position, junction = aisleJunction.position;
        bool local = OnAisle(from);
        bool queueAtEntrance = Vector3.Distance(destination, entry) < 0.001f;
        if (socialDestination && !queueAtEntrance)
        {
            if (!local) route.AddRange(RoadRouter.FindRoute(roads, from, entry));
            if (!local || Mathf.Abs(from.z - junction.z) > 0.01f) route.Add(junction);
            route.Add(destination);
        }
        else if (local)
        {
            if (Mathf.Abs(from.z - junction.z) < 0.01f) route.Add(junction);
            route.Add(entry);
            route.AddRange(RoadRouter.FindRoute(roads, entry, destination));
        }
        else route.AddRange(RoadRouter.FindRoute(roads, from, destination));
        // Road and local segments share endpoints.
        for (int i = route.Count - 1; i > 0; i--)
            if (Vector3.Distance(route[i], route[i - 1]) < 0.00001f) route.RemoveAt(i);
        return route.ToArray();
    }
}
