using System.Globalization;
using Modulix.Core.Contracts.Localization;
using Modulix.Modules.Crm.Quotes;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Modulix.Modules.Crm.Endpoints;

/// <summary>
/// Müşteri teklif belgesi (PDF). Girdisi yalnızca sabitlenmiş belge verisidir (<see cref="QuoteDocumentSnapshot"/>): canlı katalog,
/// firma kartı, maliyet ve iç not buraya hiç girmez. Tutarlar sunucudaki hesap motorunun sonucudur; burada yeniden hesaplanmaz.
/// Üretilen dosya SHA-256 özetiyle saklanır; şablon değişse de eski belge eski haliyle kalır (<see cref="TemplateVersion"/>).
/// </summary>
public static class QuotePdf
{
    /// <summary>
    /// Şablon sürümü: görünüm değiştiğinde artırılır; belgeyle birlikte saklanır. 2: grup alt toplam satırı ve "ayrıca gösterilmeyen ek ücretleri içerir" notu
    /// (ek ücret sunum seçimi, ADR-0018 Dilim 2c); eski belgeler 1 olarak kalır.
    /// </summary>
    public const int TemplateVersion = 2;

    private static readonly NumberFormatInfo Numbers = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = ".", NumberGroupSizes = [3] };
    private static readonly string Accent = "#1F4E79";
    private static readonly string Muted = "#5F6B7A";
    private static readonly string Line = "#C9D1DA";

    static QuotePdf()
    {
        // QuestPDF Community lisansı: yıllık geliri 1 milyon USD altındaki şirketler ve açık kaynak için ücretsizdir; aşan kullanım ücretli
        // lisans gerektirir (docs/adr/0019-teklif-yonetimi.md).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <param name="t">Belgenin dili: müşterinin yazışma dili (ADR-0034 §6, dilim 22c). Sabit başlıklar bu dildedir; teklif içeriği girildiği gibidir.</param>
    public static byte[] Render(QuoteDocumentSnapshot quote, LocalizedTexts t) => Build(quote, t).GeneratePdf();

    /// <summary>Belge tanımı (PDF üretimi ve görsel denetim için ortak).</summary>
    public static QuestPDF.Infrastructure.IDocument Build(QuoteDocumentSnapshot quote, LocalizedTexts t)
    {
        var document = Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(32);
            page.DefaultTextStyle(style => style.FontSize(9).FontColor("#1B2430"));
            page.Header().Element(header => Header(header, quote, t));
            page.Content().PaddingTop(12).Element(content => Body(content, quote, t));
            page.Footer().Element(footer => Footer(footer, quote, t));
        }));

        // Üretim anı değil sunum anı: aynı veriden üretilen belgenin üst bilgisi sabit kalır.
        document.WithMetadata(new DocumentMetadata
        {
            Title = $"{quote.QuoteNumber} V{quote.RevisionNo} - {quote.Title}",
            Author = quote.Seller.Name,
            Subject = t.Get("crm.quote-pdf.subject"),
            Creator = "ModüliX",
            Producer = "ModüliX",
            CreationDate = quote.PresentedAt,
            ModifiedDate = quote.PresentedAt,
        });
        return document;
    }

    public static string Money(decimal amount, string currency) => $"{amount.ToString("N2", Numbers)} {currency}";

    private static string Date(DateTimeOffset value) => value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    private static string Quantity(decimal value, string? unit) => $"{value.ToString("#,0.####", Numbers)}{(unit is null ? string.Empty : $" {unit}")}";

    private static void Header(IContainer container, QuoteDocumentSnapshot quote, LocalizedTexts t) =>
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(seller =>
                {
                    seller.Item().Text(quote.Seller.Name).FontSize(16).Bold().FontColor(Accent);
                    if (quote.Seller.Details is { Length: > 0 } details)
                    {
                        seller.Item().Text(details).FontColor(Muted).FontSize(8);
                    }
                });
                row.ConstantItem(190).AlignRight().Column(info =>
                {
                    info.Item().Text(t.Get("crm.quote-pdf.heading")).FontSize(16).Bold().FontColor(Accent);
                    info.Item().Text(t.Get("crm.quote-pdf.number", quote.QuoteNumber, quote.RevisionNo)).FontSize(8);
                    info.Item().Text(t.Get("crm.quote-pdf.date", quote.IssueDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture))).FontSize(8);
                    if (quote.ValidUntil is { } until)
                    {
                        info.Item().Text(t.Get("crm.quote-pdf.valid-until", Date(until))).FontSize(8).Bold();
                    }
                });
            });
            column.Item().PaddingTop(6).LineHorizontal(1).LineColor(Accent);
        });

    private static void Body(IContainer container, QuoteDocumentSnapshot quote, LocalizedTexts t) =>
        container.Column(column =>
        {
            column.Spacing(10);
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(customer =>
                {
                    customer.Item().Text(t.Get("crm.quote-pdf.to")).FontSize(7).FontColor(Muted).Bold();
                    customer.Item().Text(quote.CustomerName).FontSize(11).Bold();
                    var detail = string.Join(" · ", new[] { quote.CustomerCity, quote.CustomerTaxNumber is null ? null : t.Get("crm.quote-pdf.tax-number", quote.CustomerTaxNumber) }.Where(p => p is not null));
                    if (detail.Length > 0)
                    {
                        customer.Item().Text(detail).FontColor(Muted);
                    }

                    var contact = string.Join(" · ", new[] { quote.ContactName, quote.ContactEmail }.Where(p => p is not null));
                    if (contact.Length > 0)
                    {
                        customer.Item().Text(t.Get("crm.public-quote.contact", contact));
                    }
                });
            });

            column.Item().Column(title =>
            {
                title.Item().Text(quote.Title).FontSize(12).Bold();
                if (quote.Description is { Length: > 0 } description)
                {
                    title.Item().PaddingTop(2).Text(description);
                }
            });

            column.Item().Element(content => Lines(content, quote, t));
            column.Item().AlignRight().Element(content => Totals(content, quote, t));
            column.Item().Element(content => Terms(content, quote, t));
        });

    private static void Lines(IContainer container, QuoteDocumentSnapshot quote, LocalizedTexts t) =>
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(22);
                columns.RelativeColumn(5);
                columns.RelativeColumn(1.6f);
                columns.RelativeColumn(1.8f);
                columns.RelativeColumn(1.5f);
                columns.RelativeColumn(1f);
                columns.RelativeColumn(2f);
            });
            table.Header(header =>
            {
                foreach (var (text, right) in new[] { ("#", false), (t.Get("crm.public-quote.col.item"), false), (t.Get("crm.public-quote.col.quantity"), true), (t.Get("crm.public-quote.col.unit-price"), true), (t.Get("crm.public-quote.col.discount"), true), (t.Get("crm.public-quote.col.vat"), true), (t.Get("crm.public-quote.col.net"), true) })
                {
                    var cell = header.Cell().Background(Accent).PaddingVertical(4).PaddingHorizontal(3);
                    (right ? cell.AlignRight() : cell).Text(text).FontColor(Colors.White).Bold().FontSize(8);
                }
            });

            // Gruba dahil edilen ek ücretin bulunduğu gruplar için alt toplam satırı, grubun son satırından sonra çizilir.
            var subtotals = (quote.GroupSubtotals ?? []).ToDictionary(g => g.GroupName, StringComparer.OrdinalIgnoreCase);
            var lastOfGroup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < quote.Lines.Count; i++)
            {
                if (quote.Lines[i].GroupName is { Length: > 0 } groupName)
                {
                    lastOfGroup[groupName] = i;
                }
            }

            string? group = null;
            var index = 0;
            foreach (var line in quote.Lines)
            {
                if (line.GroupName is { Length: > 0 } name && name != group)
                {
                    group = name;
                    table.Cell().ColumnSpan(7).Background("#EEF2F6").PaddingVertical(3).PaddingHorizontal(3).Text(name).Bold();
                }

                index++;
                var muted = !line.Included;
                IContainer Cell(IContainer c) => c.BorderBottom(0.5f).BorderColor(Line).PaddingVertical(4).PaddingHorizontal(3);
                table.Cell().Element(Cell).Text(index.ToString(CultureInfo.InvariantCulture)).FontColor(Muted);
                table.Cell().Element(Cell).Column(item =>
                {
                    item.Item().Text(text =>
                    {
                        text.Span(line.Name).SemiBold();
                        if (line.Code is { Length: > 0 } code)
                        {
                            text.Span($"  ({code})").FontColor(Muted).FontSize(8);
                        }
                    });
                    if (RoleLabel(line, t) is { } label)
                    {
                        item.Item().Text(label).FontSize(7.5f).Italic().FontColor(Muted);
                    }

                    if (line.Description is { Length: > 0 } description)
                    {
                        item.Item().Text(description).FontSize(8).FontColor(Muted);
                    }

                    if (line.RecurringPeriod is { Length: > 0 } period)
                    {
                        item.Item().Text(t.Get("crm.quote-pdf.recurring", PeriodLabel(period, t))).FontSize(7.5f).FontColor(Muted);
                    }
                });
                table.Cell().Element(Cell).AlignRight().Text(Quantity(line.Quantity, line.Unit));
                table.Cell().Element(Cell).AlignRight().Text(line.UnitPrice.ToString("N2", Numbers));
                table.Cell().Element(Cell).AlignRight().Text(DiscountText(line));
                table.Cell().Element(Cell).AlignRight().Text($"%{line.TaxRatePercent.ToString("0.##", Numbers)}");
                table.Cell().Element(Cell).AlignRight().Text(muted ? "—" : line.Net.ToString("N2", Numbers)).FontColor(muted ? Muted : "#1B2430");

                if (line.GroupName is { Length: > 0 } endName && lastOfGroup[endName] == index - 1 && subtotals.TryGetValue(endName, out var subtotal))
                {
                    table.Cell().ColumnSpan(6).Background("#F6F8FA").PaddingVertical(3).PaddingHorizontal(3).AlignRight()
                        .Text(t.Get("crm.public-quote.group-subtotal", subtotal.GroupName)).Bold().FontSize(8.5f);
                    table.Cell().Background("#F6F8FA").PaddingVertical(3).PaddingHorizontal(3).AlignRight().Text(subtotal.Net.ToString("N2", Numbers)).Bold();
                }
            }
        });

    private static void Totals(IContainer container, QuoteDocumentSnapshot quote, LocalizedTexts t) =>
        container.Width(240).Column(column =>
        {
            void Row(string label, string value, bool strong = false)
            {
                column.Item().PaddingVertical(2).Row(row =>
                {
                    var l = row.RelativeItem().Text(label);
                    var v = row.ConstantItem(110).AlignRight().Text(value);
                    if (strong)
                    {
                        l.Bold();
                        v.Bold();
                    }
                });
            }

            Row(t.Get("crm.public-quote.list-total"), Money(quote.ListTotal, quote.Currency));
            if (quote.DiscountTotal > 0)
            {
                Row(t.Get("crm.public-quote.discount-total"), "-" + Money(quote.DiscountTotal, quote.Currency));
            }

            Row(quote.PriceIncludesTax ? t.Get("crm.quote-pdf.net-total-excl-vat") : t.Get("crm.public-quote.net-total"), Money(quote.NetTotal, quote.Currency));
            foreach (var group in quote.TaxGroups)
            {
                Row(t.Get("crm.public-quote.vat-rate", group.RatePercent.ToString("0.##", Numbers)), Money(group.Tax, quote.Currency));
            }

            column.Item().PaddingVertical(2).LineHorizontal(1).LineColor(Accent);
            Row(t.Get("crm.quote-pdf.grand-total"), Money(quote.GrossTotal, quote.Currency), strong: true);
            if (quote.OptionalNet > 0)
            {
                column.Item().PaddingTop(4).Text(t.Get("crm.public-quote.optional-note", Money(quote.OptionalNet, quote.Currency)))
                    .FontSize(7.5f).FontColor(Muted);
            }

            if (quote.IncludesUnlistedCharges)
            {
                column.Item().PaddingTop(4).Text(t.Get("crm.public-quote.unlisted-charges-note")).FontSize(7.5f).FontColor(Muted);
            }
        });

    private static void Terms(IContainer container, QuoteDocumentSnapshot quote, LocalizedTexts t)
    {
        var terms = new[] { (t.Get("crm.public-quote.payment-terms"), quote.PaymentTerms), (t.Get("crm.public-quote.delivery-terms"), quote.DeliveryTerms), (t.Get("crm.public-quote.commercial-terms"), quote.CommercialTerms) }
            .Where(t => t.Item2 is { Length: > 0 }).ToList();
        if (terms.Count == 0)
        {
            return;
        }

        container.Column(column =>
        {
            column.Spacing(6);
            foreach (var (label, text) in terms)
            {
                column.Item().Column(item =>
                {
                    item.Item().Text(label).Bold().FontColor(Accent);
                    item.Item().Text(text!).FontSize(8.5f);
                });
            }
        });
    }

    private static void Footer(IContainer container, QuoteDocumentSnapshot quote, LocalizedTexts t) =>
        container.Column(column =>
        {
            column.Item().LineHorizontal(0.5f).LineColor(Line);
            column.Item().PaddingTop(3).Row(row =>
            {
                row.RelativeItem().Text(quote.Seller.FooterNote ?? string.Empty).FontSize(7.5f).FontColor(Muted);
                row.ConstantItem(140).AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(7.5f).FontColor(Muted));
                    text.Span(t.Get("crm.quote-pdf.page", quote.QuoteNumber, quote.RevisionNo) + " ");
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        });

    private static string? RoleLabel(QuoteSnapshotLine line, LocalizedTexts t) => line.Role switch
    {
        "Optional" => t.Get(line.IsSelected ? "crm.public-quote.role.optional-selected" : "crm.public-quote.role.optional"),
        "Alternative" => t.Get(line.IsSelected ? "crm.public-quote.role.alternative-selected" : "crm.public-quote.role.alternative"),
        "PackageChild" => t.Get("crm.public-quote.role.package-child"),
        _ => null,
    };

    private static string DiscountText(QuoteSnapshotLine line) => line.DiscountKind switch
    {
        "Percent" when line.DiscountValue > 0 => $"%{line.DiscountValue.ToString("0.##", Numbers)}",
        "Amount" when line.DiscountValue > 0 => line.DiscountValue.ToString("N2", Numbers),
        _ => "—",
    };

    private static string PeriodLabel(string period, LocalizedTexts t) => period switch
    {
        "Monthly" => t.Get("crm.quote-pdf.period.monthly"),
        "Quarterly" => t.Get("crm.quote-pdf.period.quarterly"),
        "Yearly" => t.Get("crm.quote-pdf.period.yearly"),
        _ => period,
    };
}
