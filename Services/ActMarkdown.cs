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

    /// <summary>Voor een heel document (Tools ▸ Claude Shares): gewone markdown, dus een enkele regelovergang is geen nieuwe regel.</summary>
    private static readonly MarkdownPipeline DocumentPijplijn = new MarkdownPipelineBuilder()
        .DisableHtml()
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

    /// <summary>#123 in gewone tekst: een verwijzing naar kaart 123 (niet midden in een woord of adres).</summary>
    private static readonly System.Text.RegularExpressions.Regex Kaart = new(@"(?<![\w&/#])#(\d{1,9})(?!\w)", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>De eigen adressen van de wiki: een ander artikel, of een bijlage/plaatje van een artikel. Niets anders.</summary>
    private static readonly System.Text.RegularExpressions.Regex WikiAdres = new(@"^wiki(\?artikel=\d{1,9}|/bijlage/\d{1,9})$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <param name="document">Een heel document in plaats van een tekstveld van het board: gewone regelovergangen en geen #123-kaartlinks.</param>
    /// <param name="wiki">Een artikel van de wiki: links naar een ander artikel blijven in dit venster, en een plaatje dat
    /// als bijlage bij een artikel staat (wiki/bijlage/nr) wordt getoond. Plaatjes van buiten blijven een gewone link.</param>
    public static string Html(string? markdown, bool document = false, bool wiki = false)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";
        var pijplijn = document ? DocumentPijplijn : Pijplijn;
        var doc = Markdown.Parse(markdown, pijplijn);

        // #123 wordt een link naar die kaart (activityboard?kaart=123 zoekt zelf het board erbij); niet in code of in een link
        foreach (var lit in document ? new List<LiteralInline>() : doc.Descendants<LiteralInline>().Where(l => !l.FindParentOfType<LinkInline>().Any()).ToList())
        {
            var tekst = lit.Content.ToString();
            var treffers = Kaart.Matches(tekst);
            if (treffers.Count == 0) continue;
            Inline laatste = lit;
            var pos = 0;
            void Na(Inline nieuw) { laatste.InsertAfter(nieuw); laatste = nieuw; }
            foreach (System.Text.RegularExpressions.Match m in treffers)
            {
                if (m.Index > pos) Na(new LiteralInline(tekst[pos..m.Index]));
                var link = new LinkInline($"activityboard?kaart={m.Groups[1].Value}", "");
                link.AppendChild(new LiteralInline(m.Value));
                link.GetAttributes().AddClass("ab-kaartlink");
                Na(link);
                pos = m.Index + m.Length;
            }
            if (pos < tekst.Length) Na(new LiteralInline(tekst[pos..]));
            lit.Remove();
        }

        foreach (var link in doc.Descendants<LinkInline>().ToList())
        {
            if (link.GetAttributes().Classes?.Contains("ab-kaartlink") == true) continue;      // de eigen verwijzing naar een kaart
            if (wiki && WikiAdres.IsMatch(link.Url ?? ""))
            {
                var bijlage = link.Url!.StartsWith(Wiki.BijlageAdres, StringComparison.Ordinal);
                if (link.IsImage && bijlage) { link.GetAttributes().AddClass("wk-plaatje"); continue; }
                link.IsImage = false;
                if (bijlage) link.GetAttributes().AddPropertyIfNotExist("target", "_blank");
                else link.GetAttributes().AddClass("wk-link");
                continue;
            }
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

        return doc.ToHtml(pijplijn);
    }
}
