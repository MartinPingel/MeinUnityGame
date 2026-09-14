namespace Village.Npc
{
    public interface INpcPriorityRestock : INpcDeliveryInventory
    {
        bool HasAvailableSupply { get; }
        bool IsTavernSupplyEmpty(bool drink);
        bool TrySelectSupply(bool? preferredDrink, out NpcPoint pickup);
        int TakeSelectedSupply();
    }

    public interface INpcRestockNavigation
    {
        void SetRestockPickup(NpcPoint pickup);
    }
}
