using System;
using UnityEngine;
using Village.Npc;
using Village.Storage;

/// <summary>Iron pickup from a smelter, delivery and tool processing at the smithy.</summary>
[DisallowMultipleComponent]
public sealed class SmithDeliveryJob : WorkDeliveryJob, INpcProductionGate
{
    [SerializeField] private BuildingWarehouse sourceWarehouse;
    [SerializeField] private BuildingWarehouse smithWarehouse;
    [SerializeField] private Transform pickupPoint;
    [SerializeField] private Transform deliveryPoint;
    [SerializeField, Min(1)] private int deliveryQuantity = 10;
    [SerializeField, Min(1)] private int ironPerCycle = 2;
    [SerializeField, Min(1)] private int toolsPerCycle = 1;
    [SerializeField, Min(1f)] private float workMinutesPerCycle = 30f;

    public override Transform DeliveryPoint => deliveryPoint;
    public override Transform PickupPoint => pickupPoint;
    public override string DestinationName => smithWarehouse != null ? smithWarehouse.WarehouseName : "Schmiede";
    public override string PickupName => sourceWarehouse != null ? sourceWarehouse.WarehouseName : "Schmelze";
    public override string CargoName => "Eisen";
    public override WorkDeliverySettings Settings => new WorkDeliverySettings {
        unitsPerWorkHour = 60d / workMinutesPerCycle, deliveryQuantity = deliveryQuantity };

    public override void Validate()
    {
        if (sourceWarehouse == null || smithWarehouse == null || pickupPoint == null || deliveryPoint == null ||
            sourceWarehouse == smithWarehouse || ironPerCycle <= 0 || toolsPerCycle <= 0 ||
            deliveryQuantity <= 0 || float.IsNaN(workMinutesPerCycle) ||
            float.IsInfinity(workMinutesPerCycle) || workMinutesPerCycle < 1f)
            throw new InvalidOperationException("Smith job needs distinct warehouses, road points and positive recipe values.");
        Settings.Validate();
    }

    public bool CanProduce => smithWarehouse != null &&
        smithWarehouse.CanConvert("Eisen", ironPerCycle, "Werkzeuge", toolsPerCycle);

    public override bool HasBatch(int quantity) => sourceWarehouse != null && smithWarehouse != null &&
        !smithWarehouse.Has("Eisen", ironPerCycle) && sourceWarehouse.Has("Eisen", quantity);

    public override bool TryPickUp(int quantity) => sourceWarehouse != null && sourceWarehouse.TryRemove("Eisen", quantity);

    public override bool TryDeliver(int quantity)
    {
        if (smithWarehouse == null) return false;
        try { smithWarehouse.Add("Eisen", quantity); return true; }
        catch (OverflowException) { return false; } // The shared simulation keeps undelivered cargo.
    }

    public override bool TryProduceOne() => smithWarehouse != null &&
        smithWarehouse.TryConvert("Eisen", ironPerCycle, "Werkzeuge", toolsPerCycle);
}
