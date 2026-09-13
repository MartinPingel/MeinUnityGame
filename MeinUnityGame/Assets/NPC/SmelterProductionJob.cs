using System;
using UnityEngine;
using Village.Npc;
using Village.Storage;

/// <summary>Jan consumes deposited ore while working; no starting iron or automatic refill.</summary>
[DisallowMultipleComponent]
public sealed class SmelterProductionJob : WorkDeliveryJob, INpcProductionGate
{
    [SerializeField] private BuildingWarehouse warehouse;
    [SerializeField] private Transform workPoint;
    [SerializeField, Min(1)] private int orePerCycle = 2;
    [SerializeField, Min(1)] private int ironPerCycle = 1;
    [SerializeField, Min(1f)] private float workMinutesPerCycle = 20f;

    public override Transform PickupPoint => workPoint;
    public override Transform DeliveryPoint => workPoint;
    public override string PickupName => "Schmelze";
    public override string DestinationName => "Schmelze";
    public override string CargoName => "Eisen";
    public override WorkDeliverySettings Settings => new WorkDeliverySettings {
        unitsPerWorkHour = 60d / workMinutesPerCycle, deliveryQuantity = 1 };

    public override void Validate()
    {
        if (warehouse == null || workPoint == null || orePerCycle <= 0 || ironPerCycle <= 0 ||
            float.IsNaN(workMinutesPerCycle) || float.IsInfinity(workMinutesPerCycle) || workMinutesPerCycle < 1f)
            throw new InvalidOperationException("Smelter needs its warehouse, work point and positive recipe values.");
        Settings.Validate();
    }

    public bool CanProduce => warehouse != null && warehouse.CanConvert("Eisenerz", orePerCycle, "Eisen", ironPerCycle);
    public override bool TryProduceOne() => warehouse != null &&
        warehouse.TryConvert("Eisenerz", orePerCycle, "Eisen", ironPerCycle);
    public override bool HasBatch(int quantity) => false;
    public override bool TryPickUp(int quantity) => false;
    public override bool TryDeliver(int quantity) => false;
}
