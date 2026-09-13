namespace Village.Npc
{
    /// <summary>Optional transport speed; ordinary NPC jobs retain their original movement speed.</summary>
    public interface INpcTravelSpeed
    {
        double GetTravelSpeedMultiplier(int cargoQuantity);
    }
}
