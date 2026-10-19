// Zoekvak + lijst eronder werkt als een combobox. Eén globale listener; per scherm alleen een markering.
//
// Markeer het zoekvak met data-zoeklijst. Zijn lijst is de eerstvolgende .wn-namen (of [data-zoeklijst-lijst]) in de
// buurt (hoogstens 6 niveaus omhoog), met knoppen button.wn-naam.
//
//   Enter in het zoekvak  : staat er (na het bijwerken van de lijst) precies één treffer, dan wordt die gekozen.
//                           Niet bij data-zoeklijst="pijltjes" (velden met een eigen Enter, zoals scannen).
//   Pijltje omlaag        : naar de eerste treffer. In de lijst: pijltjes = vorige/volgende, Home/End,
//                           Enter = kiezen (gewone knop), Esc of pijltje omhoog op de eerste = terug naar het zoekvak,
//                           een letter of spatie typen = verder typen in het zoekvak.
//   Een lijst met tabindex (Bestellingen) heeft eigen pijltjes: dan gaat alleen de focus naar de lijst zelf.
(function () {
    const LIJST = '.wn-namen, [data-zoeklijst-lijst]';
    const terug = new WeakMap();   // lijst -> zoekvak

    function lijstBij(invoer) {
        if (!invoer.hasAttribute || !invoer.hasAttribute('data-zoeklijst') || invoer.readOnly || invoer.disabled) return null;
        let el = invoer.parentElement;
        for (let i = 0; el && i < 6; i++, el = el.parentElement) {
            for (const l of el.querySelectorAll(LIJST))
                if (invoer.compareDocumentPosition(l) & Node.DOCUMENT_POSITION_FOLLOWING) return l;
        }
        return null;
    }

    function regels(lijst) {
        return Array.from(lijst.querySelectorAll('button.wn-naam'))
            .filter(b => !b.disabled && b.offsetParent !== null);
    }

    function handtekening(lijst) {
        return lijst ? regels(lijst).map(b => b.textContent).join('\u001f') : '';
    }

    // Enter: wachten tot de lijst rustig is (een zoekvak met onchange zoekt pas na de Enter), dan bij één treffer kiezen.
    let wachter = null;
    function kiesEnigeNaRust(invoer) {
        if (wachter) clearInterval(wachter);
        const waarde = invoer.value, begin = Date.now();
        let vorige = handtekening(lijstBij(invoer)), rustigSinds = begin;
        wachter = setInterval(function () {
            if (document.activeElement !== invoer || invoer.value !== waarde || Date.now() - begin > 4000) {
                clearInterval(wachter); wachter = null; return;
            }
            const lijst = lijstBij(invoer);
            const nu = handtekening(lijst);
            if (nu !== vorige) { vorige = nu; rustigSinds = Date.now(); return; }
            if (Date.now() - rustigSinds < 500) return;
            clearInterval(wachter); wachter = null;
            const r = lijst ? regels(lijst) : [];
            if (r.length === 1) r[0].click();
        }, 80);
    }

    document.addEventListener('keydown', function (e) {
        if (e.ctrlKey || e.altKey || e.metaKey || e.isComposing) return;
        const doel = e.target;
        if (!doel || !doel.closest) return;

        // ---- in het zoekvak ----
        if (doel.hasAttribute('data-zoeklijst')) {
            if (e.key !== 'ArrowDown' && e.key !== 'Enter') return;
            const lijst = lijstBij(doel);
            if (!lijst) return;
            if (e.key === 'Enter') {
                if (doel.getAttribute('data-zoeklijst') !== 'pijltjes') kiesEnigeNaRust(doel);
                return;
            }
            const r = regels(lijst);
            if (r.length === 0) return;
            e.preventDefault();
            terug.set(lijst, doel);
            if (lijst.hasAttribute('tabindex')) lijst.focus();
            else r[0].focus();
            return;
        }

        // ---- op een regel in de lijst ----
        const knop = doel.closest('button.wn-naam');
        if (!knop) return;
        const lijst = knop.closest(LIJST);
        if (!lijst || lijst.hasAttribute('tabindex')) return;
        const invoer = terug.get(lijst);
        const r = regels(lijst);
        const i = r.indexOf(knop);
        let naar = null;
        if (e.key === 'ArrowDown') naar = r[Math.min(i + 1, r.length - 1)];
        else if (e.key === 'ArrowUp') naar = i <= 0 ? invoer : r[i - 1];
        else if (e.key === 'Home') naar = r[0];
        else if (e.key === 'End') naar = r[r.length - 1];
        else if (e.key === 'Escape') naar = invoer;
        else if (e.key.length === 1 && invoer && document.contains(invoer)) { invoer.focus(); return; }   // letter of spatie: verder typen
        else return;
        e.preventDefault();
        if (naar && document.contains(naar)) {
            naar.focus();
            if (naar.scrollIntoView) naar.scrollIntoView({ block: 'nearest' });
        }
    });
})();
