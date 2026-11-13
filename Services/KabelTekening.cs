using System.Globalization;
using System.Text;
using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>
/// Tekent een <see cref="KabelSpec"/> als SVG in de opmaak van de tekeningen van LDZY: schets met maten, Wiring Table,
/// stuklijst, eisen, uitleg van het nummer en een titelblok. Engels op de tekening (voor de leverancier), Nederlands in
/// het scherm. Staat bewust in een .cs en niet in een .razor: de letterlijke SVG-tekst-tag mag daar niet in @code staan
/// (CLAUDE.md, Razor-valkuilen). De schets is schematisch, niet op schaal (zoals bij LDZY zelf).
/// <see cref="SpecTekst"/> geeft dezelfde inhoud als platte tekst, voor in een e-mail.
/// </summary>
public static class KabelTekening
{
    public const int Breedte = 1400, Hoogte = 900;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const string Maatkleur = "#b0177b";   // maten in magenta, zoals op de tekening van LDZY

    public static string Maak(KabelSpec k, string getekendDoor, DateTime? datum = null)
    {
        var s = new Svg(Breedte, Hoogte);
        s.Raw("<defs><marker id=\"kabelpijl\" viewBox=\"0 0 10 10\" refX=\"10\" refY=\"5\" markerWidth=\"7\" markerHeight=\"7\" " +
              "orient=\"auto-start-reverse\"><path d=\"M0,0 L10,5 L0,10 z\" fill=\"#111\"/></marker></defs>");
        s.Rect(0, 0, Breedte, Hoogte, "#fff");
        s.Rect(10, 10, Breedte - 20, Hoogte - 20, "none", "#111", 1.5);
        Schets(s, k);
        const double onder = 345;
        WiringTable(s, k, 20, onder, 440);
        var bomOnder = Stuklijst(s, k, 480, onder, 420);
        Eisen(s, k, 480, bomOnder + 18, 420);
        NummerUitleg(s, k, 920, onder, 460);
        Bestelinfo(s, k, 920, 600, 460);
        Titelblok(s, k, getekendDoor, datum ?? DateTime.Today);
        return s.ToString();
    }

    // ---------------------------------------------------------------- schets bovenin

    private static void Schets(Svg s, KabelSpec k)
    {
        const double cy = 205;                      // hartlijn van de kabel
        const double p1X = 130, p2X = 1270;         // buitenkant van de uiteinden
        const double kousB = 70;                    // breedte van een krimpkousblok
        const double kousL = 330, kousR = 1000;     // linkerrand van het linker- en rechterblok
        var mantelX0 = k.HeeftKous(k.P1) ? kousL + kousB : kousL;
        var mantelX1 = k.HeeftKous(k.P2) ? kousR : kousR + kousB;
        if (k.IsLosseAders)
        {
            // losse aders: elke ader als eigen lijn van de ene kant naar de andere, naast elkaar in een bundel
            for (var i = 0; i < k.Aders.Count; i++)
            {
                var y = BundelY(k, i, cy);
                s.Line(mantelX0, y, mantelX1, y, "#777", 3.2);
                s.Line(mantelX0, y, mantelX1, y, KabelKleuren.Hex(k.Aders[i].Kleur), 2);
            }
        }
        else
        {
            var mantel = KabelKleuren.Hex(k.Mantelkleur);
            if (mantel == KabelKleuren.Hex("zwart")) mantel = "#4a4a4a";
            s.Rect(mantelX0, cy - 8, mantelX1 - mantelX0, 16, mantel, "#111");
        }
        // totale lengte
        s.Line(p1X, cy - 135, p1X, cy - 80, "#111", .7);
        s.Line(p2X, cy - 135, p2X, cy - 80, "#111", .7);
        DimH(s, p1X, p2X, cy - 128, $"{k.Lengte}±{k.LengteTolerantie} MM", 16);
        Eind(s, k, k.P1, true, p1X, cy, kousL, kousB, "P1");
        Eind(s, k, k.P2, false, p2X, cy, kousR, kousB, "P2");
        // aanwijzer A: de kabel (of de bundel losse aders)
        s.Line(700, k.IsLosseAders ? BundelY(k, k.Aders.Count - 1, cy) + 2 : cy + 8, 760, cy + 70, "#111", .8);
        s.Line(760, cy + 70, 800, cy + 70, "#111", .8);
        s.Text(806, cy + 75, "A", 16);
    }

