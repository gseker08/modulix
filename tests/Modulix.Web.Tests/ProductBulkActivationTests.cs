using Modulix.Shared.Crm;
using Modulix.Web.Services;

namespace Modulix.Web.Tests;

/// <summary>Ürünler listesindeki toplu aktif yapma: istek yalnızca durumu değiştirir, sürümü ve diğer alanları korur; aktif olanlar atlanır.</summary>
public class ProductBulkActivationTests
{
    private static ProductDto Product(string status = "Draft", string? costCurrency = "USD") => new(
        Guid.NewGuid(), 7, "S-1", "Şehir içi dağıtım", "Açıklama", "Service", status, Guid.NewGuid(), "Kategori", Guid.NewGuid(), "ADT",
        "EXT-1", "869000", "Marka", "Model", "etiket", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "KDV20", true, 2, 42.5m, costCurrency,
        90, "OnSite", "Recurring", "Monthly");

    [Fact]
    public void Request_sets_status_active_and_keeps_every_other_field_and_the_version()
    {
        var p = Product();

        var r = ProductBulkActivation.ActivationRequest(p);

        Assert.Equal("Active", r.Status);
        Assert.Equal(p.Version, r.Version);
        Assert.Equal((p.Code, p.Name, p.BaseUnitId, p.Kind, p.Description, p.CategoryId), (r.Code, r.Name, r.BaseUnitId, r.Kind, r.Description, r.CategoryId));
        Assert.Equal((p.ExternalCode, p.Barcode, p.Brand, p.Model, p.Tags), (r.ExternalCode, r.Barcode, r.Brand, r.Model, r.Tags));
        Assert.Equal((p.SaleStart, p.SaleEnd, p.TaxCategory, p.PriceIncludesTax, p.QuantityPrecision), (r.SaleStart, r.SaleEnd, r.TaxCategory, r.PriceIncludesTax, r.QuantityPrecision));
        Assert.Equal((p.Cost, p.CostCurrency), (r.Cost, r.CostCurrency));
        Assert.Equal((p.ServiceDurationMinutes, p.DeliveryMode, p.ChargeType, p.RecurringPeriod), (r.ServiceDurationMinutes, r.DeliveryMode, r.ChargeType, r.RecurringPeriod));
    }

    [Fact]
    public void Missing_cost_currency_falls_back_to_try()
    {
        Assert.Equal("TRY", ProductBulkActivation.ActivationRequest(Product(costCurrency: null)).CostCurrency);
    }

    [Theory]
    [InlineData("Draft", true)]
    [InlineData("Inactive", true)]
    [InlineData("Withdrawn", true)]
    [InlineData("Active", false)]
    public void Only_products_that_are_not_already_active_need_activation(string status, bool expected)
    {
        Assert.Equal(expected, ProductBulkActivation.NeedsActivation(Product(status)));
    }
}
