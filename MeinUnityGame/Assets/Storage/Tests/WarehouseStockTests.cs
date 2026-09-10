using System;
using NUnit.Framework;
using UnityEngine;
using Village.Storage;

public sealed class WarehouseStockTests
{
    [Test]
    public void TypesAccumulateIndependentlyAndRemovalIsAllOrNothing()
    {
        var stock = new WarehouseStock();
        Assert.That(stock.GetQuantity("grain"), Is.Zero);
        stock.Add("grain", 10);
        stock.Add("grain", 5);
        stock.Add("ore", 8);
        Assert.That(stock.Has("grain", 15), Is.True);
        Assert.That(stock.TryRemove("grain", 16), Is.False);
        Assert.That(stock.GetQuantity("grain"), Is.EqualTo(15));
        Assert.That(stock.TryRemove("grain", 4), Is.True);
        Assert.That(stock.GetQuantity("grain"), Is.EqualTo(11));
        Assert.That(stock.GetQuantity("ore"), Is.EqualTo(8));
        Assert.That(stock.TryRemove("grain", 11), Is.True);
        Assert.That(stock.GetSnapshot().Length, Is.EqualTo(1));
    }

    [Test]
    public void InvalidOperationsAndOverflowDoNotChangeStock()
    {
        var stock = new WarehouseStock();
        stock.Add("ore", int.MaxValue);
        Assert.Throws<OverflowException>(() => stock.Add("ore", 1));
        Assert.Throws<ArgumentException>(() => stock.Add(" ", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => stock.Add("ore", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => stock.TryRemove("ore", -1));
        Assert.That(stock.TryRemove("missing", 1), Is.False);
        Assert.That(stock.GetQuantity("ore"), Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void SnapshotCannotMutateStorageAndOnlySuccessfulChangesRaiseEvents()
    {
        var stock = new WarehouseStock();
        int changes = 0;
        stock.Changed += () => changes++;
        stock.Add("ore", 2);
        StockRecord[] snapshot = stock.GetSnapshot();
        snapshot[0] = new StockRecord("ore", 999);
        Assert.That(stock.GetQuantity("ore"), Is.EqualTo(2));
        stock.TryRemove("ore", 3);
        Assert.That(changes, Is.EqualTo(1));
        stock.TryRemove("ore", 2);
        Assert.That(changes, Is.EqualTo(2));
        Assert.That(stock.GetSnapshot(), Is.Empty);
    }

    [Test]
    public void BuildingStocksAreIndependentAndSurviveDisableEnable()
    {
        var market = new GameObject("Market");
        var mine = new GameObject("Mine");
        try
        {
            var a = market.AddComponent<BuildingWarehouse>();
            var b = mine.AddComponent<BuildingWarehouse>();
            a.Add("ore", 5);
            Assert.That(b.GetQuantity("ore"), Is.Zero);
            market.SetActive(false);
            market.SetActive(true);
            Assert.That(a.GetQuantity("ore"), Is.EqualTo(5));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(market);
            UnityEngine.Object.DestroyImmediate(mine);
        }
    }
}