    /// <summary>De hoogte van ader <paramref name="index"/> in het rechte stuk: bij een kabel de hartlijn (alle aders komen
    /// uit de mantel), bij losse aders een eigen baan in de bundel (hooguit 64 hoog, anders dichter op elkaar).</summary>
    private static double BundelY(KabelSpec k, int index, double cy)
    {
        if (!k.IsLosseAders) return cy;
        var n = Math.Max(1, k.Aders.Count);
        var stap = Math.Min(7.0, 64.0 / n);
        return cy + (index - (n - 1) / 2.0) * stap;
    }

    /// <param name="links">P1 (links) of P2 (rechts).</param>
    /// <param name="buitenX">De buitenkant van dit uiteinde.</param>
    /// <param name="kousX">Linkerrand van het krimpkousblok van deze kant.</param>
    private static void Eind(Svg s, KabelSpec k, KabelEind e, bool links, double buitenX, double cy, double kousX, double kousB, string naam)
    {
        var r = links ? 1 : -1;                              // richting naar het midden van de kabel
        const double body = 60;
        var binnenX = buitenX + r * body;                    // de kant van de connector waar de aders in gaan
        var kousNaar = links ? kousX : kousX + kousB;        // rand van het kousblok aan de connectorkant
        var kousVan = links ? kousX + kousB : kousX;         // rand aan de kabelkant
        s.Text(buitenX - r * 55, cy + 7, naam, 20, "middle");
        var kous = k.HeeftKous(e);

        if (e.IsConnector)
        {
            var n = Math.Max(1, e.Pinnen);
            var h = Math.Clamp(16 * n + 20, 56, 150);
            var bodyX = links ? buitenX : buitenX - body;
            var pinY = new double[n];
            for (var i = 0; i < n; i++) pinY[i] = cy - h / 2 + 10 + (i + 0.5) * (h - 20) / n;
            // aders van de kous naar de pinnen (eerst, zodat de connector eroverheen ligt)
            for (var ai = 0; ai < k.Aders.Count; ai++)
            {
                var a = k.Aders[ai];
                var pin = links ? a.PinP1 : a.PinP2;
                if (pin is not int p || p < 1 || p > n) continue;
                var idx = links ? n - p : p - 1;
                var vanY = BundelY(k, ai, cy);
                s.Line(kousNaar, vanY, binnenX, pinY[idx], "#777", 3.2);     // randje, anders is een witte ader onzichtbaar
                s.Line(kousNaar, vanY, binnenX, pinY[idx], KabelKleuren.Hex(a.Kleur), 2);
            }
            s.Rect(bodyX, cy - h / 2, body, h, "#fff", "#111", 1.5, 4);
            s.Rect(bodyX + (links ? 6 : body - 14), cy - h / 2 + 6, 8, h - 12, "#fff", "#111", .8);   // vergrendelnok
            for (var i = 0; i < n; i++) s.Rect(links ? binnenX - 14 : binnenX + 4, pinY[i] - 3.5, 10, 7, "#fff", "#111", .8);
            s.Text(buitenX - r * 8, pinY[0] + 4, (links ? n : 1).ToString(), 11, links ? "end" : "start");
            if (n > 1) s.Text(buitenX - r * 8, pinY[n - 1] + 4, (links ? 1 : n).ToString(), 11, links ? "end" : "start");
            if (k.AdersLijmen)
            {
                // lijm bij de ingang van de aders (over de aders heen getekend)
                s.Rect(links ? binnenX : binnenX - 13, cy - h / 2 + 7, 13, h - 14, "#f2c94c", "#8a6d00", .8, 4);
                s.Text(binnenX + r * 6.5, cy - h / 2 - 5, "GLUE", 10, "middle", "fill=\"#8a6d00\" font-weight=\"600\"");
            }
            // aanwijzer B,C: housing en terminal
            var ax = bodyX + body / 2;
            s.Line(ax, cy + h / 2, ax + r * 30, cy + h / 2 + 40, "#111", .8);
            s.Line(ax + r * 30, cy + h / 2 + 40, ax + r * 55, cy + h / 2 + 40, "#111", .8);
            s.Text(ax + r * 42, cy + h / 2 + 58, "B,C", 15, "middle");
            if (kous)
            {
                // afstand connector -> kous, boven de kabel
                s.Line(binnenX, cy - h / 2, binnenX, cy - 95, "#111", .7);
                s.Line(kousNaar, cy - 13, kousNaar, cy - 95, "#111", .7);
                DimH(s, Math.Min(binnenX, kousNaar), Math.Max(binnenX, kousNaar), cy - 88, $"{e.KrimpkousAfstand}±5 MM", 12);
            }
        }
        else
        {
            var n = Math.Max(1, k.Aders.Count);
            var h = Math.Clamp(12 * n + 10, 40, 150);
            var tipX = buitenX + r * 10;
            for (var i = 0; i < n; i++)
            {
                var y = cy - h / 2 + 5 + (i + 0.5) * (h - 10) / n;
                var kleur = i < k.Aders.Count ? KabelKleuren.Hex(k.Aders[i].Kleur) : "#888";
                s.Line(kousNaar, BundelY(k, i, cy), tipX + r * 22, y, "#777", 3.2);
                s.Line(kousNaar, BundelY(k, i, cy), tipX + r * 22, y, kleur, 2);
                s.Line(tipX + r * 22, y, tipX, y, "#8c8c8c", 3.5);
                if (i == 0 || i == n - 1) s.Text(buitenX - r * 8, y + 4, (i + 1).ToString(), 11, links ? "end" : "start");
            }
            s.Text(buitenX + r * 60, cy + h / 2 + 24, $"strip {e.StripLengte} mm, {Afwerking(e)}", 11, "middle");
            if (kous)
            {
                s.Line(kousNaar, cy - 13, kousNaar, cy - 95, "#111", .7);
                s.Line(buitenX, cy - h / 2, buitenX, cy - 95, "#111", .7);
                DimH(s, Math.Min(buitenX, kousNaar), Math.Max(buitenX, kousNaar), cy - 88, $"{e.KrimpkousAfstand}±5 MM", 12);
            }
        }

        if (kous)
        {
            s.Rect(kousX, cy - 13, kousB, 26, KabelKleuren.Hex(e.KrimpkousKleur), "#111");
            s.Line(kousX, cy + 13, kousX, cy + 50, "#111", .7);
            s.Line(kousX + kousB, cy + 13, kousX + kousB, cy + 50, "#111", .7);
            DimH(s, kousX, kousX + kousB, cy + 44, $"{e.KrimpkousLengte}±5 MM", 12);
            // aanwijzer D
            s.Line(kousVan, cy + 12, kousVan + r * 28, cy + 78, "#111", .8);
            s.Line(kousVan + r * 28, cy + 78, kousVan + r * 50, cy + 78, "#111", .8);
            s.Text(kousVan + r * 39, cy + 95, "D", 15, "middle");
        }
    }

