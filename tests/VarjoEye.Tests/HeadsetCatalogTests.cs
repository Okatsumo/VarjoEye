using Xunit;
using VarjoEye.Core.Models;

namespace VarjoEye.Tests;

public sealed class HeadsetCatalogTests
{
    [Theory]
    [InlineData("Varjo Aero", "aero", "headset-aero.png")]
    [InlineData("Aero", "aero", "headset-aero.png")]
    [InlineData("Varjo VR-1", "vr-1", "headset-vr-1.png")]
    [InlineData("VR-3", "vr-3", "headset-vr-3.png")]
    [InlineData("Varjo XR-4", "xr-4", "headset-xr-4.png")]
    [InlineData("XR3", "xr-3", "headset-xr-3.png")]
    public void Resolve_KnownProductName_MapsAsset(string product, string id, string asset)
    {
        var model = HeadsetCatalog.Resolve(product);
        Assert.Equal(id, model.Id);
        Assert.Equal(asset, model.ImageAssetKey);
    }

    [Fact]
    public void Resolve_UnknownProduct_KeepsNameAndUnknownAsset()
    {
        var model = HeadsetCatalog.Resolve("Varjo Future HMD");
        Assert.Equal("unknown", model.Id);
        Assert.Equal("headset-unknown.png", model.ImageAssetKey);
        Assert.Equal("Varjo Future HMD", model.DisplayName);
    }

    [Fact]
    public void Resolve_Empty_UsesUnknownPlaceholder()
    {
        var model = HeadsetCatalog.Resolve(null);
        Assert.Equal(HeadsetCatalog.Unknown, model);
    }
}
