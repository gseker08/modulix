namespace Modulix.Web.Tests;

/// <summary>
/// Kural düzenleyicisinde akıştaki "formda düzenle" düğmesi (ADR-0056, E6-2b) adım kartının aksiyon seçicisini kimliğiyle bulur (<c>action{sıra}</c>, sıra 1'den).
/// Kimlik biçimi iki yerde yazıldığı için biri değişirse düğme sessizce hiçbir şeye odaklanmaz; bu sözleşme testi ikisini birlikte tutar.
/// </summary>
public class AutomationEditorFocusContractTests
{
    private static string Source(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "Modulix.Web", "Pages", "Admin", "AutomationRuleEditor.razor")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, relative));
    }

    [Fact]
    public void The_step_selector_id_and_the_focus_call_use_the_same_format_and_the_script_exposes_the_function()
    {
        var editor = Source("src/Modulix.Web/Pages/Admin/AutomationRuleEditor.razor");
        var script = Source("src/Modulix.Web/wwwroot/js/shell.js");

        Assert.Contains("id=\"@($\"action{number}\")\"", editor);
        Assert.Contains("\"modulixShell.focusById\", $\"action{index + 1}\"", editor);
        Assert.Contains("focusById: focusById", script);
    }
}
