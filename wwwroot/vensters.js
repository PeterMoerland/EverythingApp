// Escape sluit het bovenste venster (pop-up), ook als de cursor er niet in staat (wens gebruiker 19-11-2026: "direct na
// openen met Escape kunnen sluiten; dit is nog niet overal").
//
// Bijna alle vensters in de app staan in een .cmdk-overlay en hebben in hun kop een knop "✕" (of "✕ Sluiten"). Een deel
// vangt Escape zelf in Blazor, maar alleen als de cursor in het venster staat; een deel doet er niets mee. In plaats van
// honderd vensters langs te gaan regelt dit het op één plek, als vangnet:
//
//   1. Escape: even wachten (180 ms) of iets anders de toets afhandelt. Verandert er in die tijd iets in het venster
//      (een keuzelijst die dichtklapt, een regel die uit bewerken gaat), is het venster weg of is de cursor verplaatst,
//      dan was de toets daarvoor en gebeurt er hier niets.
//   2. Anders: de sluitknop in de kop van het bovenste venster aanklikken. Dat is dus precies wat een klik op ✕ doet,
//      met dezelfde controles (bv. de vraag bij niet-bewaarde wijzigingen). Een uitgeschakelde knop blijft uit.
//   3. Geen sluitknop gevonden: een klik naast het venster nadoen; vensters die daarop sluiten, sluiten dan.
//
// Een venster zonder sluitknop dat niet sluit bij een klik ernaast (een verplichte keuze) blijft dus staan.
// Het bevestigvenster (mdeBevestig) en het Activity board (.ab-drawer, .ab-modal) regelen Escape zelf.
(function () {
    'use strict';
    var bezig = false;

    function zichtbaar(el) { return el.getClientRects().length > 0; }

    // Het bovenste venster: de hoogste z-index, bij gelijke stand het laatste in het document
    function bovenste() {
        var best = null, bz = -Infinity;
        Array.prototype.forEach.call(document.querySelectorAll('.cmdk-overlay'), function (l) {
            if (!zichtbaar(l)) return;
            var z = parseInt(getComputedStyle(l).zIndex, 10) || 0;
            if (z >= bz) { bz = z; best = l; }
        });
        return best;
    }

    // De sluitknop: een knop met alleen "✕" of "×" (eventueel "✕ Sluiten") boven in het venster. Een ✕ met een andere
    // titel (Wissen, Verwijderen, filter weg) of lager in het venster (een regel weghalen) is geen sluitknop.
    function sluitknop(laag) {
        var doos = laag.firstElementChild || laag;
        var top = doos.getBoundingClientRect().top;
        var knoppen = laag.querySelectorAll('button');
        for (var i = 0; i < knoppen.length; i++) {
            var b = knoppen[i], tekst = (b.textContent || '').trim();
            if (!/^[✕×✖]\s*(Sluiten)?$/i.test(tekst)) continue;
            var titel = (b.getAttribute('title') || b.getAttribute('aria-label') || '').trim();
            if (!/sluiten$/i.test(tekst) && titel && !/^sluit/i.test(titel)) continue;
            if (!zichtbaar(b) || b.getBoundingClientRect().top - top > 90) continue;
            return b;
        }
        return null;
    }

    document.addEventListener('keydown', function (e) {
        if (e.key !== 'Escape' || e.defaultPrevented || e.isComposing || bezig) return;
        var laag = bovenste();
        if (!laag) return;
        var t = e.target;
        // een keuzelijst die openstaat sluit zelf op Escape; de cursor eruit halen, de volgende Escape sluit het venster
        if (t && t.tagName === 'SELECT') { t.blur(); return; }

        bezig = true;
        var veranderd = false, actief = document.activeElement;
        var kijker = new MutationObserver(function () { veranderd = true; });
        kijker.observe(laag, { childList: true, subtree: true, attributes: true, characterData: true });
        setTimeout(function () {
            kijker.disconnect();
            bezig = false;
            if (veranderd || !laag.isConnected || document.activeElement !== actief || bovenste() !== laag) return;
            var knop = sluitknop(laag);
            if (knop) { if (!knop.disabled) knop.click(); return; }
            laag.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true }));
            laag.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
        }, 180);
    });
})();
