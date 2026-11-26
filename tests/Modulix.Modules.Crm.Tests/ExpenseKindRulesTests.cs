using Modulix.Modules.Crm.Domain;
using Modulix.Modules.Crm.Quotes;
using Modulix.Modules.Crm.Records;

namespace Modulix.Modules.Crm.Tests;

/// <summary>Masraf ürün türü (ADR-0018): katalog ve teklif satırı türü kümeleri, masraf satırının genel indirimden hariç tutulması (veritabanı gerektirmez).</summary>
public class ExpenseKindRulesTests
{
    [Fact]
    public void Catalog_and_quote_line_kinds_include_expense()
    {
        Assert.Contains(CatalogRules.KindExpense, CatalogRules.Kinds);
        Assert.Contains(QuoteLineKind.Expense, QuoteLineKind.All);
        Assert.Equal(CatalogRules.KindExpense, QuoteLineKind.Expense);
    }

    [Fact]
    public void Only_expense_and_rule_generated_lines_are_excluded_from_the_document_discount()
    {
        Assert.True(QuoteTotals.ToInput(new QuoteLine { Kind = QuoteLineKind.Expense, Quantity = 1m, UnitPrice = 50m }).ExcludedFromDocumentDiscount);
        Assert.True(QuoteTotals.ToInput(new QuoteLine { Kind = QuoteLineKind.Free, SourceRuleId = Guid.NewGuid(), Quantity = 1m, UnitPrice = 50m }).ExcludedFromDocumentDiscount);
        Assert.False(QuoteTotals.ToInput(new QuoteLine { Kind = QuoteLineKind.Service, Quantity = 1m, UnitPrice = 50m }).ExcludedFromDocumentDiscount);
        Assert.False(QuoteTotals.ToInput(new QuoteLine { Kind = QuoteLineKind.Product, Quantity = 1m, UnitPrice = 50m }).ExcludedFromDocumentDiscount);
    }
}
