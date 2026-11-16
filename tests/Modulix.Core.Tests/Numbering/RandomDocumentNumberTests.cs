using Modulix.Core.Contracts.Numbering;

namespace Modulix.Core.Tests.Numbering;

/// <summary>ADR-0049 dilim 3a: sıralı seriye geçmeyen kayıt türlerinin ortak rastgele numara yardımcısı.</summary>
public class RandomDocumentNumberTests
{
    [Fact]
    public void Number_has_prefix_date_and_six_unambiguous_characters()
    {
        var at = new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

        var numbers = Enumerable.Range(0, 500).Select(_ => RandomDocumentNumber.New("LD", at)).ToList();

        Assert.All(numbers, n => Assert.Matches("^LD-20261116-[A-HJ-NP-Z2-9]{6}$", n));
        Assert.Equal(numbers.Count, numbers.Distinct().Count());
    }
}
