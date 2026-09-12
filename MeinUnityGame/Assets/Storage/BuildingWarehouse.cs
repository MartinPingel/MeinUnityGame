using System;
using System.Collections.Generic;
using UnityEngine;

namespace Village.Storage
{
    /// <summary>Attach to a building/storage location, never to its worker.</summary>
    [DisallowMultipleComponent]
    public sealed class BuildingWarehouse : MonoBehaviour
    {
        [SerializeField] private string warehouseName = "Warenlager";
        [Tooltip("Optional starting quantities. Configure before entering Play mode.")]
        [SerializeField] private List<InitialStock> initialStock = new List<InitialStock>();
        private WarehouseStock stock;
        [Header("Optional workplace tools - configure before Play")]
        [SerializeField] private bool requiresWorkTools;
        [SerializeField, Min(0.1f)] private float toolMaximumDurability = 100f;
        [SerializeField, Min(0.1f)] private float toolWearPerWorkHour = 10f;
        private WorkplaceTools tools;

        public WorkplaceTools Tools => !requiresWorkTools ? null :
            tools ?? (tools = new WorkplaceTools(Stock, toolMaximumDurability, toolWearPerWorkHour));

        public string WarehouseName => warehouseName;
        public int GetQuantity(string goodsType) => Stock.GetQuantity(goodsType);
        public bool Has(string goodsType, int quantity) => Stock.Has(goodsType, quantity);
        public void Add(string goodsType, int quantity) => Stock.Add(goodsType, quantity);
        public bool TryRemove(string goodsType, int quantity) => Stock.TryRemove(goodsType, quantity);
        public StockRecord[] GetSnapshot() => Stock.GetSnapshot();
        public bool CanConvert(string inputType, int inputQuantity, string outputType, int outputQuantity) =>
            Stock.CanConvert(inputType, inputQuantity, outputType, outputQuantity);
        public bool TryConvert(string inputType, int inputQuantity, string outputType, int outputQuantity) =>
            Stock.TryConvert(inputType, inputQuantity, outputType, outputQuantity);

        private WarehouseStock Stock
        {
            get
            {
                if (stock == null)
                {
                    // Build atomically, also when another component queries before Awake.
                    var initialized = new WarehouseStock();
                    foreach (InitialStock entry in initialStock)
                        initialized.Add(entry.goodsType, entry.quantity);
                    stock = initialized;
                }
                return stock;
            }
        }

        [Serializable]
        private struct InitialStock
        {
            public string goodsType;
            [Min(1)] public int quantity;
        }
    }
}
