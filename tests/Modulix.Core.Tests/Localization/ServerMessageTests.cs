using System.Reflection;
using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Localization;

namespace Modulix.Core.Tests.Localization;

/// <summary>
/// ADR-0034 §6, dilim 18a: sunucu iletileri. Dil oturumdan çözülür (kullanıcı → tenant → tr), hata iletileri HTTP sınırında
/// anahtarından çevrilir; hata kodu ve alan adları değişmez, anahtarsız ileti olduğu gibi kalır.
/// </summary>
public class ServerMessageTests
{
    private readonly TextCatalog catalog = new();

    [Theory]
    [InlineData("en", "az", "en")]
    [InlineData(null, "az", "az")]
    [InlineData(null, null, "tr")]
    [InlineData("de", "en", "en")]
    [InlineData("", "xx", "tr")]
    [InlineData(" EN ", "az", "en")]
    public async Task Language_is_user_preference_then_tenant_default_then_turkish(string? user, string? tenant, string expected)
    {
        var language = new RequestLanguage(new FakePreferences(user, tenant));

        Assert.Equal(expected, await language.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Tenant_default_is_not_read_when_the_user_has_a_preference_and_the_result_is_cached()
    {
        var preferences = new FakePreferences("az", "en");
        var language = new RequestLanguage(preferences);

        Assert.Equal("az", await language.GetAsync(CancellationToken.None));
        Assert.Equal("az", await language.GetAsync(CancellationToken.None));
        Assert.Equal(1, preferences.UserReads);
        Assert.Equal(0, preferences.TenantReads);
    }

    [Fact]
    public async Task Background_job_without_user_or_tenant_gets_turkish()
    {
        var language = new RequestLanguage(new FakePreferences(null, null));

        Assert.Equal("tr", await language.GetAsync(CancellationToken.None));
    }

    [Fact]
    public void Record_error_is_translated_but_code_fields_and_field_codes_stay()
    {
        var error = new RecordError(RecordErrorCode.ValidationFailed, ServerMessages.Record.ValidationFailed,
        [
            new RecordFieldError("name", ServerMessages.Validation.Required),
            new RecordFieldError("code", ServerMessages.Validation.MaxLength(20)) { Code = "length" },
            new RecordFieldError("taxNumber", "Modüle özgü ileti."),
        ]);

        var english = catalog.Translate("en", error);

        Assert.Equal(RecordErrorCode.ValidationFailed, english.Code);
        Assert.Equal("Validation failed.", english.Message);
        Assert.Collection(english.FieldErrors!,
            e => Assert.Equal(("name", "This field is required."), (e.Field, e.Message)),
            e => Assert.Equal(("code", "Can be at most 20 characters.", "length"), (e.Field, e.Message, e.Code)),
            e => Assert.Equal(("taxNumber", "Modüle özgü ileti."), (e.Field, e.Message)));

        Assert.Equal("Doğrulama hatası.", catalog.Translate("tr", error).Message);
        Assert.Equal("Bu alan zorunludur.", catalog.Translate("tr", error).FieldErrors![0].Message);
    }

    [Fact]
    public void Error_without_keys_is_returned_unchanged()
    {
        var error = new RecordError(RecordErrorCode.Conflict, "Taslak olmayan teklif silinemez.");

        Assert.False(ServerMessageTranslation.IsTranslatable(error));
        Assert.Same(error, catalog.Translate("en", error));
    }

    [Fact]
    public void Placeholders_are_filled_in_the_target_language()
    {
        Assert.Equal("This action requires the 'crm.companies.read' permission.",
            catalog.Translate("en", ServerMessages.Access.PermissionRequired("crm.companies.read")));
        Assert.Equal("Unknown record type: crm.nothing",
            catalog.Translate("en", ServerMessages.Record.UnknownEntity("crm.nothing")));
    }

    [Fact]
    public void Unknown_key_or_broken_placeholder_falls_back_to_the_turkish_text()
    {
        Assert.Equal("Türkçe", catalog.Translate("en", new ServerMessage("Türkçe", "server.yok.boyle")));
        Assert.Equal("Türkçe", catalog.Translate("en", "server.validation.range", ["yalnız biri"], "Türkçe"));
    }

    [Fact]
    public void Conflict_and_access_exceptions_carry_the_message_key()
    {
        var conflict = new RecordConflictException(ServerMessages.Record.Concurrency);
        var plain = new RecordConflictException("Düz metin.");

        Assert.Equal("server.record.concurrency", conflict.ServerMessage.Key);
        Assert.Equal("Kayıt siz düzenlerken değişti. Güncel halini alıp tekrar deneyin.", conflict.Message);
        Assert.Null(plain.ServerMessage.Key);
        Assert.Equal("Düz metin.", plain.Message);
    }

    /// <summary>Test kapısı: her ortak iletinin anahtarı sözlükte var ve Türkçe metni <c>tr.json</c> kalıbıyla birebir aynı.</summary>
    [Fact]
    public void Every_server_message_matches_the_turkish_dictionary()
    {
        var source = TextCatalog.Load("tr");
        var checkedCount = 0;
        foreach (var (name, message) in AllServerMessages())
        {
            Assert.NotNull(message.Key);
            Assert.True(message.Key!.StartsWith("server.", StringComparison.Ordinal), name);
            Assert.True(source.ContainsKey(message.Key), $"{name}: '{message.Key}' tr.json'da yok.");
            Assert.Equal(catalog.Get("tr", message.Key, [.. message.Args]), message.Text);
            checkedCount++;
        }

        Assert.True(checkedCount >= 50);
    }

    [Fact]
    public void Every_server_key_in_the_dictionary_is_used_by_a_server_message()
    {
        var used = AllServerMessages().Select(m => m.Message.Key).ToHashSet();
        var unused = TextCatalog.Load("tr").Keys.Where(k => k.StartsWith("server.", StringComparison.Ordinal) && !used.Contains(k)).ToList();

        Assert.True(unused.Count == 0, "Kullanılmayan sunucu anahtarları: " + string.Join(", ", unused));
    }

    private static IEnumerable<(string Name, ServerMessage Message)> AllServerMessages()
    {
        foreach (var group in typeof(ServerMessages).GetNestedTypes(BindingFlags.Public))
        {
            foreach (var property in group.GetProperties(BindingFlags.Public | BindingFlags.Static).Where(p => p.PropertyType == typeof(ServerMessage)))
            {
                yield return ($"{group.Name}.{property.Name}", (ServerMessage)property.GetValue(null)!);
            }

            foreach (var method in group.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.ReturnType == typeof(ServerMessage) && !m.IsSpecialName))
            {
                var args = method.GetParameters().Select(p => p.ParameterType == typeof(string) ? "x" + p.Position : Convert.ChangeType(7, p.ParameterType, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                yield return ($"{group.Name}.{method.Name}", (ServerMessage)method.Invoke(null, args)!);
            }
        }
    }

    private sealed class FakePreferences(string? user, string? tenant) : ILanguagePreferences
    {
        public int UserReads { get; private set; }

        public int TenantReads { get; private set; }

        public Task<string?> GetUserPreferenceAsync(CancellationToken cancellationToken)
        {
            UserReads++;
            return Task.FromResult(user);
        }

        public Task<string?> GetTenantDefaultAsync(CancellationToken cancellationToken)
        {
            TenantReads++;
            return Task.FromResult(tenant);
        }
    }
}
