using System;
using System.Collections.Generic;

namespace Village.Npc
{
    /// <summary>One real tavern portion, reserved at pickup and consumed only at a seated guest.</summary>
    public sealed class NpcTavernService
    {
        private readonly List<NpcSimulation> guests = new List<NpcSimulation>();
        private readonly Func<bool, int, bool> hasStock;
        private readonly Func<bool, bool> consume;
        private readonly Func<NpcSimulation, NpcPoint> servicePoint;
        private NpcSimulation waiter, guest;
        private bool drink, pickedUp;
        private NpcPoint destination;
        private int nextGuest;
        public NpcSimulation Guest => guest;
        public bool HasReservedPortion => guest != null && pickedUp;
        public bool IsWaiter(NpcSimulation npc) => npc == waiter;

        public NpcTavernService(Func<bool, int, bool> hasStock, Func<bool, bool> consume,
            Func<NpcSimulation, NpcPoint> servicePoint)
        {
            this.hasStock = hasStock ?? throw new ArgumentNullException(nameof(hasStock));
            this.consume = consume ?? throw new ArgumentNullException(nameof(consume));
            this.servicePoint = servicePoint ?? throw new ArgumentNullException(nameof(servicePoint));
        }
        public void Register(NpcSimulation npc, bool isWaiter)
        {
            if (!guests.Contains(npc)) guests.Add(npc);
            npc.TavernService = this;
            if (isWaiter)
            {
                if (waiter != null && waiter != npc) throw new InvalidOperationException("Tavern already has a waiter.");
                waiter = npc;
            }
        }
        public void Remove(NpcSimulation npc)
        {
            guests.Remove(npc);
            if (npc == guest || npc == waiter) Cancel();
            if (npc == waiter) waiter = null;
            npc.TavernService = null;
        }
        // Ordinary tavern consumers cannot take the portion already carried to a guest.
        // The warehouse total is decremented exactly once, at handover, as requested.
        public bool TryConsumeUnreserved(bool isDrink) =>
            hasStock(isDrink, HasReservedPortion && drink == isDrink ? 2 : 1) && consume(isDrink);

        private void Cancel() { guest = null; pickedUp = false; }
        public bool DirectWaiter(NpcSimulation npc)
        {
            if (npc != waiter || guest == null || !npc.CanServeTavern) return false;
            npc.GoToService(pickedUp ? destination : npc.TavernPoint, pickedUp);
            return true;
        }
        // Called after every shared event boundary, including all skipped-time travel arrivals.
        public void Update()
        {
            if (guest != null && (!guest.WantsSeatService(drink) ||
                NpcPoint.Distance(servicePoint(guest), destination) > 0.001d)) Cancel();
            // Do not let the last reserved portion deadlock the waiter's own urgent need.
            // Cancelling returns its availability; no stock has been consumed or lost.
            if (HasReservedPortion && waiter != null &&
                (drink ? waiter.NeedsDrink : waiter.NeedsFood) && !hasStock(drink, 2)) Cancel();
            if (waiter == null || !waiter.CanServeTavern) return;
            if (guest == null)
            {
                for (int i = 0; i < guests.Count; i++)
                {
                    int index = (nextGuest + i) % guests.Count;
                    NpcSimulation candidate = guests[index];
                    if (candidate == waiter) continue;
                    bool thirsty = candidate.WantsSeatService(true);
                    bool hungry = candidate.WantsSeatService(false);
                    if (!thirsty && !hungry) continue;
                    // Preserve thirst priority; do not serve a meal in place of a missing drink.
                    if (!hasStock(thirsty, 1)) continue;
                    guest = candidate; drink = thirsty; pickedUp = false;
                    destination = servicePoint(candidate);
                    nextGuest = (index + 1) % guests.Count;
                    break;
                }
            }
            if (guest == null) return;
            if (!pickedUp && NpcPoint.Distance(waiter.Position, waiter.TavernPoint) < 0.000001d)
            {
                if (!hasStock(drink, 1)) { Cancel(); return; }
                pickedUp = true; // Reserve, without consuming or improving the need.
            }
            if (pickedUp && NpcPoint.Distance(waiter.Position, destination) < 0.000001d &&
                NpcPoint.Distance(waiter.Position, guest.Position) <= 1.1d)
            {
                if (guest.WantsSeatService(drink) && hasStock(drink, 1) && consume(drink))
                    guest.ReceiveSeatService(drink);
                Cancel();
                return;
            }
            DirectWaiter(waiter);
        }
    }

    public interface INpcServiceNavigation
    {
        void SetServiceDestination(NpcPoint point);
    }
}
