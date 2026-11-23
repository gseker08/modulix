using Modulix.Shared.Crm;

namespace Modulix.Web.Services;

/// <summary>
/// Ürünler listesindeki toplu aktif yapma: her ürün mevcut güncelleme ucuna (kayıt boru hattı) tam gövdeyle gönderilir; yalnızca durum
/// değişir. Zaten aktif olanlar atlanır (sunucu aynı duruma geçişi kabul eder, ancak gereksiz sürüm artışı ve denetim kaydı oluşmasın).
/// </summary>
public static class ProductBulkActivation
{
    public const string Active = "Active";

    public static bool NeedsActivation(ProductDto product) => product.Status != Active;

    /// <summary>Ürünün bütün alanlarını ve sürümünü koruyup durumu Aktif yapan kayıt isteği.</summary>
    public static SaveProductRequest ActivationRequest(ProductDto p) => new(
        p.Code, p.Name, p.BaseUnitId, p.Kind, Active, p.Description, p.CategoryId, p.ExternalCode, p.Barcode, p.Brand, p.Model, p.Tags,
        p.SaleStart, p.SaleEnd, p.TaxCategory, p.PriceIncludesTax, p.QuantityPrecision, p.Cost, p.CostCurrency ?? "TRY",
        p.ServiceDurationMinutes, p.DeliveryMode, p.ChargeType, p.RecurringPeriod, p.Version);
}
