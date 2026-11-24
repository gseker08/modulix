using System.Globalization;

namespace Modulix.Core.Contracts.Pricing;

/// <summary>
/// Ek ücret kuralı türü (ADR-0018 devamı). Hepsi pozitif tutar üretir; indirim ayrı mekanizmadır (<c>DiscountEngine</c>).
/// <c>Fixed</c>: sabit tutar (× çarpan anahtarları, ör. beyanname adedi ya da saat × kişi). <c>Percent</c>: bir tabanın yüzdesi.
/// <c>Minimum</c>: grup toplamı asgari tutarın altındaysa farkı ekler. <c>Duration</c>: serbest sürenin üstündeki her birim için ücret
/// (demuraj, gecikme, gün aşımı; birim ücreti sabit ya da bir tabanın yüzdesi olabilir).
/// </summary>
public enum SurchargeKind
{
    Fixed,
    Percent,
    Minimum,
    Duration,
}

/// <summary>Kuralın teklifte nasıl görüneceği: ayrı satır, grubun satırına dahil ya da yalnızca hesap dökümü (müşteri belgesinde satır yok).</summary>
public enum SurchargePresentation
{
    SeparateLine,
    IncludedInGroup,
    BreakdownOnly,
}

public enum ConditionOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterOrEqual,
    LessThan,
    LessOrEqual,
}

/// <summary>Koşul: <paramref name="Key"/> bağlam değeri (teklifteki ya da satırdaki alan) <paramref name="Value"/> ile karşılaştırılır; tümü sağlanırsa kural çalışır.</summary>
public sealed record SurchargeCondition(string Key, ConditionOperator Operator, string Value);

/// <summary>
/// Ek ücret kuralı. Kural verisidir; kodda sektör ya da müşteri adı yoktur. Anahtarlar (<see cref="QuantityKeys"/>, <see cref="BaseKey"/>,
/// <see cref="DurationKey"/>, koşul anahtarları) bağlam değerlerinin adlarıdır; hangi teklif alanına karşılık geldiği bağlamı kuran katmandadır.
/// <see cref="Group"/> kuralın katkıda bulunduğu toplam grubudur ("ardiye" gibi); <see cref="SurchargeKind.Minimum"/> kendi grubunu denetler.
/// <see cref="Stackable"/> false ise kural çalıştığında aynı gruptaki sonraki (öncelik sırasıyla) kurallar uygulanmaz.
/// </summary>
public sealed record SurchargeRule(
    Guid Id,
    string Name,
    SurchargeKind Kind,
    string Currency,
    int Priority = 0,
    decimal? Amount = null,
    decimal? Percent = null,
    string? Group = null,
    string? BaseKey = null,
    string? DurationKey = null,
    decimal FreeUnits = 0m,
    IReadOnlyList<string>? QuantityKeys = null,
    IReadOnlyList<SurchargeCondition>? Conditions = null,
    SurchargePresentation Presentation = SurchargePresentation.SeparateLine,
    bool Stackable = true);

/// <summary>
/// Hesap bağlamı. <see cref="Numbers"/>: sayısal değerler (miktar, süre, teminat tutarı, <c>group.&lt;ad&gt;</c> grup toplamı, satır tutarı).
/// <see cref="Texts"/>: seçim/metin/evet-hayır alanları (<c>true</c>/<c>false</c> küçük harf). Koşullar önce metne, yoksa sayıya bakar.
/// </summary>
public sealed record SurchargeContext(
    string Currency,
    IReadOnlyDictionary<string, decimal> Numbers,
    IReadOnlyDictionary<string, string> Texts);

public sealed record SurchargeLine(
    Guid RuleId,
    string Name,
    SurchargeKind Kind,
    string? Group,
    SurchargePresentation Presentation,
    decimal Amount,
    string Explanation);

