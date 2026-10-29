using Xunit;
using VarjoEye.Core.Models;

namespace VarjoEye.Tests;

public sealed class HeadsetCatalogTests
{
    [Theory]
    [InlineData("Varjo Aero", "aero")]
    [InlineData("Aero", "aero")]
    [InlineData("Varjo VR-1", "vr-1")]
    [InlineData("VR-3", "vr-3")]
    [InlineData("Varjo XR-4", "xr-4")]
    [InlineData("XR3", "xr-3")]
    public void Resolve_KnownProductName_MapsAsset(string product, string id)
    {
        var model = HeadsetCatalog.Resolve(product);
        Assert.Equal(id, model.Id);
    }

    [Fact]
    public void Resolve_UnknownProduct_KeepsNameAndUnknownAsset()
    {
        var model = HeadsetCatalog.Resolve("Varjo Future HMD");
        Assert.Equal("unknown", model.Id);
        Assert.Equal("Varjo Future HMD", model.DisplayName);
    }

    [Fact]
    public void Resolve_Empty_UsesUnknownPlaceholder()
    {
        var model = HeadsetCatalog.Resolve(null);
        Assert.Equal(HeadsetCatalog.Unknown, model);
    }
}
