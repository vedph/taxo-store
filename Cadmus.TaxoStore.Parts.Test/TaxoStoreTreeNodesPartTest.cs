using System;
using Cadmus.Core;
using System.Collections.Generic;
using Cadmus.Seed.TaxoStore.Parts;
using Fusi.Tools;

namespace Cadmus.TaxoStore.Parts.Test;

public sealed class TaxoStoreTreeNodesPartTest
{
    private static TaxoStoreNodesPart GetPart()
    {
        TaxoStoreNodesPartSeeder seeder = new();
        seeder.Configure(new TaxoStoreNodesPartSeederOptions
        {
            TreeIds = ["animals"],
            Nodes =
            [
                new StringPair("dog", "animals/cat"),
                new StringPair("dog", "animals/dog"),
            ]
        });

        IItem item = new Item
        {
            FacetId = "default",
            CreatorId = "zeus",
            UserId = "zeus",
            Description = "Test item",
            Title = "Test Item",
            SortKey = ""
        };
        return (TaxoStoreNodesPart)seeder.GetPart(item, null, null)!;
    }

    private static TaxoStoreNodesPart GetEmptyPart()
    {
        return new TaxoStoreNodesPart
        {
            ItemId = Guid.NewGuid().ToString(),
            RoleId = "some-role",
            CreatorId = "zeus",
            UserId = "another",
        };
    }

    [Fact]
    public void Part_Is_Serializable()
    {
        TaxoStoreNodesPart part = GetPart();

        string json = TestHelper.SerializePart(part);
        TaxoStoreNodesPart part2 = TestHelper.DeserializePart<TaxoStoreNodesPart>(json)!;

        Assert.Equal(part.Id, part2.Id);
        Assert.Equal(part.TypeId, part2.TypeId);
        Assert.Equal(part.ItemId, part2.ItemId);
        Assert.Equal(part.RoleId, part2.RoleId);
        Assert.Equal(part.CreatorId, part2.CreatorId);
        Assert.Equal(part.UserId, part2.UserId);
    }

     [Fact]
    public void GetDataPins_NoNode_Empty()
    {
        TaxoStoreNodesPart part = GetEmptyPart();
        Assert.Empty(part.GetDataPins());
    }

    [Fact]
    public void GetDataPins_Nodes_Ok()
    {
        TaxoStoreNodesPart part = GetEmptyPart();
        part.TreeId = "animals";
        part.NodeIds.Add(new StringPair("animals: cat", "cat"));
        part.NodeIds.Add(new StringPair("animals: dog", "dog"));

        List<DataPin> pins = [.. part.GetDataPins(null)];
        Assert.Equal(2, pins.Count);

        // animals/cat
        DataPin? pin = pins.Find(p => p.Name == "node-key" && p.Value == "animals/cat");
        Assert.NotNull(pin);
        TestHelper.AssertPinIds(part, pin!);

        // animals/dog
        pin = pins.Find(p => p.Name == "node-key" && p.Value == "animals/dog");
        Assert.NotNull(pin);
        TestHelper.AssertPinIds(part, pin!);
    }
}
