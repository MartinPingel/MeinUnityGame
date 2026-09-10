using System;

namespace Village.Npc
{
    [Serializable]
    public sealed class WorkDeliverySettings
    {
        public double unitsPerWorkHour = 6d;
        public int deliveryQuantity = 10;

        public void Validate()
        {
            if (double.IsNaN(unitsPerWorkHour) || double.IsInfinity(unitsPerWorkHour) ||
                unitsPerWorkHour <= 0d || unitsPerWorkHour > 3600d || deliveryQuantity <= 0)
                throw new ArgumentException("Production rate must be in (0, 3600] and delivery quantity positive.");
        }
    }

    /// <summary>Atomic building-stock operations. Carried cargo stays in the NPC simulation until delivery.</summary>
    public interface INpcDeliveryInventory
    {
        bool TryProduceOne();
        bool HasBatch(int quantity);
        bool TryPickUp(int quantity);
        bool TryDeliver(int quantity);
    }

    /// <summary>Optional input/capacity gate. Time without materials must not count as production work.</summary>
    public interface INpcProductionGate
    {
        bool CanProduce { get; }
    }
}