    private static void DimH(Svg s, double x1, double x2, double y, string tekst, double grootte)
    {
        s.Line(x1, y, x2, y, "#111", .9, "marker-start=\"url(#kabelpijl)\" marker-end=\"url(#kabelpijl)\"");
        s.Text((x1 + x2) / 2, y - 5, tekst, grootte, "middle", "fill=\"" + Maatkleur + "\"");
    }

    // ---------------------------------------------------------------- tabellen

    private static void WiringTable(Svg s, KabelSpec k, double x, double y, double w)
    {
        s.Text(x + w / 2, y + 14, "Wiring Table", 15, "middle", "font-weight=\"600\"");
        var top = y + 24;
        var n = k.Aders.Count;
        var rowH = n == 0 ? 22 : Math.Min(22, 360.0 / n);
        var h = 30 + rowH * Math.Max(n, 1) + 6;
        s.Rect(x, top, w, h, "none", "#111");
        double cP1 = x + 36, lijnA = x + 80, lijnB = x + 280, cP2 = x + 324, cKleur = x + 366;
        s.Text(cP1, top + 19, k.P1.IsConnector ? "P1" : "P1 (open)", 13, "middle", "font-weight=\"600\"");
        s.Text(cP2, top + 19, k.P2.IsConnector ? "P2" : "P2 (open)", 13, "middle", "font-weight=\"600\"");
        s.Text(cKleur, top + 19, "Colour", 13, "start", "font-weight=\"600\"");
        double RijY(int i) => top + 30 + (i + 0.5) * rowH;
        // twee aders met hetzelfde paarnummer zijn getwist: een kruis tussen hun lijnen
        var paren = k.Aders.Where(a => a.Paar is not null).GroupBy(a => a.Paar!.Value).Where(g => g.Count() == 2)
                     .Select(g => (A: k.Aders.IndexOf(g.First()), B: k.Aders.IndexOf(g.Last()))).ToList();
        var inPaar = new HashSet<int>(paren.SelectMany(p => new[] { p.A, p.B }));
        double kruisA = x + 165, kruisB = x + 195;
        for (var i = 0; i < n; i++)
        {
            var a = k.Aders[i];
            var ry = RijY(i);
            s.Text(cP1, ry + 4, k.P1.IsConnector ? (a.PinP1?.ToString() ?? "NC") : a.Nummer.ToString(), 12, "middle");
            s.Text(cP2, ry + 4, k.P2.IsConnector ? (a.PinP2?.ToString() ?? "NC") : a.Nummer.ToString(), 12, "middle");
            if (inPaar.Contains(i)) { s.Line(lijnA, ry, kruisA, ry, "#111", 1.2); s.Line(kruisB, ry, lijnB, ry, "#111", 1.2); }
            else s.Line(lijnA, ry, lijnB, ry, "#111", 1.2);
            s.Rect(cKleur, ry - 6, 12, 12, KabelKleuren.Hex(a.Kleur), "#555", .6);
            s.Text(cKleur + 18, ry + 4, KabelKleuren.Engels(a.Kleur) + (a.Paar is int p ? $" (pair {p})" : ""), 12);
        }
        foreach (var (ia, ib) in paren)
        {
            double ya = RijY(ia), yb = RijY(ib);
            s.Line(kruisA, ya, kruisB, yb, "#111", 1.2);
            s.Line(kruisA, yb, kruisB, ya, "#111", 1.2);
        }
        if (n == 0) s.Text(x + w / 2, RijY(0) + 4, "no wires", 12, "middle");
    }

