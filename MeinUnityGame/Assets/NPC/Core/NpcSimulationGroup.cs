using System;
using System.Collections.Generic;

namespace Village.Npc
{
    /// <summary>
    /// Social participants must share a timeline: plan all NPCs at the same instant,
    /// advance only to the earliest event, then reevaluate attendance.
    /// Existing per-NPC event equations and transactions are reused.
    /// </summary>
    public sealed class NpcSimulationGroup
    {
        private sealed class Member
        {
            public NpcSimulation Npc;
            public Func<double> Speed;
            public NpcSimulation.StepFrame Frame;
        }
        private readonly List<Member> members = new List<Member>();
        public Action PrepareServices { get; set; }
        public double TotalMinutes { get; private set; }
        public void Add(NpcSimulation npc, Func<double> speed)
        {
            foreach (Member m in members) if (m.Npc == npc) return;
            if (npc == null || speed == null) throw new ArgumentNullException();
            if (npc.TotalMinutes > TotalMinutes) throw new ArgumentException("NPC is ahead of group time.");
            npc.SetSocialContact(false);
            if (npc.TotalMinutes < TotalMinutes) npc.AdvanceTo(TotalMinutes, speed());
            members.Add(new Member { Npc = npc, Speed = speed });
        }
        public void Remove(NpcSimulation npc)
        {
            members.RemoveAll(m => m.Npc == npc);
            npc.ReleaseSocialPlace();
        }
        private void Prepare()
        {
            foreach (Member m in members) m.Npc.PrepareGroupStep(m.Speed());
            if (PrepareServices != null)
            {
                PrepareServices();
                // A handover may resolve a need; plan every NPC again before advancing time.
                foreach (Member m in members) m.Npc.PrepareGroupStep(m.Speed());
            }
            // Freeze attendance for this interval. Arrivals and departures are event boundaries.
            foreach (Member m in members)
            {
                int participants = 0;
                foreach (Member other in members)
                    if (other.Npc.SocialVenue == m.Npc.SocialVenue && other.Npc.IsPresentTavernGuest) participants++;
                m.Npc.SetSocialContact(participants >= 2);
            }
        }
        public void AdvanceTo(double target)
        {
            if (double.IsNaN(target) || double.IsInfinity(target) || target < TotalMinutes)
                throw new ArgumentOutOfRangeException(nameof(target));
            while (target - TotalMinutes > 1e-9)
            {
                Prepare();
                double step = target - TotalMinutes;
                foreach (Member m in members)
                {
                    double speed = m.Speed();
                    if (double.IsNaN(speed) || double.IsInfinity(speed) || speed <= 0)
                        throw new InvalidOperationException("NPC speed must be finite and positive.");
                    m.Frame = m.Npc.PlanStep(target, speed);
                    step = Math.Min(step, m.Frame.Step);
                }
                if (step <= 0 || TotalMinutes + step == TotalMinutes) throw new InvalidOperationException("Group could not advance.");
                foreach (Member m in members) m.Npc.ApplyStep(m.Frame, step);
                TotalMinutes += step;
            }
            TotalMinutes = target;
            Prepare();
        }
    }
}

