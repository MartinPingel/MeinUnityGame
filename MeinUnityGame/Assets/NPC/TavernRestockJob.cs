using System;
using UnityEngine;
using Village.Npc;
using Village.Storage;

/// <summary>Real source stock, carried load, then tavern deposit at arrival.</summary>
[DisallowMultipleComponent]
public sealed class TavernRestockJob : WorkDeliveryJob, INpcPriorityRestock, INpcProductionGate
{
    [SerializeField] private BuildingWarehouse tavernWarehouse;
    [SerializeField] private BuildingWarehouse marketWarehouse;
    [SerializeField] private BuildingWarehouse wellWarehouse;
    [SerializeField] private Transform marketPoint;
    [SerializeField] private Transform wellPoint;
    [SerializeField] private Transform deliveryPoint;
    [SerializeField, Min(1)] private int maximumFoodStock = 20;
    [SerializeField, Min(1)] private int maximumWaterStock = 20;
    [SerializeField, Min(1)] private int carryingCapacity = 40;
    private bool selectedDrink, preferDrink;
    private BuildingWarehouse selectedSource;
    private int carriedQuantity;
    private string SelectedGoods => selectedDrink ? "Getränke" : "Lebensmittel";
    private int SelectedMaximum => selectedDrink ? maximumWaterStock : maximumFoodStock;
    public bool CanProduce => false;
    public override Transform DeliveryPoint => deliveryPoint;
    public override Transform PickupPoint => selectedDrink ? wellPoint : marketPoint;
    public override string DestinationName => "Taverne (Lager auffüllen)";
    public override string PickupName => selectedDrink ? "Brunnen (Wasserlager)" : "Marktstand (Lebensmittel)";
    public override string CargoName => selectedDrink ? "Wasser" : "Lebensmittel";
    public override WorkDeliverySettings Settings => new WorkDeliverySettings {
        unitsPerWorkHour = 1d, deliveryQuantity = carryingCapacity };
    public bool HasAvailableSupply => Available(false) > 0 || Available(true) > 0;
    public bool IsTavernSupplyEmpty(bool drink) =>
        tavernWarehouse.GetQuantity(drink ? "Getränke" : "Lebensmittel") == 0;

    private int Available(bool drink)
    {
        string goods = drink ? "Getränke" : "Lebensmittel";
        BuildingWarehouse source = drink ? wellWarehouse : marketWarehouse;
        int deficit = Math.Max(0, (drink ? maximumWaterStock : maximumFoodStock) - tavernWarehouse.GetQuantity(goods));
        return Math.Min(carryingCapacity, Math.Min(deficit, source.GetQuantity(goods)));
    }
    public bool TrySelectSupply(bool? preferredDrink, out NpcPoint pickup)
    {
        pickup = default(NpcPoint);
        if (carriedQuantity > 0) return false;
        bool choice = preferredDrink ?? preferDrink;
        if (Available(choice) == 0 && !preferredDrink.HasValue) choice = !choice;
        if (Available(choice) == 0) return false;
        selectedDrink = choice;
        selectedSource = choice ? wellWarehouse : marketWarehouse;
        Vector3 point = PickupPoint.position;
        pickup = new NpcPoint(point.x, point.y, point.z);
        return true;
    }
    // The simulation calls this only after actual arrival at the selected source.
    public int TakeSelectedSupply()
    {
        if (selectedSource == null || carriedQuantity > 0) return 0;
        int quantity = Available(selectedDrink);
        if (quantity <= 0 || !selectedSource.TryRemove(SelectedGoods, quantity)) return 0;
        carriedQuantity = quantity;
        return quantity;
    }
    public override bool TryDeliver(int quantity)
    {
        if (selectedSource == null || quantity <= 0 || quantity != carriedQuantity ||
            quantity > SelectedMaximum - tavernWarehouse.GetQuantity(SelectedGoods)) return false;
        try { tavernWarehouse.Add(SelectedGoods, quantity); }
        catch (OverflowException) { return false; }
        carriedQuantity = 0;
        preferDrink = !selectedDrink;
        selectedSource = null;
        return true;
    }
    public override bool TryProduceOne() => false;
    public override bool HasBatch(int quantity) => false;
    public override bool TryPickUp(int quantity) => false;
    public override void Validate()
    {
        if (tavernWarehouse == null || marketWarehouse == null || wellWarehouse == null ||
            tavernWarehouse == marketWarehouse || tavernWarehouse == wellWarehouse || marketWarehouse == wellWarehouse ||
            marketPoint == null || wellPoint == null || deliveryPoint == null ||
            marketPoint == deliveryPoint || wellPoint == deliveryPoint || maximumFoodStock <= 0 ||
            maximumWaterStock <= 0 || carryingCapacity <= 0)
            throw new InvalidOperationException("Tavern restocking needs distinct warehouses, connected source points and positive stock limits.");
        Settings.Validate();
    }
}