/// <summary>Uygulanmayan kural ve nedeni: satışçı "kural neden çalışmadı" sorusunu hesap dökümünde görür.</summary>
public sealed record SurchargeSkip(Guid RuleId, string Name, string Reason);

public sealed record SurchargeResult(
    IReadOnlyList<SurchargeLine> Applied,
    IReadOnlyList<SurchargeSkip> Skipped,
    IReadOnlyDictionary<string, decimal> GroupTotals)
{
    public decimal Total => Applied.Sum(a => a.Amount);
}

/// <summary>
/// Ek ücret motoru: saf ve deterministiktir (veritabanı, saat ve sektör bilgisi yok; <see cref="decimal"/>). Sıra: önce <c>Minimum</c> dışındaki
/// kurallar (öncelik, ad), sonra <c>Minimum</c> kuralları grubun o ana kadarki toplamıyla. Eksik girdi ya da uyuşmayan para birimi için tutar
/// uydurulmaz; kural nedeniyle "uygulanmadı" listesine yazılır. Tutarlar satır başına 2 hane (sıfırdan uzağa) yuvarlanır.
/// </summary>
public static class SurchargeEngine
{
    public static void Validate(SurchargeRule rule)
    {
        static void Fail(string message) => throw new PricingException(PricingErrors.InvalidRule, message);

        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            Fail("Kural adı boş olamaz.");
        }

        if (rule.Currency is not { Length: 3 } || !rule.Currency.All(char.IsAsciiLetterUpper))
        {
            Fail($"'{rule.Name}': para birimi 3 büyük harfli ISO kodu olmalıdır.");
        }

        if (rule.Amount is < 0 || rule.Percent is < 0 || rule.FreeUnits < 0)
        {
            Fail($"'{rule.Name}': tutar, yüzde ve serbest süre negatif olamaz.");
        }

        if ((rule.QuantityKeys ?? []).Any(string.IsNullOrWhiteSpace) || (rule.Conditions ?? []).Any(c => string.IsNullOrWhiteSpace(c.Key)))
        {
            Fail($"'{rule.Name}': anahtar adı boş olamaz.");
        }