    /// <returns>De onderkant van de tabel.</returns>
    private static double Stuklijst(Svg s, KabelSpec k, double x, double y, double w)
    {
        s.Text(x + w / 2, y + 14, "Bill of Materials", 15, "middle", "font-weight=\"600\"");
        var top = y + 24;
        double cSn = x + 34, cItem = x + 128, cQty = x + w - 48;
        var rijen = Stuklijstregels(k).Select(r => (r.Item, Lijnen: Wrap(r.Omschrijving, 44).ToList(), r.Aantal)).ToList();
        var h = 20 + rijen.Sum(r => 13 * r.Lijnen.Count + 7);
        s.Rect(x, top, w, h, "none", "#111");
        s.Line(x, top + 20, x + w, top + 20);
        foreach (var cx in new[] { cSn, cItem, cQty }) s.Line(cx, top, cx, top + h, "#111", .8);
        s.Text((x + cSn) / 2, top + 14, "S/N", 11, "middle", "font-weight=\"600\"");
        s.Text((cSn + cItem) / 2, top + 14, "Item", 11, "middle", "font-weight=\"600\"");
        s.Text((cItem + cQty) / 2, top + 14, "DESCRIPTION", 11, "middle", "font-weight=\"600\"");
        s.Text((cQty + x + w) / 2, top + 14, "Q'TY", 11, "middle", "font-weight=\"600\"");
        var yy = top + 20;
        var letter = 'A';
        foreach (var (item, lijnen, aantal) in rijen)
        {
            s.Text((x + cSn) / 2, yy + 14, letter.ToString(), 11, "middle");
            s.Text(cSn + 6, yy + 14, item, 11);
            for (var i = 0; i < lijnen.Count; i++) s.Text(cItem + 6, yy + 14 + 13 * i, lijnen[i], 11);
            s.Text((cQty + x + w) / 2, yy + 14, aantal, 11, "middle");
            yy += 13 * lijnen.Count + 7;
            s.Line(x, yy, x + w, yy, "#111", .6);
            letter++;
        }
        return top + h;
    }

