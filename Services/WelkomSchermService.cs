using System.Text.Json;
using static System.Net.WebUtility;

namespace EverythingApp.Services;

/// <summary>
/// Het welkomstscherm voor de tv in de hal (Relaties ▸ Welkomstscherm). De tv opent /hal/&lt;sleutel&gt; zonder inlog;
/// de sleutel staat in appsettings (Welkomstscherm:Sleutel) en is alleen te zien voor wie het scherm mag instellen.
/// De pagina is losse HTML (geen Blazor), vraagt elke minuut /hal/&lt;sleutel&gt;/data op en wisselt bij meerdere
/// bezoekers elke 12 seconden. Relatielogo: het nieuwste plaatje met "logo" in de naam bij de documenten van de relatie
/// in de filestore (StoreType 311); gastheer: de bestaande pasfoto (/werknemerfoto/{id}).
/// </summary>
public class WelkomSchermService
{
    private readonly IConfiguration _config;
    private readonly IWelkomRepository _repo;
    private readonly IWerknemerRepository _wn;
    private readonly FilestoreService _store;

    public WelkomSchermService(IConfiguration config, IWelkomRepository repo, IWerknemerRepository wn, FilestoreService store)
    {
        _config = config;
        _repo = repo;
        _wn = wn;
        _store = store;
    }

