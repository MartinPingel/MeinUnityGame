using System;
using NUnit.Framework;
using Village.Storage;

public sealed class WorkplaceToolsTests
{
    [Test]
    public void BrokenToolsAreRemovedOneByOneAndSpareStartsWithFullDurability()
    {
        var stock = new WarehouseStock(); stock.Add(WorkplaceTools.GoodsType, 2);
        var tools = new WorkplaceTools(stock, 100, 10);
        tools.Wear(600);
        Assert.That(stock.GetQuantity(WorkplaceTools.GoodsType), Is.EqualTo(1));
        Assert.That(tools.CurrentDurability, Is.EqualTo(100));
        tools.Wear(600);
        Assert.That(stock.GetQuantity(WorkplaceTools.GoodsType), Is.Zero);
        Assert.That(tools.CurrentDurability, Is.Zero);
        Assert.That(tools.HasUsableTool, Is.False);
        tools.Wear(600);
        Assert.That(stock.GetQuantity(WorkplaceTools.GoodsType), Is.Zero);
    }

    [Test]
    public void ReplenishingOrChangingOtherGoodsDoesNotRepairTheUsedTool()
    {
        var stock = new WarehouseStock(); stock.Add(WorkplaceTools.GoodsType, 1);
        var tools = new WorkplaceTools(stock, 100, 10); tools.Wear(300);
        stock.Add(WorkplaceTools.GoodsType, 2); stock.Add("Lebensmittel", 10);
        stock.TryRemove("Lebensmittel", 1); stock.TryRemove(WorkplaceTools.GoodsType, 1);
        Assert.That(tools.CurrentDurability, Is.EqualTo(50));
        stock.TryRemove(WorkplaceTools.GoodsType, 2);
        Assert.That(tools.CurrentDurability, Is.Zero);
        stock.Add(WorkplaceTools.GoodsType, 1);
        Assert.That(tools.CurrentDurability, Is.EqualTo(100));
    }

    [Test]
    public void WarehousesHaveIndependentDurabilityAndRejectInvalidRates()
    {
        var a = new WarehouseStock(); var b = new WarehouseStock();
        a.Add(WorkplaceTools.GoodsType, 1); b.Add(WorkplaceTools.GoodsType, 1);
        var first = new WorkplaceTools(a, 100, 10); var second = new WorkplaceTools(b, 100, 10);
        first.Wear(300);
        Assert.That(second.CurrentDurability, Is.EqualTo(100));
        Assert.Throws<ArgumentException>(() => new WorkplaceTools(a, 100, 0));
        Assert.Throws<ArgumentException>(() => new WorkplaceTools(a, double.NaN, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => first.Wear(-1));
        Assert.That(first.CurrentDurability, Is.EqualTo(50));
    }
}
