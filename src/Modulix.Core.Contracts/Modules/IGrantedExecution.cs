namespace Modulix.Core.Contracts.Modules;

/// <summary>
/// Arka plan işinin bir iş parçasını "verilmiş izinlerle" çalıştırır (ADR-0056 dilim 1b-2). İş kendi sistem kimliğiyle çalışır ve kimliğin izinleri
/// yalnızca kendi modülünün anahtarlarından oluşur; başka modülün verisini (ör. segment koşulundaki CRM alanları) okuması gerekiyorsa, kaydın
/// sahibinin yayın anında kabul ettiği izin altkümesi burada verilir (yetki yükseltme yok: otomasyon kurallarındaki "yayınlayanın altkümesi" ilkesi).
/// <list type="bullet">
///   <item>Verilen kümedeki, işin kimliğinin kendi aboneliğinde bildirilmeyen her izin okuma izni olmalıdır (<c>.read</c> ile biter); başka bir
///   yazma izni verilemez, aksi hâlde <see cref="ArgumentException"/>.</item>
///   <item>İş parçası ayrı bir tenant kapsamında, yeni bir veritabanı bağlamıyla çalışır; kapsam sonunda değişiklikler kaydedilir. Kapsam
///   tenant genelidir (iş kimliğiyle aynı), izin kümesi verilen kümedir. Yalnızca bir iş ya da olay işleyicisinin içinden çağrılabilir.</item>
/// </list>
/// </summary>
public interface IGrantedExecution
{
    Task<T> RunAsync<T>(IReadOnlyCollection<string> grantedPermissions, Func<IServiceProvider, CancellationToken, Task<T>> work, CancellationToken cancellationToken);
}
