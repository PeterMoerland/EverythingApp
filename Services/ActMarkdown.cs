using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace EverythingApp.Services;

/// <summary>
/// Markdown in de tekstvelden van het Activity board (vraag Gerard 20-11-2026): kopjes, vet en cursief, lijsten, code en
/// links. De tekst komt van gebruikers en via de API en gaat als HTML het scherm in, dus:
/// - HTML in de tekst wordt niet doorgelaten (het komt als gewone tekst in beeld);
/// - een link mag alleen naar http, https of mailto; een andere link (javascript:, data:, file: …) wordt gewone tekst;
/// - een plaatje wordt een gewone link (geen plaatjes van buiten laden);
/// - links openen in een nieuw tabblad zonder de app mee te geven.
/// Een enkele regelovergang blijft een regelovergang.
/// </summary>
public static class ActMarkdown
{
    private static readonly MarkdownPipeline Pijplijn = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseSoftlineBreakAsHardlineBreak()
        .UseAutoLinks()
        .UsePipeTables()
        .Build();

    private static bool Veilig(string? url)
    {
        var u = (url ?? "").Trim();
        return u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || u.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
    }

    private static string TekstVan(ContainerInline c)
        => string.Concat(c.Descendants<LiteralInline>().Select(l => l.Content.ToString()));

    public static string Html(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";
        var doc = Markdown.Parse(markdown, Pijplijn);

        foreach (var link in doc.Descendants<LinkInline>().ToList())
        {
            if (!Veilig(link.Url))
            {
                // geen link maar de tekst die er stond (bij een lege tekst het adres zelf, zodat je ziet wat er stond)
                var tekst = TekstVan(link);
                link.ReplaceBy(new LiteralInline(tekst.Length > 0 ? tekst : link.Url ?? ""), copyChildren: false);
                continue;
            }
            link.IsImage = false;
            var attr = link.GetAttributes();
            attr.AddPropertyIfNotExist("target", "_blank");
            attr.AddPropertyIfNotExist("rel", "noopener noreferrer");
        }
        foreach (var auto in doc.Descendants<AutolinkInline>().ToList())
            if (!Veilig(auto.Url)) auto.ReplaceBy(new LiteralInline(auto.Url));

        return doc.ToHtml(Pijplijn);
    }
}
