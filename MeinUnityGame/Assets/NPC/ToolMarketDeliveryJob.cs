using System;
using UnityEngine;
using Village.Npc;
using Village.Storage;

/// <summary>Marcel physically collects finished tools from the smithy for the market warehouse.</summary>
[DisallowMultipleComponent]
public sealed class ToolMarketDeliveryJob : WorkDeliveryJob, INpcProductionGate
{
    [SerializeField] private BuildingWarehouse smithWarehouse;
    [SerializeField] private BuildingWarehouse marketWarehouse;
    [SerializeField] private Transform pickupPoint;
    [SerializeField] private Transform deliveryPoint;
    [SerializeField, Min(1)] private int deliveryQuantity = 2;

    public override Transform PickupPoint => pickupPoint;
    public override Transform DeliveryPoint => deliveryPoint;
    public override string PickupName => "Schmiede";
    public override string DestinationName => "Marktstand";
    public override string CargoName => "Werkzeuge";
    public override WorkDeliverySettings Settings => new WorkDeliverySettings {
        unitsPerWorkHour = 1d, deliveryQuantity = deliveryQuantity };
    public bool CanProduce => false;

    public override void Validate()
    {
        if (smithWarehouse == null || marketWarehouse == null || pickupPoint == null || deliveryPoint == null ||
            smithWarehouse == marketWarehouse)
            throw new InvalidOperationException("Tool delivery needs distinct smith/market warehouses and road points.");
        Settings.Validate();
    }

    public override bool TryProduceOne() => false;
    public override bool HasBatch(int quantity) => smithWarehouse != null && quantity > 0 &&
        smithWarehouse.GetQuantity("Werkzeuge") - (smithWarehouse.Tools != null ? 2 : 0) >= quantity;
    public override bool TryPickUp(int quantity) => HasBatch(quantity) && smithWarehouse.TryRemove("Werkzeuge", quantity);
    public override bool TryDeliver(int quantity)
    {
        if (marketWarehouse == null) return false;
        try { marketWarehouse.Add("Werkzeuge", quantity); return true; }
        catch (OverflowException) { return false; }
    }
}