    public string Sleutel => (_config["Welkomstscherm:Sleutel"] ?? "").Trim();
    public bool Aan => Sleutel.Length >= 12;
    public bool SleutelKlopt(string? sleutel)
        => Aan && sleutel is not null && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
               System.Text.Encoding.UTF8.GetBytes(sleutel), System.Text.Encoding.UTF8.GetBytes(Sleutel));

    private static readonly string[] Plaatjes = { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".svg" };

    /// <summary>Het logo van een relatie in de filestore: het nieuwste plaatje met "logo" in de naam.</summary>
    public async Task<int?> RelatieLogoAsync(int relatieId)
    {
        if (!_wn.Beschikbaar || !_store.Beschikbaar) return null;
        try
        {
            var docs = await _wn.GetBestandenAsync(FilestoreService.StoreTypeRelatie, relatieId);
            return docs.Where(d => d.Bestandsnaam.Contains("logo", StringComparison.OrdinalIgnoreCase)
                                   && Plaatjes.Contains(Path.GetExtension(d.Bestandsnaam).ToLowerInvariant()))
                       .OrderByDescending(d => d.Toegevoegd ?? DateTime.MinValue).ThenByDescending(d => d.FileStoreId)
                       .Select(d => (int?)d.FileStoreId).FirstOrDefault();
        }
        catch { return null; }
    }

    public async Task<(string Pad, string Mime)?> RelatieLogoBestandAsync(int relatieId)
    {
        if (await RelatieLogoAsync(relatieId) is not int id) return null;
        var gevonden = await _store.ZoekAsync(id);
        return gevonden is null ? null : (gevonden.Value.Pad, FilestoreService.MimeVoor(gevonden.Value.Naam));
    }

    /// <summary>Ons eigen logo: Welkomstscherm:Logo, anders het factuurlogo van MDE Automation (Templates/Factuur/logos/5254).</summary>
    public string? EigenLogoPad()
    {
        var ingesteld = _config["Welkomstscherm:Logo"];
        if (!string.IsNullOrWhiteSpace(ingesteld) && File.Exists(ingesteld)) return ingesteld;
        foreach (var ext in new[] { "png", "jpg" })
        {
            var pad = Path.Combine(AppContext.BaseDirectory, "Templates", "Factuur", "logos", $"5254.{ext}");
            if (File.Exists(pad)) return pad;
        }
        return null;
    }

    /// <summary>Wat de tv nu laat zien; met <paramref name="voorbeeld"/> dat ene welkom, ongeacht datum en tijd.</summary>
    public async Task<string> DataJsonAsync(string basis, int? voorbeeld)
    {
        var nu = DateTime.Now;
        List<WelkomItem> items;
        if (voorbeeld is int id) items = (await _repo.GetAsync(id)) is { } w ? new() { w } : new();
        else items = (await _repo.VandaagAsync(DateOnly.FromDateTime(nu))).Where(w => w.Actief(nu)).ToList();

        var uit = new List<object>();
        foreach (var w in items)
            uit.Add(new
            {
                relatie = w.Relatie,
                tekst = w.Tekst,
                gastheer = w.Gastheer,
                gastheerFoto = w.GastheerId is int g ? $"{basis}/werknemerfoto/{g}" : null,
                logo = w.RelatieId is int r && await RelatieLogoAsync(r) is not null ? $"{basis}/hal/{Sleutel}/logo/{r}" : null,
            });
        return JsonSerializer.Serialize(new { items = uit, standaard = StandaardTekst });
    }

    private string StandaardTekst => _config["Welkomstscherm:StandaardTekst"] is { Length: > 0 } t ? t : "Welkom bij MDE Automation";

    public string Html(string basis, int? voorbeeld)
    {
        var data = $"{basis}/hal/{Sleutel}/data" + (voorbeeld is int v ? $"?voorbeeld={v}" : "");
        var logo = $"{basis}/hal/{Sleutel}/eigen-logo";
        return $$"""
<!doctype html>
<html lang="nl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Welkom — MDE Automation</title>
<style>
  :root { --rood: #E30613; --tekst: #f4f6fa; --zacht: #a9b4c6; }
  * { box-sizing: border-box; }
  html, body { margin: 0; height: 100%; background: #0b1424; color: var(--tekst); overflow: hidden;
               font-family: 'Segoe UI', 'Helvetica Neue', Arial, sans-serif; cursor: none; }
  body { background: radial-gradient(ellipse at 75% 20%, #1c2f52 0%, #0b1424 55%, #070d18 100%); }
  .balk { position: absolute; left: 0; top: 0; bottom: 0; width: 1.1vw; background: var(--rood); }
  header { position: absolute; top: 4vh; left: 5vw; right: 5vw; display: flex; justify-content: space-between; align-items: center; }
  .eigen { height: 9vh; max-width: 32vw; object-fit: contain; filter: drop-shadow(0 0 1px rgba(255,255,255,.35)); background: #fff; border-radius: .6vw; padding: .8vh 1vw; }
  .klok { text-align: right; }
  .klok .tijd { font-size: 6vh; font-weight: 300; letter-spacing: .05em; line-height: 1; }
  .klok .datum { font-size: 2.4vh; color: var(--zacht); margin-top: .8vh; }
  main { position: absolute; left: 5vw; right: 5vw; top: 22vh; bottom: 6vh; display: flex; flex-direction: column; justify-content: center; }
  .welkom { font-size: 4vh; text-transform: uppercase; letter-spacing: .35em; color: var(--rood); font-weight: 600; }
  .relatie { display: flex; align-items: center; gap: 3vw; margin-top: 2vh; }
  .relatie h1 { font-size: min(10vh, 5.6vw); line-height: 1.05; margin: 0; font-weight: 700; overflow-wrap: anywhere; }
  .relatielogo { max-height: 18vh; max-width: 28vw; object-fit: contain; background: #fff; border-radius: 1vw; padding: 1.5vh 1.5vw; }
  .tekst { font-size: min(4.2vh, 2.6vw); line-height: 1.35; color: #dfe5ef; margin-top: 4vh; max-width: 70vw; white-space: pre-line; }
  .gastheer { display: flex; align-items: center; gap: 1.6vw; margin-top: 6vh; }
  .gastheer .foto, .gastheer .initialen { width: 13vh; height: 13vh; border-radius: 50%; object-fit: cover; border: .5vh solid var(--rood); }
  .gastheer .initialen { display: flex; align-items: center; justify-content: center; background: #22385e; font-size: 5vh; font-weight: 600; }
  .gastheer small { display: block; font-size: 2.4vh; color: var(--zacht); text-transform: uppercase; letter-spacing: .2em; }
  .gastheer b { font-size: 4.4vh; font-weight: 600; }
  .standaard { align-items: flex-start; }
  .standaard h1 { font-size: min(9vh, 5.4vw); margin: 2vh 0 0; font-weight: 700; }
  .stippen { position: absolute; right: 5vw; bottom: 5vh; display: flex; gap: .8vw; }
  .stippen span { width: 1.4vh; height: 1.4vh; border-radius: 50%; background: #3a4b68; }
  .stippen span.aan { background: var(--rood); }
  .fade { animation: fade .8s ease; }
  @keyframes fade { from { opacity: 0; transform: translateY(1.5vh); } to { opacity: 1; transform: none; } }
</style></head>
<body>
<div class="balk"></div>
<header><img class="eigen" src="{{HtmlEncode(logo)}}" alt="MDE Automation" onerror="this.style.display='none'">
  <div class="klok"><div class="tijd" id="tijd"></div><div class="datum" id="datum"></div></div></header>
<main id="inhoud"></main>
<div class="stippen" id="stippen"></div>
<script>
const DATA = {{JsonSerializer.Serialize(data)}};
let items = [], standaard = 'Welkom bij MDE Automation', i = 0, vorige = '';
const e = s => (s || '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const initialen = n => (n || '').split(/\s+/).filter(Boolean).map(w => w[0]).filter(c => c === c.toUpperCase()).slice(0, 2).join('');
function klok() {
  const nu = new Date();
  document.getElementById('tijd').textContent = nu.toLocaleTimeString('nl-NL', { hour: '2-digit', minute: '2-digit' });
  const d = nu.toLocaleDateString('nl-NL', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
  document.getElementById('datum').textContent = d.charAt(0).toUpperCase() + d.slice(1);
}
function toon() {
  const m = document.getElementById('inhoud');
  let html;
  if (items.length === 0) {
    html = `<div class="standaard"><div class="welkom">Welkom</div><h1>${e(standaard)}</h1></div>`;
  } else {
    const w = items[i % items.length];
    html = `<div class="welkom">Welkom</div>` +
      (w.relatie ? `<div class="relatie"><h1>${e(w.relatie)}</h1>${w.logo ? `<img class="relatielogo" src="${e(w.logo)}" alt="" onerror="this.remove()">` : ''}</div>` : '') +
      (w.tekst ? `<div class="tekst">${e(w.tekst)}</div>` : '') +
      (w.gastheer ? `<div class="gastheer">${w.gastheerFoto ? `<img class="foto" src="${e(w.gastheerFoto)}" alt="" onerror="this.outerHTML='<div class=&quot;initialen&quot;>${e(initialen(w.gastheer))}</div>'">` : `<div class="initialen">${e(initialen(w.gastheer))}</div>`}<div><small>Uw gastheer</small><b>${e(w.gastheer)}</b></div></div>` : '');
  }
  if (html !== vorige) { m.innerHTML = html; m.classList.remove('fade'); void m.offsetWidth; m.classList.add('fade'); vorige = html; }
  document.getElementById('stippen').innerHTML = items.length > 1 ? items.map((_, n) => `<span class="${n === i % items.length ? 'aan' : ''}"></span>`).join('') : '';
}
async function haal() {
  try {
    const r = await fetch(DATA, { cache: 'no-store' });
    if (r.ok) { const d = await r.json(); items = d.items || []; standaard = d.standaard || standaard; if (i >= items.length) i = 0; toon(); }
  } catch (x) { /* netwerk even weg: laten staan wat er staat */ }
}
klok(); setInterval(klok, 1000);
haal(); setInterval(haal, 60000);
setInterval(() => { if (items.length > 1) { i = (i + 1) % items.length; toon(); } }, 12000);
setTimeout(() => location.reload(), 6 * 3600 * 1000);   // af en toe vers laden (nieuwe versie van de app)
</script>
</body></html>
""";
    }
}
