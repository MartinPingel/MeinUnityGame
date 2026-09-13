namespace Village.Npc
{
    /// <summary>Optional building-owned equipment required for productive work.</summary>
    public interface INpcToolSupply : INpcWorkEquipment
    {
        bool NeedsDelivery { get; }
        bool TryCollect();
        bool TryDeposit();
    }

    public interface INpcWorkEquipment
    {
        bool CanWork { get; }
        double MinutesUntilBreak { get; }
        void Wear(double workMinutes);
    }
}
