using System;
using System.Collections.Generic;

namespace Village.Npc
{
    /// <summary>One real tavern portion, reserved at pickup and consumed only at a seated guest.</summary>
    public sealed class NpcTavernService
    {
        private readonly List<NpcTavernService> coworkers = new List<NpcTavernService>();
        private readonly bool selfSupplyAtTavern;
        private readonly List<NpcSimulation> guests = new List<NpcSimulation>();
        private readonly Func<bool, int, bool> hasStock;
        private readonly Func<bool, bool> consume;
        private readonly Func<NpcSimulation, NpcPoint> servicePoint;
        private NpcSimulation waiter, guest;
        // A guest needing both may be ordered together; each good is reserved and delivered
        // independently so partial stock never blocks the other.
        private bool orderDrink, orderFood, pickedUpDrink, pickedUpFood;
        private NpcPoint destination;
        private int nextGuest;
        public NpcSimulation Guest => guest;
        public bool HasReservedPortion => guest != null && (pickedUpDrink || pickedUpFood);
        public bool IsWaiter(NpcSimulation npc) => npc == waiter;
        public bool HasPresentGuest
        {
            get
            {
                foreach (NpcSimulation npc in guests)
                    if (npc != waiter && npc.IsPresentTavernGuest) return true;
                return false;
            }
        }


        public NpcTavernService(Func<bool, int, bool> hasStock, Func<bool, bool> consume,
            Func<NpcSimulation, NpcPoint> servicePoint, bool selfSupplyAtTavern = false)
        {
            this.selfSupplyAtTavern = selfSupplyAtTavern;
            this.hasStock = hasStock ?? throw new ArgumentNullException(nameof(hasStock));
            this.consume = consume ?? throw new ArgumentNullException(nameof(consume));
            this.servicePoint = servicePoint ?? throw new ArgumentNullException(nameof(servicePoint));
        }
        public void Register(NpcSimulation npc, bool isWaiter, bool attachService = true)
        {
            if (!guests.Contains(npc)) guests.Add(npc);
            if (attachService) npc.TavernService = this;
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
            if (npc.TavernService == this) npc.TavernService = null;
        }
        // Coworkers share guests and real stock, but each carries at most one portion.
        public void LinkCoworker(NpcTavernService other)
        {
            if (other == null || other == this) throw new ArgumentException("Invalid coworker.");
            if (!coworkers.Contains(other)) coworkers.Add(other);
            if (!other.coworkers.Contains(this)) other.coworkers.Add(this);
        }
        public bool SelfSuppliesAtTavern(NpcSimulation npc) => selfSupplyAtTavern && npc == waiter;
        private int OtherReservations(bool isDrink)
        {
            int count = 0;
            foreach (NpcTavernService other in coworkers)
                if (other.HasReservedPortionOf(isDrink)) count++;
            return count;
        }
        private bool HasReservedPortionOf(bool isDrink) => guest != null && (isDrink ? pickedUpDrink : pickedUpFood);
        private bool IsClaimedByCoworker(NpcSimulation npc)
        {
            foreach (NpcTavernService other in coworkers) if (other.Guest == npc) return true;
            return false;
        }
        private bool HasAvailablePortion(bool isDrink) => hasStock(isDrink, 1 + OtherReservations(isDrink));
        // Whether every currently ordered good has already been physically collected.
        private bool FullyPickedUp => (!orderDrink || pickedUpDrink) && (!orderFood || pickedUpFood);
        internal bool TryConsumeSelf(NpcSimulation npc, bool isDrink)
        {
            if (!SelfSuppliesAtTavern(npc) || NpcPoint.Distance(npc.Position, npc.TavernPoint) > 0.000001d)
                return false;
            int reserved = OtherReservations(isDrink) + (HasReservedPortionOf(isDrink) ? 1 : 0);
            return hasStock(isDrink, 1 + reserved) && consume(isDrink);
        }

        public bool IsOwnOrder(NpcSimulation npc) => npc == waiter && guest == waiter;
        // The innkeeper's own order carries exactly one good; a guest order may carry either or both.
        private bool CanContinueOrder => waiter != null &&
            (guest == waiter ? waiter.HasSupplyReservation(orderDrink) : waiter.CanServeTavern);
        private NpcPoint HandoverPoint(NpcSimulation npc) => npc == waiter
            ? npc.ReservedSupplySeat : servicePoint(npc);