    private static List<(string Item, string Omschrijving, string Aantal)> Stuklijstregels(KabelSpec k)
    {
        var r = new List<(string, string, string)>();
        var paren = k.Paren;
        if (k.IsLosseAders)
        {
            var los = $"{k.DraadType} {k.Awg}AWG {k.Draadopbouw} single wires, no jacket" + (paren > 0 ? $" ({paren} twisted pairs)" : "") +
                      ", colours according to wiring table";
            r.Add(("Wire", los, $"{k.Aders.Count} PCS"));
        }
        else
        {
            var draad = $"{k.DraadType} {k.Awg}AWG {k.Draadopbouw} x {k.Aders.Count}C" + (paren > 0 ? $" ({paren} twisted pairs)" : "") +
                        $", {AfschermingTekst(k.Afscherming)}, OD {F(k.Buitendiameter)} mm, {KabelKleuren.Engels(k.Mantelkleur)} jacket";
            r.Add(("Wire", draad, "1 PCS"));
        }
        var einden = new[] { (Naam: "P1", Eind: k.P1), (Naam: "P2", Eind: k.P2) }.Where(x => x.Eind.IsConnector).ToList();
        // housings en terminals: een regel voor beide kanten als ze dezelfde hebben
        foreach (var groep in einden.GroupBy(x => (x.Eind.Housing.Trim(), x.Eind.Familie.Trim(), x.Eind.Steek, x.Eind.Pinnen)))
        {
            var eind = groep.First().Eind;
            var namen = string.Join("+", groep.Select(g => g.Naam));
            var familie = eind.Familie.Trim().Length > 0 ? eind.Familie.Trim() + " " : "";
            r.Add(($"Housing {namen}", $"{Nummer(eind.Housing)} {familie}{F(eind.Steek)} mm pitch, {eind.Pinnen} circuits (original housing)", $"{groep.Count()} PCS"));
        }
        foreach (var groep in einden.GroupBy(x => x.Eind.Terminal.Trim()))
        {
            var stuks = groep.Sum(g => k.Aders.Count(a => (g.Naam == "P1" ? a.PinP1 : a.PinP2) is not null));
            var namen = string.Join("+", groep.Select(g => g.Naam));
            r.Add(($"Terminal {namen}", $"{Nummer(groep.Key)} crimp terminal (original)", $"{stuks} PCS"));
        }
        foreach (var g in new[] { k.P1, k.P2 }.Where(k.HeeftKous).GroupBy(e => (e.KrimpkousKleur.Trim(), e.KrimpkousLengte)))
            r.Add(("Sleeve", $"Heat shrink tube {KabelKleuren.Engels(g.Key.Item1)}, L={g.Key.KrimpkousLengte} mm, for OD {F(k.Buitendiameter)} mm cable", $"{g.Count()} PCS"));
        return r;
    }

    private static void Eisen(Svg s, KabelSpec k, double x, double y, double w)
    {
        s.Text(x, y + 12, "Requirements", 13, "start", "font-weight=\"600\"");
        var yy = y + 30;
        var nr = 1;
        foreach (var eis in k.Eisen.Where(e => e.Trim().Length > 0))
        {
            var lijnen = Wrap(eis.Trim(), 66).ToList();
            for (var i = 0; i < lijnen.Count; i++) { s.Text(x, yy, (i == 0 ? nr + ". " : "    ") + lijnen[i], 11); yy += 13; }
            yy += 2;
            nr++;
            if (yy > 748) return;
        }
        foreach (var (naam, eind) in new[] { ("P1", k.P1), ("P2", k.P2) })
            if (!eind.IsConnector && yy < 748) { s.Text(x, yy, $"{nr++}. {naam}: {OpenEindTekst(k, eind)}.", 11); yy += 15; }
        if (LijmTekst(k) is { } lijm)
        {
            var lijnen = Wrap(lijm, 66).ToList();
            for (var i = 0; i < lijnen.Count && yy < 748; i++) { s.Text(x, yy, (i == 0 ? nr + ". " : "    ") + lijnen[i], 11); yy += 13; }
        }
    }

