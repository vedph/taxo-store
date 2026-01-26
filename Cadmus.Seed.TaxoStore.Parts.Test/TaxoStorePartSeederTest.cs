using Cadmus.Core;
using Cadmus.Seed;
using Cadmus.TaxoStore.Parts;
using Fusi.Tools;
using Fusi.Tools.Configuration;
using System;
using System.Reflection;
using Xunit;

namespace Cadmus.Seed.TaxoStore.Parts.Test;

public sealed class TaxoStorePartSeederTest
{
    private static readonly PartSeederFactory _factory =
        TestHelper.GetFactory();
    private static readonly SeedOptions _seedOptions =
        _factory.GetSeedOptions();
    private static readonly IItem _item =
        _factory.GetItemSeeder().GetItem(1, "facet");

    [Fact]
    public void TypeHasTagAttribute()
    {
        Type t = typeof(TaxoStoreNodesPartSeeder);
        TagAttribute? attr = t.GetTypeInfo().GetCustomAttribute<TagAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("seed.it.vedph.taxo-store-nodes", attr!.Tag);
    }

    [Fact]
    public void Seed_Ok()
    {
        TaxoStoreNodesPartSeeder seeder = new();
        seeder.SetSeedOptions(_seedOptions);
        seeder.Configure(new TaxoStoreNodesPartSeederOptions
        {
            TreeIds = ["animals"],
            Nodes =
            [
                new StringPair("dog", "animals/cat"),
                new StringPair("dog", "animals/dog"),
            ]
        });

        IPart? part = seeder.GetPart(_item, null, _factory);

        Assert.NotNull(part);

        TaxoStoreNodesPart? p = part as TaxoStoreNodesPart;
        Assert.NotNull(p);

        TestHelper.AssertPartMetadata(p!);

        Assert.NotNull(p!.TreeId);
        Assert.NotEmpty(p!.NodeIds);
    }
}