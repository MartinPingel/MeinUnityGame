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
                if (placeApproaches == null || placeApproaches.Length != places.Length)
                    throw new InvalidOperationException("Every beer garden place needs a clear bank approach.");
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

    // Each place has an explicit clear approach beside its table/bench.
    // Reverse the same approach when leaving, including interruptions mid-segment.
    [SerializeField] private Vector3[] placeApproaches;

    private Vector3[] LocalPath(int index)
    {
        Vector3 seat = places[index].position;
        Vector3 approach = placeApproaches[index];
        Vector3 junction = aisleJunction.position;
        return new[] { roadEntrance.position, junction,
            new Vector3(approach.x, junction.y, junction.z), approach, seat };
    }

    private bool ExitLocal(Vector3 from, List<Vector3> route)
    {
        for (int i = 0; i < places.Length; i++)
        {
            Vector3[] path = LocalPath(i);
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
        return false;
    }

    public Vector3[] FindRoute(Transform roads, Vector3 from, Vector3 destination, bool socialDestination)
    {
        var route = new List<Vector3> { from };
        Vector3 entry = roadEntrance.position;
        bool local = ExitLocal(from, route);
        if (socialDestination && Vector3.Distance(destination, entry) >= 0.001f)
        {
            if (!local) route.AddRange(RoadRouter.FindRoute(roads, from, entry));
            int index = Array.FindIndex(places, p => Vector3.Distance(p.position, destination) < 0.001f);
            if (index < 0) throw new InvalidOperationException("Unknown beer garden seat.");
            route.AddRange(LocalPath(index));
        }
        else route.AddRange(RoadRouter.FindRoute(roads, local ? entry : from, destination));
        // Road and local segments share endpoints.
        for (int i = route.Count - 1; i > 0; i--)
            if (Vector3.Distance(route[i], route[i - 1]) < 0.00001f) route.RemoveAt(i);
        return route.ToArray();
    }
}

