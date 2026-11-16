namespace Modulix.Modules.Crm.Domain;

/// <summary>
/// Müşteri adayı puan kuralı (ADR-0050, dilim 1). Tek koşul (alan + işleç + değer) ve bir puan taşır; koşulu sağlayan her etkin kuralın puanı
/// toplanır, toplam 0..100 aralığına kırpılır (<c>LeadScoring.Calculate</c>). Puanlanabilir alanlar ve işleçler <c>LeadScoring</c>'deki beyaz listedir.
/// </summary>
public sealed class LeadScoreRule : CrmEntity
{
    public const int NameMaxLength = 150;
    public const int DescriptionMaxLength = 500;
    public const int FieldMaxLength = 40;
    public const int OperatorMaxLength = 20;
    public const int ValueMaxLength = 200;
    public const int MinPoints = -100;
    public const int MaxPoints = 100;

    public string Name { get; internal set; } = string.Empty;

    public string? Description { get; internal set; }

    public bool IsActive { get; internal set; } = true;

    /// <summary>Adayın puanlanan alanı (ör. "source", "industry", "email").</summary>
    public string Field { get; internal set; } = string.Empty;

    /// <summary>Karşılaştırma işleci: equals, notEquals, contains, isSet, isEmpty.</summary>
    public string Operator { get; internal set; } = LeadScoreOperator.EqualsTo;

    /// <summary>Karşılaştırılan değer; isSet/isEmpty işleçlerinde boştur.</summary>
    public string? Value { get; internal set; }

    /// <summary>-100..100, sıfır olamaz.</summary>
    public int Points { get; internal set; }
}

/// <summary>Puan kuralı işleçleri (ADR-0050).</summary>
public static class LeadScoreOperator
{
    public const string EqualsTo = "equals";
    public const string NotEquals = "notEquals";
    public const string Contains = "contains";
    public const string IsSet = "isSet";
    public const string IsEmpty = "isEmpty";

    public static readonly IReadOnlyList<string> All = [EqualsTo, NotEquals, Contains, IsSet, IsEmpty];

    /// <summary>Değer gerektirmeyen işleçler.</summary>
    public static bool IsPresenceCheck(string op) => op is IsSet or IsEmpty;
}
