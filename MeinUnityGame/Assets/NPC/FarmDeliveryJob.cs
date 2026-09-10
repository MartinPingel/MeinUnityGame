using System;
using UnityEngine;
using Village.Npc;
using Village.Storage;

/// <summary>Building-owned production/delivery configuration, enabled only for its assigned worker.</summary>
[DisallowMultipleComponent]
public sealed class FarmDeliveryJob : WorkDeliveryJob
{
    [SerializeField] private BuildingWarehouse sourceWarehouse;
    [SerializeField] private BuildingWarehouse destinationWarehouse;
    [SerializeField] private Transform deliveryPoint;
    [SerializeField] private Transform pickupPoint;
    [SerializeField] private WorkDeliverySettings settings = new WorkDeliverySettings();

    public override Transform DeliveryPoint => deliveryPoint;
    public override Transform PickupPoint => pickupPoint;
    public override string DestinationName => destinationWarehouse != null ? destinationWarehouse.WarehouseName : "Lieferziel";
    public override string CargoName => "Lebensmittel";
    public override string PickupName => "Bauernhoflager";
    public override WorkDeliverySettings Settings => settings;

    public override void Validate()
    {
        if (sourceWarehouse == null || destinationWarehouse == null || deliveryPoint == null || pickupPoint == null ||
            sourceWarehouse == destinationWarehouse)
            throw new InvalidOperationException("Delivery needs two distinct building warehouses and a road access point.");
        settings.Validate();
    }

    public override bool TryProduceOne() => TryAdd(sourceWarehouse, 1);
    public override bool HasBatch(int quantity) => sourceWarehouse != null && sourceWarehouse.Has("Lebensmittel", quantity);
    public override bool TryPickUp(int quantity) => sourceWarehouse != null &&
        sourceWarehouse.TryRemove("Lebensmittel", quantity);
    public override bool TryDeliver(int quantity) => TryAdd(destinationWarehouse, quantity);

    private static bool TryAdd(BuildingWarehouse warehouse, int quantity)
    {
        if (warehouse == null) return false;
        try { warehouse.Add("Lebensmittel", quantity); return true; }
        catch (OverflowException) { return false; } // Keep cargo when destination cannot accept it.
    }
}
