namespace Modulix.Core.Contracts.Modules;

/// <summary>
/// Modülün zamanlanmış işi (ADR-0046): host'taki tek zamanlayıcı işi <see cref="DefaultInterval"/> aralığıyla, modülü açık ve erişime açık
/// her tenant için ayrı ayrı çalıştırır. Kapsamı, tenant bağlamını, lisans kontrolünü, tenant başına kilidi ve kaydı zamanlayıcı yapar;
/// modül yalnızca işin kendisini (<see cref="IModuleJob"/>) yazar.
/// <list type="bullet">
///   <item><see cref="Key"/> modül anahtarıyla başlar (ör. "orders.jobs") ve değişmez: yapılandırma (<c>Jobs:&lt;anahtar&gt;:Enabled</c>,
///   <c>Jobs:&lt;anahtar&gt;:IntervalSeconds</c>), ölçüm adı ve tenant başına kilit bu anahtara bağlıdır.</item>
///   <item><see cref="Identity"/> işin sistem kimliğidir: modülün <see cref="IModule.EventSubscriptions"/> listesindeki bir aboneliğin anahtarı
///   (ör. "orders.scheduler"). İş oturumsuz çalışır ve yalnızca o aboneliğin bildirdiği izinleri taşır.</item>
///   <item><see cref="Runner"/> modülün <see cref="IModule.ConfigureServices"/> içinde scoped kaydettiği <see cref="IModuleJob"/> uygulamasıdır.</item>
///   <item><see cref="RequiresWritableModule"/> açıksa (varsayılan) lisansı salt okunur olan tenant için iş çalışmaz (ADR-0028).</item>
/// </list>
/// </summary>
public sealed record ModuleJobDefinition(string Key, TimeSpan DefaultInterval, Type Runner, string Identity, bool RequiresWritableModule = true)
{
    /// <summary>
    /// İşin eski yapılandırma bölümü (ör. "OrderJobs"): <c>&lt;bölüm&gt;:WorkerEnabled</c> ve <c>&lt;bölüm&gt;:PollIntervalSeconds</c>, yeni
    /// <c>Jobs:&lt;anahtar&gt;</c> ayarı verilmemişse okunmaya devam eder (on-prem yapılandırmaları kırılmasın). Yeni işte verilmez.
    /// </summary>
    public string? LegacySection { get; init; }

    public static ModuleJobDefinition For<TJob>(string key, TimeSpan defaultInterval, string identity, bool requiresWritableModule = true)
        where TJob : class, IModuleJob =>
        new(key, defaultInterval, typeof(TJob), identity, requiresWritableModule);
}

/// <summary>İşin çalıştığı tenant. Tenant bağlamı ve sistem kimliği zamanlayıcı tarafından kurulmuştur; iş servislerini kurucusundan alır.</summary>
public sealed record ModuleJobContext(Guid TenantId);

/// <summary>
/// Tur özeti. <see cref="Summary"/> doluysa zamanlayıcı günlüğe yazar (yalnızca bir şey yapıldığında doldurun; her turda gürültü olmasın).
/// <see cref="Details"/> modülün kendi özet nesnesidir; testler ve tanılama için taşınır, zamanlayıcı yorumlamaz.
/// </summary>
public sealed record ModuleJobResult(string? Summary = null, object? Details = null)
{
    public static ModuleJobResult Nothing { get; } = new();
}

/// <summary>
/// Zamanlanmış işin modüldeki uygulaması. Tek tenant için çalışır; değişiklikleri kendisi kaydetmez (zamanlayıcı tur sonunda kaydeder).
/// Aynı turun iki kez çalışması güvenli olmalıdır (kaçırılan tur bir sonrakinde yeniden denenir).
/// </summary>
public interface IModuleJob
{
    Task<ModuleJobResult> RunAsync(ModuleJobContext context, CancellationToken cancellationToken);
}
