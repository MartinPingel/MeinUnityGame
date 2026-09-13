using System;
using System.Collections.Generic;

namespace Village.Npc
{
    /// <summary>Exclusive reservations, including journeys. Only actual arrivals count as social participants.</summary>
    public sealed class NpcSocialVenue
    {
        private readonly NpcPoint[] places;
        private readonly Dictionary<NpcSimulation, int> reservations = new Dictionary<NpcSimulation, int>();
        public NpcPoint Entrance { get; }
        public NpcSocialVenue(NpcPoint entrance, NpcPoint[] places)
        {
            if (places == null || places.Length < 2) throw new ArgumentException("Social venue needs at least two places.");
            Entrance = entrance;
            this.places = (NpcPoint[])places.Clone();
        }
        public int Reserve(NpcSimulation npc)
        {
            if (reservations.TryGetValue(npc, out int place)) return place;
            for (int i = 0; i < places.Length; i++)
                if (!reservations.ContainsValue(i)) { reservations.Add(npc, i); return i; }
            return -1;
        }
        public NpcPoint GetPlace(int index) => index < 0 ? Entrance : places[index];
        public void Release(NpcSimulation npc) => reservations.Remove(npc);
    }

    public interface INpcSocialNavigation
    {
        void SetSocialDestination(NpcPoint point);
    }
}