        private void Cancel() { guest = null; orderDrink = orderFood = pickedUpDrink = pickedUpFood = false; }
        public bool DirectWaiter(NpcSimulation npc)
        {
            if (npc != waiter || guest == null || !CanContinueOrder) return false;
            npc.GoToService(FullyPickedUp ? destination : npc.TavernPoint, FullyPickedUp);
            return true;
        }
        // Called after every shared event boundary, including all skipped-time travel arrivals.
        public void Update()
        {
            // Cancel only once every currently ordered good is no longer wanted at all - a
            // combined order keeps running even after one of its two goods is still pending.
            bool stillWanted = guest != null && (guest == waiter
                ? waiter.HasSupplyReservation(orderDrink)
                : (orderDrink && guest.WantsSeatService(true)) || (orderFood && guest.WantsSeatService(false)));
            if (guest != null && (!stillWanted ||
                NpcPoint.Distance(HandoverPoint(guest), destination) > 0.001d)) Cancel();
            // Newly arrived thirst always joins or restarts the order; never serve a meal alone
            // while the same guest is also waiting on a drink.
            if (guest != null && !orderDrink && guest.NeedsDrink) Cancel();
            if (waiter == null) return;
            // The innkeeper's own hunger/thirst retains priority. Return any guest reservation
            // to availability, then collect one portion and carry it back to his own bank place.
            if (waiter.NeedsDrink || waiter.NeedsFood)
            {
                if (guest != null && guest != waiter) Cancel();
                if (selfSupplyAtTavern) return; // This worker eats/drinks at the store, never as a seated guest.
                if (guest == null)
                {
                    bool ownDrink = waiter.NeedsDrink;
                    if (!waiter.WantsSeatService(ownDrink) || !HasAvailablePortion(ownDrink)) return;
                    guest = waiter; orderDrink = ownDrink; orderFood = !ownDrink;
                    pickedUpDrink = pickedUpFood = false;
                    destination = HandoverPoint(waiter);
                }
            }
            else if (!waiter.CanServeTavern) return;
            if (guest == null)
            {
                for (int i = 0; i < guests.Count; i++)
                {
                    int index = (nextGuest + i) % guests.Count;
                    NpcSimulation candidate = guests[index];
                    if (candidate == waiter || IsClaimedByCoworker(candidate)) continue;
                    bool thirsty = candidate.WantsSeatService(true);
                    bool hungry = candidate.WantsSeatService(false);
                    if (!thirsty && !hungry) continue;
                    // Preserve thirst priority: never serve a meal while a wanted drink is unavailable,
                    // even if the guest also wants food. The other good joins the same trip when it can.
                    if (thirsty && !HasAvailablePortion(true)) continue;
                    bool takeDrink = thirsty, takeFood = hungry && HasAvailablePortion(false);
                    if (!takeDrink && !takeFood) continue;
                    guest = candidate; orderDrink = takeDrink; orderFood = takeFood;
                    pickedUpDrink = pickedUpFood = false;
                    destination = HandoverPoint(candidate);
                    nextGuest = (index + 1) % guests.Count;
                    break;
                }
            }
            if (guest == null) return;
            if (!FullyPickedUp && NpcPoint.Distance(waiter.Position, waiter.TavernPoint) < 0.000001d)
            {
                if (orderDrink && !pickedUpDrink)
                {
                    if (!HasAvailablePortion(true)) { Cancel(); return; }
                    pickedUpDrink = true; // Reserve, without consuming or improving the need.
                }
                if (orderFood && !pickedUpFood)
                {
                    if (!HasAvailablePortion(false)) { Cancel(); return; }
                    pickedUpFood = true;
                }
            }
            if (FullyPickedUp && NpcPoint.Distance(waiter.Position, destination) < 0.000001d &&
                NpcPoint.Distance(waiter.Position, guest.Position) <= 1.1d)
            {
                bool atGuestSeat = guest == waiter
                    ? guest.HasSupplyReservation(orderDrink) &&
                      NpcPoint.Distance(guest.Position, guest.ReservedSupplySeat) < 0.000001d
                    : (!orderDrink || guest.WantsSeatService(true)) && (!orderFood || guest.WantsSeatService(false));
                if (atGuestSeat)
                {
                    if (orderDrink && hasStock(true, 1) && consume(true)) guest.ReceiveSeatService(true);
                    if (orderFood && hasStock(false, 1) && consume(false)) guest.ReceiveSeatService(false);
                }
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



