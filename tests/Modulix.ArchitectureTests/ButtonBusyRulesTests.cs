namespace Modulix.ArchitectureTests;

/// <summary>
/// Arayüz tasarım kuralı (CLAUDE.md, düğmeler): kaydetme sırasında düğmeye <c>aria-busy="true"</c> verilir; ekran okuyucu ve çift tıklama
/// kullanıcısı işlemin sürdüğünü bu işaretten anlar. Kural <c>type="submit"</c> olan birincil (<c>btn-primary</c>) düğmeler için denetlenir.
/// Kuraldan önce yazılmış ekranlar <see cref="PendingCleanup"/> listesindedir (dosya → <c>aria-busy</c>'siz düğme sayısı); liste yalnızca küçülür:
/// yeni bir ihlal testi kırar, bir dosya temizlenince sayısı düşürülür (ya da satırı silinir), yoksa ikinci test kırılır.
/// </summary>
public sealed class ButtonBusyRulesTests
{
    /// <summary>
    /// Dosya (Modulix.Web'e göre, "/" ayraçlı) → <c>aria-busy</c>'siz birincil gönder düğmesi sayısı. Yeni satır eklenmez; düğmeye
    /// <c>aria-busy="@(saving ? "true" : "false")"</c> eklenip sayı düşürülür.
    /// </summary>
    private static readonly Dictionary<string, int> PendingCleanup = new()
    {
        ["Components/ActivityActions.razor"] = 1,
        ["Components/ActivityEditor.razor"] = 2,
        ["Components/AddressesSection.razor"] = 1,
        ["Components/CampaignResponsesPanel.razor"] = 1,
        ["Components/CargoEditor.razor"] = 2,
        ["Components/CarrierTermsEditor.razor"] = 2,
        ["Components/CompanyContactEditor.razor"] = 1,
        ["Components/CompanyRelationsSection.razor"] = 1,
        ["Components/ContactEditor.razor"] = 1,
        ["Components/ContactPointsSection.razor"] = 1,
        ["Components/ContractCommitmentsPanel.razor"] = 2,
        ["Components/ContractRenewalPanel.razor"] = 1,
        ["Components/ContractSignaturePanel.razor"] = 2,
        ["Components/CreateTenantForm.razor"] = 1,
        ["Components/CredentialEditor.razor"] = 2,
        ["Components/DriverEditor.razor"] = 2,
        ["Components/InviteUserForm.razor"] = 1,
        ["Components/LeadEditor.razor"] = 1,
        ["Components/MaintenanceEditor.razor"] = 2,
        ["Components/NewCompanyPerson.razor"] = 1,
        ["Components/OpportunityEditor.razor"] = 1,
        ["Components/OrderActionsPanel.razor"] = 1,
        ["Components/OrderChangeRequestsPanel.razor"] = 1,
        ["Components/OrderJobsPanel.razor"] = 1,
        ["Components/PartyRelationshipsSection.razor"] = 1,
        ["Components/PasswordForm.razor"] = 1,
        ["Components/PersonalViewBar.razor"] = 1,
        ["Components/QuoteSharingPanel.razor"] = 1,
        ["Components/ReportSchedulesPanel.razor"] = 1,
        ["Components/TenantBillingSection.razor"] = 1,
        ["Components/TenantPlanSection.razor"] = 1,
        ["Components/TenantScopeLimitsSection.razor"] = 1,
        ["Components/TenantSupportSection.razor"] = 1,
        ["Components/TicketActionsPanel.razor"] = 1,
        ["Components/TripEditor.razor"] = 1,
        ["Components/VehicleEditor.razor"] = 2,
        ["Pages/Admin/AuditLog.razor"] = 1,
        ["Pages/Admin/BusinessUnitDetail.razor"] = 1,
        ["Pages/Admin/BusinessUnits.razor"] = 1,
        ["Pages/Admin/ChoiceLists.razor"] = 1,
        ["Pages/Admin/CommunicationPreferences.razor"] = 4,
        ["Pages/Admin/Companies.razor"] = 1,
        ["Pages/Admin/CustomFields.razor"] = 1,
        ["Pages/Admin/RoleDetail.razor"] = 1,
        ["Pages/Admin/Settings.razor"] = 1,
        ["Pages/Admin/TeamDetail.razor"] = 1,
        ["Pages/Admin/Teams.razor"] = 1,
        ["Pages/Contracts/ContractApprovalRules.razor"] = 2,
        ["Pages/Contracts/ContractDetail.razor"] = 4,
        ["Pages/Contracts/ContractFromQuote.razor"] = 1,
        ["Pages/Contracts/ContractTemplates.razor"] = 2,
        ["Pages/Contracts/ContractWizard.razor"] = 2,
        ["Pages/Crm/DiscountPolicies.razor"] = 1,
        ["Pages/Crm/LeadForms.razor"] = 1,
        ["Pages/Crm/MeasureUnits.razor"] = 1,
        ["Pages/Crm/OpportunityReport.razor"] = 1,
        ["Pages/Crm/PriceCalculator.razor"] = 1,
        ["Pages/Crm/PriceLists.razor"] = 1,
        ["Pages/Crm/ProductCategories.razor"] = 1,
        ["Pages/Crm/Products.razor"] = 1,
        ["Pages/Crm/PublicQuote.razor"] = 1,
        ["Pages/Crm/QuoteDelegations.razor"] = 1,
        ["Pages/Crm/QuoteDetail.razor"] = 3,
        ["Pages/Crm/QuoteReport.razor"] = 1,
        ["Pages/Crm/QuoteRules.razor"] = 1,
        ["Pages/Crm/SalesForecastDetail.razor"] = 2,
        ["Pages/Crm/SalesForecasts.razor"] = 1,
        ["Pages/Crm/SalesTargets.razor"] = 1,
        ["Pages/Crm/TaxAndRates.razor"] = 1,
        ["Pages/Marketing/CampaignDetail.razor"] = 1,
        ["Pages/Marketing/CampaignNew.razor"] = 1,
        ["Pages/Marketing/CampaignReport.razor"] = 1,
        ["Pages/Marketing/SegmentDetail.razor"] = 2,
        ["Pages/Marketing/SegmentNew.razor"] = 1,
    };