    private static readonly string[] Cirkels = { "①", "②", "③", "④", "⑤", "⑥" };

    private static void NummerUitleg(Svg s, KabelSpec k, double x, double y, double w)
    {
        s.Text(x, y + 12, "Part number (LDZY scheme)", 13, "start", "font-weight=\"600\"");
        var delen = KabelNummer.Delen(k);
        var cx = x + 4;
        var basis = y + 52;
        for (var i = 0; i < delen.Length; i++)
        {
            var breedte = delen[i].Deel.Length * 15 + 6;
            s.Text(cx + breedte / 2, basis, delen[i].Deel, 24, "middle", "font-weight=\"600\"");
            s.Line(cx + 2, basis + 5, cx + breedte - 2, basis + 5, "#111", 1);
            s.Text(cx + breedte / 2, basis + 22, Cirkels[i], 13, "middle");
            cx += breedte + 8;
        }
        var yy = basis + 46;
        for (var i = 0; i < delen.Length; i++)
            foreach (var l in Wrap($"{Cirkels[i]} {delen[i].Deel.TrimStart('-')} = {delen[i].Uitleg}", 74)) { s.Text(x, yy, l, 11); yy += 14; }
    }

    private static void Bestelinfo(Svg s, KabelSpec k, double x, double y, double w)
    {
        s.Text(x, y + 12, "Order information", 13, "start", "font-weight=\"600\"");
        var yy = y + 30;
        var regels = new List<string>
        {
            $"Quantity: {k.Aantal} pcs",
            $"Customer part no.: {(k.EigenNummer.Trim().Length > 0 ? k.EigenNummer.Trim() : "-")}",
            $"Length: {k.Lengte} ±{k.LengteTolerantie} mm",
        };
        regels.AddRange(Wrap("Remarks: " + (k.Opmerkingen.Trim().Length > 0 ? k.Opmerkingen.Trim() : "-"), 74));
        foreach (var l in regels) { s.Text(x, yy, l, 11); yy += 14; if (yy > 748) break; }
    }

    private static void Titelblok(Svg s, KabelSpec k, string door, DateTime datum)
    {
        const double top = 760, bottom = 890, x0 = 10, x1 = 1390;
        s.Line(x0, top, x1, top, "#111", 1.5);
        s.Line(330, top, 330, bottom, "#111", 1);
        s.Line(860, top, 860, bottom, "#111", 1);
        // links: toleranties
        s.Text(170, top + 20, "GENERAL TOLERANCE (unless noted)", 11, "middle", "font-weight=\"600\"");
        s.Text(30, top + 44, $"{(k.IsLosseAders ? "Wire" : "Cable")} length: ±{k.LengteTolerantie} mm", 12);
        if (k.HeeftKous(k.P1) || k.HeeftKous(k.P2)) s.Text(30, top + 62, "Sleeve position and length: ±5 mm", 12);
        s.Text(30, top + 80, "Drawing not to scale.", 12);
        s.Text(30, top + 110, "RoHS / lead-free", 12, "start", "font-weight=\"600\"");
        // midden: klant en leverancier
        s.Text(345, top + 24, "CUSTOMER", 10, "start", "fill=\"#555\"");
        s.Text(345, top + 46, "MDE Automation", 18, "start", "font-weight=\"600\"");
        s.Text(345, top + 72, "SUPPLIER", 10, "start", "fill=\"#555\"");
        s.Text(345, top + 90, "DongGuan LDZY Electronics Co., Ltd.", 13);
        s.Text(345, top + 118, $"Drawn: {door}   Date: {datum:yyyy-MM-dd}   (MDE cable configurator)", 11);
        // rechts: nummer en titel
        s.Text(875, top + 20, "PART NO", 10, "start", "fill=\"#555\"");
        s.Text(875, top + 48, k.LeveranciersNummer, 26, "start", "font-weight=\"600\"");
        s.Line(860, top + 58, x1, top + 58, "#111", .8);
        s.Text(875, top + 72, "TITLE", 10, "start", "fill=\"#555\"");
        var titel = Wrap(Titel(k), 78).Take(3).ToList();
        for (var i = 0; i < titel.Count; i++) s.Text(875, top + 85 + 12 * i, titel[i], 11);
        s.Line(860, top + 112, x1, top + 112, "#111", .8);
        foreach (var cx in new[] { 992.0, 1124, 1256 }) s.Line(cx, top + 112, cx, bottom, "#111", .8);
        s.Text(926, top + 125, "UNIT  mm", 11, "middle");
        s.Text(1058, top + 125, "SCALE  NTS", 11, "middle");
        s.Text(1190, top + 125, "REV  A", 11, "middle");
        s.Text(1323, top + 125, "SHEET  1/1", 11, "middle");
    }

