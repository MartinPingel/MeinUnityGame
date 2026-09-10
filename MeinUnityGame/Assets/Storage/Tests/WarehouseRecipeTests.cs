using System;
using NUnit.Framework;
using Village.Storage;

public sealed class WarehouseRecipeTests
{
    [Test]
    public void ConversionIsAtomicAndPublishesOnlyTheFinalState()
    {
        var stock = new WarehouseStock();
        stock.Add("Eisen", 2);
        int events = 0;
        stock.Changed += () => {
            events++;
            Assert.That(stock.GetQuantity("Eisen"), Is.Zero);
            Assert.That(stock.GetQuantity("Werkzeuge"), Is.EqualTo(1));
        };
        Assert.That(stock.TryConvert("Eisen", 2, "Werkzeuge", 1), Is.True);
        Assert.That(events, Is.EqualTo(1));
        Assert.That(stock.TryConvert("Eisen", 2, "Werkzeuge", 1), Is.False);
        Assert.That(events, Is.EqualTo(1));
    }

    [Test]
    public void InsufficientInputOutputOverflowAndInvalidRecipesLeaveStockUnchanged()
    {
        var stock = new WarehouseStock();
        stock.Add("Eisen", 2);
        stock.Add("Werkzeuge", int.MaxValue);
        Assert.That(stock.CanConvert("Eisen", 2, "Werkzeuge", 1), Is.False);
        Assert.That(stock.TryConvert("Eisen", 2, "Werkzeuge", 1), Is.False);
        Assert.That(stock.TryConvert("Eisen", 3, "other", 1), Is.False);
        Assert.Throws<ArgumentException>(() => stock.TryConvert("Eisen", 1, "Eisen", 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => stock.TryConvert("Eisen", -1, "other", 1));
        Assert.That(stock.GetQuantity("Eisen"), Is.EqualTo(2));
        Assert.That(stock.GetQuantity("Werkzeuge"), Is.EqualTo(int.MaxValue));
        Assert.That(stock.GetQuantity("other"), Is.Zero);
    }
}
