using System;

namespace Village.Storage
{
    /// <summary>
    /// One building-owned tool in use, plus fresh spares in the same warehouse quantity.
    /// The active tool remains part of stock until it breaks. No NPC owns this state.
    /// </summary>
    public sealed class WorkplaceTools
    {
        public const string GoodsType = "Werkzeuge";
        private readonly WarehouseStock stock;
        private double remainingMinutes;
        private const double Epsilon = 1e-9;

        public double MaximumDurability { get; }
        public double WearPerWorkHour { get; }
        public int Quantity => stock.GetQuantity(GoodsType);
        public bool HasUsableTool => Quantity > 0 && remainingMinutes > 0;
        public double CurrentDurability => HasUsableTool
            ? Math.Min(MaximumDurability, remainingMinutes * WearPerWorkHour / 60d) : 0d;
        public double MinutesUntilBreak => HasUsableTool ? remainingMinutes : double.PositiveInfinity;

        public WorkplaceTools(WarehouseStock stock, double maximumDurability, double wearPerWorkHour)
        {
            if (stock == null) throw new ArgumentNullException(nameof(stock));
            if (double.IsNaN(maximumDurability) || double.IsInfinity(maximumDurability) || maximumDurability <= 0 ||
                double.IsNaN(wearPerWorkHour) || double.IsInfinity(wearPerWorkHour) || wearPerWorkHour <= 0 ||
                double.IsInfinity(maximumDurability / wearPerWorkHour * 60d) ||
                maximumDurability / wearPerWorkHour * 60d < 0.001d)
                throw new ArgumentException("Tool durability and wear must be finite and positive; lifetime must be at least 0.001 game minutes.");
            this.stock = stock;
            MaximumDurability = maximumDurability;
            WearPerWorkHour = wearPerWorkHour;
            stock.Changed += OnStockChanged;
            OnStockChanged();
        }

        private void OnStockChanged()
        {
            // Fresh stock must not repair a used tool. Generic withdrawals take spares first.
            if (Quantity == 0) remainingMinutes = 0;
            else if (remainingMinutes <= 0) remainingMinutes = MaximumDurability / WearPerWorkHour * 60d;
        }

        /// <summary>Called with actual work time only, bounded at break events by the NPC simulation.</summary>
        public void Wear(double workMinutes)
        {
            if (double.IsNaN(workMinutes) || double.IsInfinity(workMinutes) || workMinutes < 0)
                throw new ArgumentOutOfRangeException(nameof(workMinutes));
            while (workMinutes > 0 && HasUsableTool)
            {
                double used = Math.Min(workMinutes, remainingMinutes);
                remainingMinutes -= used;
                workMinutes -= used;
                if (remainingMinutes <= Epsilon)
                {
                    remainingMinutes = 0;
                    // The stock event prepares a fresh spare, if any; broken tools never remain available.
                    if (!stock.TryRemove(GoodsType, 1))
                        throw new InvalidOperationException("The building's active tool is missing from its stock.");
                }
            }
        }
    }
}