    [Fact]
    public void Primary_submit_buttons_declare_aria_busy_except_the_pending_cleanup()
    {
        var violations = new List<string>();
        foreach (var (file, missing) in CountMissing())
        {
            var allowed = PendingCleanup.GetValueOrDefault(file);
            if (missing > allowed)
            {
                violations.Add($"{file}: {missing} (izin verilen {allowed})");
            }
        }

        Assert.True(
            violations.Count == 0,
            "aria-busy'siz birincil gönder düğmesi var:" + Environment.NewLine + string.Join(Environment.NewLine, violations)
            + Environment.NewLine + "Düğmeye aria-busy=\"@(busy ? \"true\" : \"false\")\" ekleyin; PendingCleanup'a yeni satır eklenmez.");
    }

    [Fact]
    public void Pending_cleanup_counts_are_still_needed()
    {
        var actual = CountMissing().ToDictionary(p => p.File, p => p.Missing);
        var stale = new List<string>();
        foreach (var (file, allowed) in PendingCleanup)
        {
            var missing = actual.GetValueOrDefault(file);
            if (missing < allowed)
            {
                stale.Add($"{file}: listede {allowed}, gerçekte {missing}; sayıyı düşürün" + (missing == 0 ? " (satırı silin)" : string.Empty));
            }
        }

        Assert.True(
            stale.Count == 0,
            "PendingCleanup güncel değil:" + Environment.NewLine + string.Join(Environment.NewLine, stale));
    }

