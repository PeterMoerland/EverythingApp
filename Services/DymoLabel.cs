using Microsoft.JSInterop;

namespace EverythingApp.Services;

/// <summary>
/// Labelprinter-instellingen van déze pc (Beheer ▸ Labelprinter): printer en, bij een Twin
/// Turbo, in welke rol het kleine en het grote label zit. Opgeslagen in de browser
/// (localStorage via mdeOpslag), want dat hoort bij de werkplek en niet bij de app.
/// </summary>
public static class DymoLabel
{
    public const string Klein = "klein";   // 12 × 50 mm (99017)
    public const string Groot = "groot";   // 41 × 89 mm (11356)

    public record Instellingen(string Printer, string RolKlein, string RolGroot)
    {
        /// <summary>De rol voor een labelsoort: Left / Right / Auto.</summary>
        public string RolVoor(string label) => label == Groot ? RolGroot : RolKlein;
    }

    public static async Task<Instellingen> LeesAsync(IJSRuntime js)
    {
        string? printer = null, klein = null, groot = null;
        try
        {
            printer = await js.InvokeAsync<string?>("mdeOpslag.lees", "dymoPrinter");
            klein = await js.InvokeAsync<string?>("mdeOpslag.lees", "dymoRolKlein");
            groot = await js.InvokeAsync<string?>("mdeOpslag.lees", "dymoRolGroot");
            // oude instelling (één rol voor alles) als beginwaarde meenemen
            var oud = await js.InvokeAsync<string?>("mdeOpslag.lees", "dymoRol");
            klein ??= oud; groot ??= oud;
        }
        catch { }
        return new Instellingen(printer ?? "", Rol(klein), Rol(groot));
    }

    public static async Task SchrijfAsync(IJSRuntime js, string sleutel, string waarde)
    {
        try { await js.InvokeVoidAsync("mdeOpslag.schrijf", sleutel, waarde); } catch { }
    }

    private static string Rol(string? r) => r is "Left" or "Right" or "Auto" ? r : "Auto";

    public static string RolTekst(string rol) => rol switch { "Left" => "links", "Right" => "rechts", _ => "vanzelf" };
}
