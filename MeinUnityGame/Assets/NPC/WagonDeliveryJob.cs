using System;
using UnityEngine;
using Village.Npc;
using Village.Storage;

/// <summary>One wagon load, owned by the transport job while on the road, never a second warehouse copy.</summary>
[DisallowMultipleComponent]
public sealed class WagonDeliveryJob : WorkDeliveryJob, INpcProductionGate, INpcTravelSpeed
{
    [SerializeField] private BuildingWarehouse sourceWarehouse;
    [SerializeField] private BuildingWarehouse destinationWarehouse;
    [SerializeField] private Transform pickupPoint;
    [SerializeField] private Transform deliveryPoint;
    [SerializeField] private string goodsType = "Eisenerz";
    [Tooltip("Full-load capacity in units. Configure before entering Play mode.")]
    [SerializeField, Min(1)] private int loadCapacity = 10;
    [SerializeField, Min(0.01f)] private float emptySpeedMultiplier = 1.5f;
    [SerializeField, Min(0.01f)] private float loadedSpeedMultiplier = 0.75f;
    [SerializeField] private GameObject cargoVisual;
    private NpcAgent carrier;

    public int LoadCapacity => loadCapacity;
    // The existing simulation's single cargo slot IS the wagon load.
    public int WagonLoad => carrier != null && carrier.Simulation != null ? carrier.Simulation.CargoQuantity : 0;
    public override Transform PickupPoint => pickupPoint;
    public override Transform DeliveryPoint => deliveryPoint;
    public override string PickupName => sourceWarehouse != null ? sourceWarehouse.WarehouseName : "Abholung";
    public override string DestinationName => destinationWarehouse != null ? destinationWarehouse.WarehouseName : "Lieferung";
    public override string CargoName => goodsType;
    public override WorkDeliverySettings Settings => new WorkDeliverySettings
        { unitsPerWorkHour = 1d, deliveryQuantity = loadCapacity };
    public bool CanProduce => false;
    public override bool TryProduceOne() => false;
    public double GetTravelSpeedMultiplier(int cargoQuantity) =>
        cargoQuantity > 0 ? loadedSpeedMultiplier : emptySpeedMultiplier;

    public override void Validate()
    {
        carrier = GetComponent<NpcAgent>();
        if (carrier == null || sourceWarehouse == null || destinationWarehouse == null ||
            sourceWarehouse == destinationWarehouse || pickupPoint == null || deliveryPoint == null ||
            string.IsNullOrWhiteSpace(goodsType) || loadCapacity <= 0 ||
            float.IsNaN(emptySpeedMultiplier) || float.IsInfinity(emptySpeedMultiplier) ||
            float.IsNaN(loadedSpeedMultiplier) || float.IsInfinity(loadedSpeedMultiplier) ||
            loadedSpeedMultiplier <= 0 || emptySpeedMultiplier <= loadedSpeedMultiplier)
            throw new InvalidOperationException("Wagon needs an NPC, distinct warehouses, road points, capacity and positive speeds (empty faster than loaded).");
        Settings.Validate();
    }

    public override bool HasBatch(int quantity) => quantity > 0 && quantity <= loadCapacity &&
        sourceWarehouse != null && sourceWarehouse.Has(goodsType, quantity);

    // Called at the pickup marker only. The simulation records the exact withdrawn quantity.
    public override bool TryPickUp(int quantity) => HasBatch(quantity) &&
        sourceWarehouse.TryRemove(goodsType, quantity);

    // Called at the destination marker only. Failed deposits leave the wagon load intact.
    public override bool TryDeliver(int quantity)
    {
        if (destinationWarehouse == null || quantity <= 0) return false;
        try { destinationWarehouse.Add(goodsType, quantity); return true; }
        catch (OverflowException) { return false; }
    }

    private void LateUpdate()
    {
        if (cargoVisual != null && cargoVisual.activeSelf != (WagonLoad > 0))
            cargoVisual.SetActive(WagonLoad > 0);
    }
}
