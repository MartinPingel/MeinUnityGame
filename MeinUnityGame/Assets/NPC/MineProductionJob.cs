using System;
using UnityEngine;
using Village.Npc;
using Village.Storage;

/// <summary>Mine output through the existing work-time simulation, without transport tasks.</summary>
[DisallowMultipleComponent]
public sealed class MineProductionJob : WorkDeliveryJob, INpcProductionGate
{
    [SerializeField] private BuildingWarehouse mineWarehouse;
    [SerializeField] private Transform workPoint;
    [SerializeField, Range(0.1f, 3600f)] private float unitsPerWorkHour = 6f;

    private const string OreGoods = "Eisenerz";
    public override Transform PickupPoint => workPoint;
    public override Transform DeliveryPoint => workPoint;
    public override string PickupName => "Mine";
    public override string DestinationName => "Mine";
    public override string CargoName => OreGoods;
    public override WorkDeliverySettings Settings => new WorkDeliverySettings {
        unitsPerWorkHour = unitsPerWorkHour, deliveryQuantity = 1 };

    public override void Validate()
    {
        if (mineWarehouse == null || workPoint == null || mineWarehouse.Tools == null)
            throw new InvalidOperationException("Mine production needs its warehouse, work point and enabled work tools.");
        Settings.Validate();
    }

    public bool CanProduce => mineWarehouse != null && mineWarehouse.Tools != null &&
        mineWarehouse.Tools.HasUsableTool && mineWarehouse.GetQuantity(OreGoods) < int.MaxValue;

    // NpcSimulation calls this only after the required actual work time has elapsed.
    // Its existing work-equipment hook handles wear and exact break boundaries, also when waiting.
    public override bool TryProduceOne()
    {
        if (!CanProduce) return false;
        try { mineWarehouse.Add(OreGoods, 1); return true; }
        catch (OverflowException) { return false; }
    }

    // Ore remains in the mine; no new route, pickup or delivery is introduced.
    public override bool HasBatch(int quantity) => false;
    public override bool TryPickUp(int quantity) => false;
    public override bool TryDeliver(int quantity) => false;
}
