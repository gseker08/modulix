using Modulix.Modules.Orders.Lifecycle;

namespace Modulix.Modules.Orders.Tests;

/// <summary>Teklifteki masraf kalemi siparişe taşınırken teslim edilen kalem sayılmaz; hizmet gibi başlangıçla izlenir.</summary>
public class ExpenseLineKindTests
{
    [Fact]
    public void Expense_is_a_known_non_deliverable_service_like_kind()
    {
        Assert.Contains(LineKinds.Expense, LineKinds.All);
        Assert.False(LineKinds.IsDeliverable(LineKinds.Expense));
        Assert.True(LineKinds.IsService(LineKinds.Expense));
        Assert.True(LineKinds.IsDeliverable(LineKinds.Product));
    }
}