    // ---------------------------------------------------------------- teksten

    public static string Titel(KabelSpec k)
        => $"{k.DraadType} {k.Awg}AWG " +
           (k.IsLosseAders ? $"{k.Aders.Count} single wires" + (k.Paren > 0 ? $" ({k.Paren}P)" : "")
                           : $"{k.Aders.Count}C" + (k.Paren > 0 ? $" ({k.Paren}P)" : "") + $" {(k.Afscherming == KabelAfscherming.Geen ? "unshielded" : "shielded")}") +
           $", P1: {EindTekst(k.P1)}, P2: {EindTekst(k.P2)}, L={k.Lengte} mm";

    /// <summary>De eis "aders vastlijmen", voor de kanten met een connector; null als de optie uit staat of er geen connector is.</summary>
    private static string? LijmTekst(KabelSpec k)
    {
        if (!k.AdersLijmen) return null;
        var kanten = new[] { ("P1", k.P1), ("P2", k.P2) }.Where(x => x.Item2.IsConnector).Select(x => x.Item1).ToList();
        return kanten.Count == 0 ? null
            : $"Fix the wires to the connector housing with glue at the wire entry of {string.Join(" and ", kanten)} (strain relief). " +
              "The glue must not reach the contact area.";
    }

    /// <summary>Een open eind in woorden: bij een kabel gaat de mantel eraf, bij losse aders de isolatie van elke ader.</summary>
    private static string OpenEindTekst(KabelSpec k, KabelEind e)
        => k.IsLosseAders ? $"open end, strip wires {e.StripLengte} mm, {Afwerking(e)}"
                          : $"open end, strip jacket {e.StripLengte} mm, wires {Afwerking(e)}";

    private static string EindTekst(KabelEind e)
    {
        if (!e.IsConnector) return "open end";
        var familie = e.Familie.Trim().Length > 0 ? e.Familie.Trim() + " " : "";
        var housing = e.Housing.Trim().Length > 0 ? $" ({e.Housing.Trim()})" : "";
        return $"{familie}{e.Pinnen}P{housing}";
    }

    /// <summary>De spec als platte tekst (Engels), om in een e-mail aan de leverancier te plakken.</summary>
    public static string SpecTekst(KabelSpec k)
    {
        var b = new StringBuilder();
        b.AppendLine("Cable assembly request - MDE Automation");
        b.AppendLine($"Part no. (LDZY scheme): {k.LeveranciersNummer}");
        b.AppendLine($"Title: {Titel(k)}");
        b.AppendLine($"Quantity: {k.Aantal} pcs");
        b.AppendLine($"Length: {k.Lengte} +/-{k.LengteTolerantie} mm");
        b.AppendLine($"Wire: {Stuklijstregels(k)[0].Omschrijving}");
        foreach (var (naam, e) in new[] { ("P1", k.P1), ("P2", k.P2) })
        {
            b.Append(naam).Append(": ");
            if (e.IsConnector) b.Append($"{e.Familie.Trim()} {F(e.Steek)} mm pitch, {e.Pinnen} circuits, housing {Nummer(e.Housing)}, terminal {Nummer(e.Terminal)}".Trim());
            else b.Append(OpenEindTekst(k, e));
            if (k.HeeftKous(e)) b.Append($"; {KabelKleuren.Engels(e.KrimpkousKleur)} heat shrink tube L={e.KrimpkousLengte} mm at {e.KrimpkousAfstand} mm from the end");
            b.AppendLine();
        }
        b.AppendLine("Wiring (wire: P1 pin - P2 pin - colour - pair):");
        foreach (var a in k.Aders)
            b.AppendLine($"  {a.Nummer}: {PinTekst(k.P1, a.PinP1)} - {PinTekst(k.P2, a.PinP2)} - {KabelKleuren.Engels(a.Kleur)}" + (a.Paar is int p ? $" - pair {p}" : ""));
        b.AppendLine("Requirements:");
        var nr = 1;
        foreach (var eis in k.Eisen.Where(e => e.Trim().Length > 0)) b.AppendLine($"  {nr++}. {eis.Trim()}");
        if (LijmTekst(k) is { } lijm) b.AppendLine($"  {nr++}. {lijm}");
        if (k.Opmerkingen.Trim().Length > 0) b.AppendLine("Remarks: " + k.Opmerkingen.Trim());
        return b.ToString();
    }

