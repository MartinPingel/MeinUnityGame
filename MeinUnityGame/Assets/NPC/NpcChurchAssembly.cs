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
    // startHour is the mandatory travel/gathering start (attendees must already be heading to
    // church from this point on, ahead of normal work/tavern/social priorities); the service
    // itself still only runs 10:00-11:00 - endHour is unchanged.
    [SerializeField] private WorkSchedule schedule = new WorkSchedule
        { startHour = 9, endHour = 11, workDaysMask = 0b1000000 }; // Sunday only

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