    [Theory]
    [InlineData("<button type=\"submit\" class=\"btn btn-primary\">Kaydet</button>", 1)]
    [InlineData("<button class=\"btn btn-primary\" type=\"submit\" aria-busy=\"@(busy ? \"true\" : \"false\")\">Kaydet</button>", 0)]
    [InlineData("<button type=\"submit\" class=\"btn btn-primary\" disabled=\"@(a > b)\" @onclick=\"() => Save()\">Kaydet</button>", 1)]
    [InlineData("<button type=\"submit\" class=\"btn btn-primary\" @onclick=\"() => Save()\" aria-busy=\"@(busy ? \"true\" : \"false\")\">Kaydet</button>", 0)]
    [InlineData("<button type=\"button\" class=\"btn btn-primary\">Aç</button>", 0)]
    [InlineData("<button type=\"submit\" class=\"btn btn-outline-secondary\">Önizle</button>", 0)]
    [InlineData("<a class=\"btn btn-primary\" href=\"x\">Git</a>", 0)]
    public void The_scanner_reads_tags_with_arrows_nested_quotes_and_any_attribute_order(string markup, int expected)
    {
        Assert.Equal(expected, ButtonTags.CountPrimarySubmitWithoutBusy(markup));
    }

    private static IEnumerable<(string File, int Missing)> CountMissing()
    {
        var web = new DirectoryInfo(Path.Combine(RepositoryLayout.Root.FullName, "src", "Modulix.Web"));
        var separator = Path.DirectorySeparatorChar;
        foreach (var file in web.EnumerateFiles("*.razor", SearchOption.AllDirectories)
                     .Where(f => !f.FullName.Contains($"{separator}obj{separator}") && !f.FullName.Contains($"{separator}bin{separator}")))
        {
            var missing = ButtonTags.CountPrimarySubmitWithoutBusy(File.ReadAllText(file.FullName));
            if (missing > 0)
            {
                yield return (Path.GetRelativePath(web.FullName, file.FullName).Replace('\\', '/'), missing);
            }
        }
    }
}

/// <summary>
/// Razor işaretlemesinden <c>&lt;button …&gt;</c> açılış etiketlerini okur. Etiket sonu (<c>&gt;</c>) tırnak içinde ve <c>@( … )</c> ifadesi içinde sayılmaz:
/// <c>@onclick="() =&gt; Kaydet()"</c> ve <c>aria-busy="@(busy ? "true" : "false")"</c> gibi öznitelikler etiketi erken bitirmez.
/// </summary>
internal static class ButtonTags
{
    public static int CountPrimarySubmitWithoutBusy(string markup)
    {
        var count = 0;
        foreach (var tag in OpeningTags(markup))
        {
            if (tag.Contains("type=\"submit\"", StringComparison.Ordinal)
                && tag.Contains("btn-primary", StringComparison.Ordinal)
                && !tag.Contains("aria-busy", StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static IEnumerable<string> OpeningTags(string markup)
    {
        const string open = "<button";
        var index = 0;
        while ((index = markup.IndexOf(open, index, StringComparison.Ordinal)) >= 0)
        {
            var after = index + open.Length;
            if (after >= markup.Length || !(char.IsWhiteSpace(markup[after]) || markup[after] == '>'))
            {
                index = after;
                continue;
            }

            var end = TagEnd(markup, after);
            yield return markup.Substring(index, end - index);
            index = end;
        }
    }

    private static int TagEnd(string text, int start)
    {
        var inQuote = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuote && c == '@' && i + 1 < text.Length && text[i + 1] == '(')
            {
                i = SkipParentheses(text, i + 1);
                continue;
            }

            if (c == '"')
            {
                inQuote = !inQuote;
            }
            else if (c == '>' && !inQuote)
            {
                return i + 1;
            }
        }

        return text.Length;
    }

    // "@(" ifadesinin kapanış parantezini bulur (içindeki tırnak ve parantezler dikkate alınmaz); döndürdüğü indeks kapanış parantezidir.
    private static int SkipParentheses(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')' && --depth == 0)
            {
                return i;
            }
        }

        return text.Length - 1;
    }
}