    private static string PinTekst(KabelEind e, int? pin) => e.IsConnector ? (pin?.ToString() ?? "NC") : "open";

    private static string Nummer(string n) => n.Trim().Length > 0 ? n.Trim() : "(part no. t.b.d.)";

    private static string AfschermingTekst(KabelAfscherming a) => a switch
    {
        KabelAfscherming.Folie => "AL foil shield",
        KabelAfscherming.Vlecht => "braid shield",
        KabelAfscherming.FolieEnVlecht => "AL foil + braid shield",
        _ => "unshielded",
    };

    private static string Afwerking(KabelEind e) => e.Afwerking.Trim().ToLowerInvariant() switch
    {
        "vertind" => "tinned",
        "adereindhuls" => "with ferrules",
        "blank" => "bare (stripped only)",
        var anders => anders,
    };

    private static string F(double d) => d.ToString("0.##", Inv);

    private static IEnumerable<string> Wrap(string tekst, int max)
    {
        var regel = new StringBuilder();
        foreach (var woord in tekst.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (regel.Length > 0 && regel.Length + 1 + woord.Length > max) { yield return regel.ToString(); regel.Clear(); }
            if (regel.Length > 0) regel.Append(' ');
            regel.Append(woord);
        }
        if (regel.Length > 0) yield return regel.ToString();
    }

    // ---------------------------------------------------------------- svg-hulpje

    private sealed class Svg
    {
        private readonly StringBuilder _b = new();

        public Svg(int w, int h)
            => _b.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {w} {h}\" width=\"{w}\" height=\"{h}\" " +
                         "font-family=\"Segoe UI, Arial, Helvetica, sans-serif\" font-size=\"12\" fill=\"#111\">");

        public void Raw(string s) => _b.Append(s);

        public void Rect(double x, double y, double w, double h, string vul, string? lijn = null, double dikte = 1, double rond = 0)
            => _b.Append($"<rect x=\"{F(x)}\" y=\"{F(y)}\" width=\"{F(w)}\" height=\"{F(h)}\" fill=\"{vul}\"")
                 .Append(lijn is null ? "" : $" stroke=\"{lijn}\" stroke-width=\"{F(dikte)}\"")
                 .Append(rond > 0 ? $" rx=\"{F(rond)}\"" : "")
                 .Append("/>");

        public void Line(double x1, double y1, double x2, double y2, string kleur = "#111", double dikte = 1, string? extra = null)
            => _b.Append($"<line x1=\"{F(x1)}\" y1=\"{F(y1)}\" x2=\"{F(x2)}\" y2=\"{F(y2)}\" stroke=\"{kleur}\" stroke-width=\"{F(dikte)}\" stroke-linecap=\"round\"")
                 .Append(extra is null ? "" : " " + extra)
                 .Append("/>");

        public void Text(double x, double y, string t, double grootte = 12, string anker = "start", string? extra = null)
            => _b.Append($"<text x=\"{F(x)}\" y=\"{F(y)}\" font-size=\"{F(grootte)}\" text-anchor=\"{anker}\"")
                 .Append(extra is null ? "" : " " + extra)
                 .Append('>').Append(E(t)).Append("</text>");

        public override string ToString() => _b + "</svg>";

        private static string E(string t) => t.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
