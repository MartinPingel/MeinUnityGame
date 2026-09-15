using System;
using UnityEngine;
using Village.Npc;

/// <summary>Scene adapter for the church's Sunday service: the shared 20-seat reservation
/// pool (reusing the same generic NpcSocialVenue as the beer garden) and the single service
/// schedule every attendee's NpcSimulation references. No service/Sunday behavior lives here -
/// this only exposes the seats, entrance and schedule for NpcAgent to wire in.</summary>
[DisallowMultipleComponent]
public sealed class NpcChurchAssembly : MonoBehaviour
{
    [SerializeField] private Transform entrance;
    [SerializeField] private Transform[] seats;
    [SerializeField] private WorkSchedule schedule = new WorkSchedule
        { startHour = 10, endHour = 11, workDaysMask = 0b1000000 }; // Sunday only

    private NpcSocialVenue venue;

    public WorkSchedule Schedule => schedule;
    public NpcPoint EntrancePoint => Point(entrance.position);

    public NpcSocialVenue Venue
    {
        get
        {
            if (venue == null)
            {
                if (entrance == null || seats == null || seats.Length < 1)
                    throw new InvalidOperationException("Church assembly needs an entrance and at least one seat.");
                var points = new NpcPoint[seats.Length];
                for (int i = 0; i < seats.Length; i++)
                {
                    if (seats[i] == null) throw new InvalidOperationException("Missing church seat.");
                    points[i] = Point(seats[i].position);
                }
                venue = new NpcSocialVenue(EntrancePoint, points);
            }
            return venue;
        }
    }

    private static NpcPoint Point(Vector3 p) => new NpcPoint(p.x, p.y, p.z);
}