        switch (rule.Kind)
        {
            case SurchargeKind.Fixed:
                if (rule.Amount is null || rule.Percent is not null)
                {
                    Fail($"'{rule.Name}': sabit tutarlı kural yalnızca tutar ister.");
                }

                break;
            case SurchargeKind.Percent:
                if (rule.Percent is not > 0 || rule.Amount is not null || string.IsNullOrWhiteSpace(rule.BaseKey))
                {
                    Fail($"'{rule.Name}': yüzdeli kural sıfırdan büyük yüzde ve taban anahtarı ister.");
                }

                break;
            case SurchargeKind.Minimum:
                if (rule.Amount is not > 0 || rule.Percent is not null || string.IsNullOrWhiteSpace(rule.Group) || (rule.QuantityKeys ?? []).Count > 0)
                {
                    Fail($"'{rule.Name}': asgari bedel kuralı sıfırdan büyük tutar ve grup ister, çarpan alamaz.");
                }

                break;
            case SurchargeKind.Duration:
                if (string.IsNullOrWhiteSpace(rule.DurationKey) || (rule.Amount is null) == (rule.Percent is null)
                    || (rule.Percent is not null && string.IsNullOrWhiteSpace(rule.BaseKey)))
                {
                    Fail($"'{rule.Name}': süre aşımı kuralı süre anahtarı ve birim ücreti (tutar ya da tabanlı yüzde, ikisi birlikte olmaz) ister.");
                }

                break;
            default:
                Fail($"'{rule.Name}': bilinmeyen kural türü.");
                break;
        }
    }

    public static SurchargeResult Evaluate(IEnumerable<SurchargeRule> rules, SurchargeContext context)
    {
        var all = rules.ToList();
        foreach (SurchargeRule rule in all)
        {
            Validate(rule);
        }

        var ordered = all.Where(r => r.Kind != SurchargeKind.Minimum).OrderBy(r => r.Priority).ThenBy(r => r.Name, StringComparer.Ordinal)
            .Concat(all.Where(r => r.Kind == SurchargeKind.Minimum).OrderBy(r => r.Priority).ThenBy(r => r.Name, StringComparer.Ordinal))
            .ToList();

        var applied = new List<SurchargeLine>();
        var skipped = new List<SurchargeSkip>();
        var groups = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var closedGroups = new HashSet<string>(StringComparer.Ordinal);

        decimal GroupTotal(string group) =>
            groups.TryGetValue(group, out var running) ? running : context.Numbers.GetValueOrDefault("group." + group);

        foreach (SurchargeRule rule in ordered)
        {
            string group = rule.Group ?? string.Empty;
            if (!string.Equals(rule.Currency, context.Currency, StringComparison.Ordinal))
            {
                skipped.Add(new(rule.Id, rule.Name, $"Kural {rule.Currency}, teklif {context.Currency} para biriminde."));
                continue;
            }

            if (closedGroups.Contains(group))
            {
                skipped.Add(new(rule.Id, rule.Name, "Aynı gruptaki birleşmeyen bir kural çalıştı."));
                continue;
            }

            if (FailedCondition(rule, context) is { } failed)
            {
                skipped.Add(new(rule.Id, rule.Name, failed));
                continue;
            }

            SurchargeLine? line = Compute(rule, context, GroupTotal, out string? reason);
            if (line is null)
            {
                skipped.Add(new(rule.Id, rule.Name, reason!));
                continue;
            }

            applied.Add(line);
            groups[group] = GroupTotal(group) + line.Amount;
            if (!rule.Stackable)
            {
                closedGroups.Add(group);
            }
        }

        return new SurchargeResult(applied, skipped, groups.Where(g => g.Key.Length > 0).ToDictionary(g => g.Key, g => g.Value));
    }

    private static SurchargeLine? Compute(SurchargeRule rule, SurchargeContext context, Func<string, decimal> groupTotal, out string? reason)
    {
        reason = null;
        decimal multiplier = 1m;
        foreach (string key in rule.QuantityKeys ?? [])
        {
            if (Number(context, key) is not { } value)
            {
                reason = $"Eksik değer: {key}.";
                return null;
            }

            multiplier *= value;
        }

        string factors = rule.QuantityKeys is { Count: > 0 } keys ? " × " + string.Join(" × ", keys) : string.Empty;
        switch (rule.Kind)
        {
            case SurchargeKind.Fixed:
                return Line(rule, rule.Amount!.Value * multiplier, $"{Fmt(rule.Amount!.Value)}{factors}", ref reason);

            case SurchargeKind.Percent:
            {
                if (Base(rule, context, groupTotal) is not { } baseAmount)
                {
                    reason = $"Eksik taban: {rule.BaseKey}.";
                    return null;
                }

                return Line(rule, baseAmount * rule.Percent!.Value / 100m * multiplier, $"%{Fmt(rule.Percent.Value)} × {Fmt(baseAmount)}{factors}", ref reason);
            }

            case SurchargeKind.Minimum:
            {
                decimal total = groupTotal(rule.Group!);
                if (total >= rule.Amount!.Value)
                {
                    reason = $"Asgari bedel aşıldı ({Fmt(total)} ≥ {Fmt(rule.Amount.Value)}).";
                    return null;
                }

                return Line(rule, rule.Amount.Value - total, $"asgari {Fmt(rule.Amount.Value)} − mevcut {Fmt(total)}", ref reason);
            }

            default:
            {
                if (Number(context, rule.DurationKey!) is not { } duration)
                {
                    reason = $"Eksik değer: {rule.DurationKey}.";
                    return null;
                }

                decimal units = duration - rule.FreeUnits;
                if (units <= 0)
                {
                    reason = $"Süre serbest süre içinde ({Fmt(duration)} ≤ {Fmt(rule.FreeUnits)}).";
                    return null;
                }

                decimal unitRate;
                string rateText;
                if (rule.Amount is { } flat)
                {
                    unitRate = flat;
                    rateText = Fmt(flat);
                }
                else
                {
                    if (Base(rule, context, groupTotal) is not { } baseAmount)
                    {
                        reason = $"Eksik taban: {rule.BaseKey}.";
                        return null;
                    }

                    unitRate = baseAmount * rule.Percent!.Value / 100m;
                    rateText = $"%{Fmt(rule.Percent.Value)} × {Fmt(baseAmount)}";
                }

                return Line(rule, unitRate * units * multiplier, $"{rateText} × {Fmt(units)} birim{factors}", ref reason);
            }
        }
    }

    private static SurchargeLine? Line(SurchargeRule rule, decimal raw, string how, ref string? reason)
    {
        decimal amount = MoneyRounding.Round(raw);
        if (amount <= 0)
        {
            reason = "Hesaplanan tutar sıfır.";
            return null;
        }

        return new SurchargeLine(rule.Id, rule.Name, rule.Kind, rule.Group, rule.Presentation, amount, $"{how} = {Fmt(amount)} {rule.Currency}");
    }

    private static decimal? Base(SurchargeRule rule, SurchargeContext context, Func<string, decimal> groupTotal)
    {
        string key = rule.BaseKey!;
        if (key.StartsWith("group.", StringComparison.Ordinal) && key.Length > "group.".Length)
        {
            return groupTotal(key["group.".Length..]);
        }

        return Number(context, key);
    }

    private static decimal? Number(SurchargeContext context, string key) =>
        context.Numbers.TryGetValue(key, out var value) ? value
        : context.Texts.TryGetValue(key, out var text) && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed
        : null;

    private static string? FailedCondition(SurchargeRule rule, SurchargeContext context)
    {
        foreach (SurchargeCondition condition in rule.Conditions ?? [])
        {
            if (!Matches(condition, context))
            {
                return $"Koşul sağlanmadı: {condition.Key} {Symbol(condition.Operator)} {condition.Value}.";
            }
        }

        return null;
    }

    private static bool Matches(SurchargeCondition condition, SurchargeContext context)
    {
        string? actual = context.Texts.TryGetValue(condition.Key, out var text) ? text
            : context.Numbers.TryGetValue(condition.Key, out var number) ? number.ToString(CultureInfo.InvariantCulture)
            : null;
        if (actual is null)
        {
            // Değeri olmayan alan hiçbir koşulu sağlamaz ("eşit değil" dahil): eksik veri sessizce "farklı" sayılmaz.
            return false;
        }

        bool numeric = decimal.TryParse(actual, NumberStyles.Number, CultureInfo.InvariantCulture, out var left)
            & decimal.TryParse(condition.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var right);
        bool equal = numeric ? left == right : string.Equals(actual, condition.Value, StringComparison.OrdinalIgnoreCase);
        return condition.Operator switch
        {
            ConditionOperator.Equals => equal,
            ConditionOperator.NotEquals => !equal,

            // Büyük/küçük karşılaştırması yalnızca iki taraf da sayıysa anlamlıdır.
            ConditionOperator.GreaterThan => numeric && left > right,
            ConditionOperator.GreaterOrEqual => numeric && left >= right,
            ConditionOperator.LessThan => numeric && left < right,
            _ => numeric && left <= right,
        };
    }

    private static string Symbol(ConditionOperator op) => op switch
    {
        ConditionOperator.Equals => "=",
        ConditionOperator.NotEquals => "≠",
        ConditionOperator.GreaterThan => ">",
        ConditionOperator.GreaterOrEqual => "≥",
        ConditionOperator.LessThan => "<",
        _ => "≤",
    };

    private static string Fmt(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
