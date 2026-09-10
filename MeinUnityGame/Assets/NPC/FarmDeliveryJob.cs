using System;
using UnityEngine;
using Village.Npc;
using Village.Storage;

/// <summary>Building-owned production/delivery configuration, enabled only for its assigned worker.</summary>
[DisallowMultipleComponent]
public sealed class FarmDeliveryJob : MonoBehaviour, INpcDeliveryInventory
{
    [SerializeField] private BuildingWarehouse sourceWarehouse;
    [SerializeField] private BuildingWarehouse destinationWarehouse;
    [SerializeField] private Transform deliveryPoint;
    [SerializeField] private Transform pickupPoint;
    [SerializeField] private WorkDeliverySettings settings = new WorkDeliverySettings();

    public Transform DeliveryPoint => deliveryPoint;
    public Transform PickupPoint => pickupPoint;
    public string DestinationName => destinationWarehouse != null ? destinationWarehouse.WarehouseName : "Lieferziel";
    public WorkDeliverySettings Settings => settings;

    public void Validate()
    {
        if (sourceWarehouse == null || destinationWarehouse == null || deliveryPoint == null || pickupPoint == null ||
            sourceWarehouse == destinationWarehouse)
            throw new InvalidOperationException("Delivery needs two distinct building warehouses and a road access point.");
        settings.Validate();
    }

    public bool TryProduceOne() => TryAdd(sourceWarehouse, 1);
    public bool HasBatch(int quantity) => sourceWarehouse != null && sourceWarehouse.Has("Lebensmittel", quantity);
    public bool TryPickUp(int quantity) => sourceWarehouse != null &&
        sourceWarehouse.TryRemove("Lebensmittel", quantity);
    public bool TryDeliver(int quantity) => TryAdd(destinationWarehouse, quantity);

    private static bool TryAdd(BuildingWarehouse warehouse, int quantity)
    {
        if (warehouse == null) return false;
        try { warehouse.Add("Lebensmittel", quantity); return true; }
        catch (OverflowException) { return false; } // Keep cargo when destination cannot accept it.
    }
}
