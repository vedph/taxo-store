using System;
using System.IO;
using Cadmus.Core;
using Cadmus.Core.Config;
using Fusi.Microsoft.Extensions.Configuration.InMemoryJson;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Hosting;
using Cadmus.TaxoStore.Parts;

namespace Cadmus.Seed.TaxoStore.Parts.Test;

static internal class TestHelper
{
    static public Stream GetResourceStream(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return Assembly.GetExecutingAssembly().GetManifestResourceStream(
            $"Cadmus.Seed.TaxoStore.Parts.Test.Assets.{name}")!;
    }

    static public string LoadResourceText(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        using StreamReader reader = new(GetResourceStream(name),
            Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static IHost GetHost(string config)
    {
        // map
        TagAttributeToTypeMap map = new();
        map.Add(
        [
            // Cadmus.Core
            typeof(StandardItemSortKeyBuilder).Assembly,
            // Cadmus.TaxoStore.Parts
            typeof(TaxoStoreNodesPart).Assembly
        ]);

        return new HostBuilder().ConfigureServices((hostContext, services) =>
        {
            PartSeederFactory.ConfigureServices(services,
                new StandardPartTypeProvider(map),
                    // Cadmus.Seed.TaxoStore.Parts
                    typeof(TaxoStoreNodesPartSeeder).Assembly);
        })
            // extension method from Fusi library
            .AddInMemoryJson(config)
            .Build();
    }

    static public PartSeederFactory GetFactory()
    {
        return new PartSeederFactory(GetHost(LoadResourceText("SeedConfig.json")));
    }

    static public void AssertPartMetadata(IPart part)
    {
        Assert.NotNull(part.Id);
        Assert.NotNull(part.ItemId);
        Assert.NotNull(part.UserId);
        Assert.NotNull(part.CreatorId);
    }
}
