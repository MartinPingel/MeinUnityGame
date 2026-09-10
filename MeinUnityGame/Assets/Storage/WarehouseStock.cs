using System;
using System.Collections.Generic;

namespace Village.Storage
{
    /// <summary>Independent quantities keyed by stable, case-sensitive goods IDs.</summary>
    public sealed class WarehouseStock
    {
        private readonly SortedDictionary<string, int> quantities =
            new SortedDictionary<string, int>(StringComparer.Ordinal);

        public event Action Changed;

        public int GetQuantity(string goodsType)
        {
            ValidateType(goodsType);
            return quantities.TryGetValue(goodsType, out int quantity) ? quantity : 0;
        }

        public bool Has(string goodsType, int quantity)
        {
            ValidateQuantity(quantity);
            return GetQuantity(goodsType) >= quantity;
        }

        public void Add(string goodsType, int quantity)
        {
            ValidateQuantity(quantity);
            // Calculate before writing: overflow cannot corrupt the current stock.
            int result = checked(GetQuantity(goodsType) + quantity);
            quantities[goodsType] = result;
            Changed?.Invoke();
        }

        public bool TryRemove(string goodsType, int quantity)
        {
            ValidateQuantity(quantity);
            int current = GetQuantity(goodsType);
            if (current < quantity) return false; // Never partially withdraw.
            if (current == quantity) quantities.Remove(goodsType);
            else quantities[goodsType] = current - quantity;
            Changed?.Invoke();
            return true;
        }

        public bool CanConvert(string inputType, int inputQuantity, string outputType, int outputQuantity)
        {
            ValidateQuantity(inputQuantity);
            ValidateQuantity(outputQuantity);
            int input = GetQuantity(inputType), output = GetQuantity(outputType);
            if (inputType == outputType) throw new ArgumentException("Recipe input and output must differ.");
            return input >= inputQuantity && output <= int.MaxValue - outputQuantity;
        }

        /// <summary>Consume inputs and add outputs together, or leave the entire stock unchanged.</summary>
        public bool TryConvert(string inputType, int inputQuantity, string outputType, int outputQuantity)
        {
            if (!CanConvert(inputType, inputQuantity, outputType, outputQuantity)) return false;
            int remaining = quantities[inputType] - inputQuantity;
            int produced = GetQuantity(outputType) + outputQuantity; // Capacity checked before either write.
            if (remaining == 0) quantities.Remove(inputType);
            else quantities[inputType] = remaining;
            quantities[outputType] = produced;
            Changed?.Invoke(); // Observers only see the completed transaction.
            return true;
        }

        /// <summary>A detached, alphabetically sorted snapshot; callers cannot change this store.</summary>
        public StockRecord[] GetSnapshot()
        {
            var result = new StockRecord[quantities.Count];
            int index = 0;
            foreach (var pair in quantities) result[index++] = new StockRecord(pair.Key, pair.Value);
            return result;
        }

        private static void ValidateType(string goodsType)
        {
            if (string.IsNullOrWhiteSpace(goodsType) || goodsType != goodsType.Trim())
                throw new ArgumentException("Goods ID must be nonempty and have no surrounding whitespace.", nameof(goodsType));
        }

        private static void ValidateQuantity(int quantity)
        {
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        }
    }

    public readonly struct StockRecord
    {
        public string GoodsType { get; }
        public int Quantity { get; }
        public StockRecord(string goodsType, int quantity) { GoodsType = goodsType; Quantity = quantity; }
    }
}
