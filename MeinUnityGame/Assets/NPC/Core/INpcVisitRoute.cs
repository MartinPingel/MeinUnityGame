namespace Village.Npc
{
    /// <summary>Optional dynamic work destination: a sequence of timed stops, each visited
    /// once per shift, followed by a steady base location for the rest of the shift. Opt-in;
    /// an NPC without a visit route keeps its single static workplace unchanged.</summary>
    public interface INpcVisitRoute
    {
        NpcPoint CurrentStop { get; }
        // PositiveInfinity once settled at the final (base) stop for the rest of the shift.
        double MinutesUntilNextStop { get; }
        // Consumes elapsed dwell time at the current stop. Call only while actually working there.
        void Consume(double minutes);
        // Restarts today's round at the first stop; called once, at the start of each shift.
        void ResetForNewShift();
    }
}
