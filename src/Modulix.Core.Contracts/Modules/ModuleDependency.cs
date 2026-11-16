namespace Modulix.Core.Contracts.Modules;

/// <summary>
/// Bir modülün başka bir modüle bağımlılığı (ADR-0051). <see cref="ModuleKey"/> sağlayan modülün anahtarıdır;
/// <see cref="Reason"/> arayüzde gösterilen cümledir (ör. "Kabul edilmiş tekliften sipariş oluşturma"). Beyan kodda sabittir.
/// </summary>
public sealed record ModuleDependency(string ModuleKey, ModuleDependencyKind Kind, string Reason);

public enum ModuleDependencyKind
{
    /// <summary>Sağlayan modül olmadan bu modül açılamaz.</summary>
    Required = 1,

    /// <summary>Sağlayan modül olmadan çalışır; <see cref="ModuleDependency.Reason"/>'daki özellik eksik kalır.</summary>
    Optional = 2,
}
