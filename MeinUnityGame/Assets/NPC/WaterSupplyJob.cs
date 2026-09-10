using System;
using UnityEngine;
using Village.Npc;
using Village.Storage;

/// <summary>Unlimited well source; water becomes tavern stock only after physical delivery.</summary>
[DisallowMultipleComponent]
public sealed class WaterSupplyJob : WorkDeliveryJob, INpcProductionGate, INpcWaterSupply
{
    [SerializeField] private BuildingWarehouse tavernWarehouse;
    [SerializeField] private Transform pickupPoint;
    [SerializeField] private Transform deliveryPoint;
    [SerializeField, Min(1)] private int minimumStock = 20;
    [SerializeField, Min(1)] private int deliveryQuantity = 40;

    // Preserve the existing goods ID used by every NPC's drink consumption.
    private const string WaterGoods = "Getränke";
    public override Transform PickupPoint => pickupPoint;
    public override Transform DeliveryPoint => deliveryPoint;
    public override string PickupName => "Brunnen";
    public override string DestinationName => "Taverne";
    public override string CargoName => "Wasser";
    public bool IsDrinkStockEmpty => tavernWarehouse.GetQuantity(WaterGoods) == 0;
    public bool CanProduce => false;
    public override WorkDeliverySettings Settings => new WorkDeliverySettings {
        unitsPerWorkHour = 1d, deliveryQuantity = deliveryQuantity };

    public override void Validate()
    {
        if (tavernWarehouse == null || pickupPoint == null || deliveryPoint == null ||
            pickupPoint == deliveryPoint || minimumStock <= 0 || deliveryQuantity <= 0 ||
            minimumStock > int.MaxValue - deliveryQuantity)
            throw new InvalidOperationException("Water supply needs a tavern warehouse, distinct road points and valid quantities.");
        Settings.Validate();
    }

    public override bool HasBatch(int quantity) => quantity == deliveryQuantity &&
        tavernWarehouse.GetQuantity(WaterGoods) < minimumStock;
    // Called exclusively at the well by NpcSimulation. No warehouse credit occurs here.
    public override bool TryPickUp(int quantity) => HasBatch(quantity);
    public override bool TryProduceOne() => false;
    public override bool TryDeliver(int quantity)
    {
        if (quantity <= 0) return false;
        try { tavernWarehouse.Add(WaterGoods, quantity); return true; }
        catch (OverflowException) { return false; } // Keep the complete carried load for a later retry.
    }
}
