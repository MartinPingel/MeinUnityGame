using System;
using UnityEngine;
using Village.Npc;
using Village.Storage;

/// <summary>Work-time ore extraction and physical delivery to the smelter.</summary>
[DisallowMultipleComponent]
public sealed class MineProductionJob : WorkDeliveryJob, INpcProductionGate
{
    [SerializeField] private BuildingWarehouse mineWarehouse;
    [SerializeField] private Transform workPoint;
    [SerializeField] private BuildingWarehouse smelterWarehouse;
    [SerializeField] private Transform smelterPoint;
    [SerializeField, Min(1)] private int deliveryQuantity = 10;
    [SerializeField, Range(0.1f, 3600f)] private float unitsPerWorkHour = 6f;

    private const string OreGoods = "Eisenerz";
    public override Transform PickupPoint => workPoint;
    public override Transform DeliveryPoint => smelterPoint;
    public override string PickupName => "Mine";
    public override string DestinationName => "Schmelze";
    public override string CargoName => OreGoods;
    public override WorkDeliverySettings Settings => new WorkDeliverySettings {
        unitsPerWorkHour = unitsPerWorkHour, deliveryQuantity = deliveryQuantity };

    public override void Validate()
    {
        if (mineWarehouse == null || workPoint == null || smelterWarehouse == null || smelterPoint == null ||
            mineWarehouse == smelterWarehouse)
            throw new InvalidOperationException("Mine production needs distinct mine/smelter warehouses and road points.");
        Settings.Validate();
    }

    public bool CanProduce => mineWarehouse != null && mineWarehouse.GetQuantity(OreGoods) < int.MaxValue;

    // NpcSimulation calls this only after the required actual work time has elapsed.
    public override bool TryProduceOne()
    {
        if (!CanProduce) return false;
        try { mineWarehouse.Add(OreGoods, 1); return true; }
        catch (OverflowException) { return false; }
    }

    public override bool HasBatch(int quantity) => mineWarehouse != null && mineWarehouse.Has(OreGoods, quantity);
    public override bool TryPickUp(int quantity) => mineWarehouse != null && mineWarehouse.TryRemove(OreGoods, quantity);
    public override bool TryDeliver(int quantity)
    {
        if (smelterWarehouse == null) return false;
        try { smelterWarehouse.Add(OreGoods, quantity); return true; }
        catch (OverflowException) { return false; } // Shared simulation keeps the undelivered cargo.
    }
}
