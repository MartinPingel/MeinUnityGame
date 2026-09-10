using UnityEngine;
using Village.Npc;

/// <summary>Scene adapter for the shared, needs-aware transport and work simulation.</summary>
public abstract class WorkDeliveryJob : MonoBehaviour, INpcDeliveryInventory
{
    public abstract Transform DeliveryPoint { get; }
    public abstract Transform PickupPoint { get; }
    public abstract string DestinationName { get; }
    public abstract string PickupName { get; }
    public abstract string CargoName { get; }
    public abstract WorkDeliverySettings Settings { get; }
    public abstract void Validate();
    public abstract bool TryProduceOne();
    public abstract bool HasBatch(int quantity);
    public abstract bool TryPickUp(int quantity);
    public abstract bool TryDeliver(int quantity);
}
