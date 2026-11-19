// Invoervelden die alleen cijfers aannemen (punt van Gerard, 19-11-2026: in de ureninvoer kon je bij de tijden ook tekst
// typen). Werkt op elke pagina, zonder dat het scherm er iets voor hoeft te doen:
//   input.tijd                 cijfers en één dubbele punt, hooguit 5 tekens; punt, komma, puntkomma of streepje wordt
//                              de dubbele punt. De server maakt er daarna een tijd van (8 -> 08:00, 830 -> 08:30).
//   input[data-alleen=getal]   cijfers en één komma (een punt wordt de komma)
// Bij het typen en bij plakken; de cursor blijft op zijn plek.
// Ook hier: Ctrl+Enter = versturen of bevestigen, in een vak met data-ctrl-enter (zie onderaan).
(function () {
    'use strict';

    function eenKeer(tekst, teken) {
        const i = tekst.indexOf(teken);
        return i < 0 ? tekst : tekst.slice(0, i + 1) + tekst.slice(i + 1).split(teken).join('');
    }

    function schoon(el) {
        const v = el.value;
        let n;
        if (el.classList.contains('tijd')) n = eenKeer(v.replace(/[.,;\-]/g, ':').replace(/[^0-9:]/g, ''), ':').slice(0, 5);
        else if (el.dataset.alleen === 'getal') n = eenKeer(v.replace(/\./g, ',').replace(/[^0-9,]/g, ''), ',');
        else return;
        if (n === v) return;
        const cursor = el.selectionStart, weg = v.length - n.length;
        el.value = n;
        try { const p = Math.max(0, (cursor || 0) - weg); el.setSelectionRange(p, p); } catch (x) { }
    }

    // capture: vóór de afhandeling van de pagina zelf, zodat die de opgeschoonde waarde ziet
    document.addEventListener('input', function (e) {
        const t = e.target;
        if (t && t.tagName === 'INPUT') schoon(t);
    }, true);

    // Ctrl+Enter = versturen of bevestigen (wens gebruiker 19-11-2026). Een scherm doet mee door het vak met de velden en
    // de knop het kenmerk data-ctrl-enter te geven: Ctrl+Enter in een veld daarbinnen klikt de eerste knop.primair die aan
    // staat. Eerst 'change' op het veld, zodat wat net getypt is meekomt (velden met @bind bewaren pas bij het verlaten).
    document.addEventListener('keydown', function (e) {
        if (e.key !== 'Enter' || !(e.ctrlKey || e.metaKey) || e.repeat) return;
        const t = e.target;
        if (!t || !t.closest || !/^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName)) return;
        const vak = t.closest('[data-ctrl-enter]');
        const knop = vak && vak.querySelector('button.primair:not(:disabled)');
        if (!knop) return;
        e.preventDefault();
        t.dispatchEvent(new Event('change', { bubbles: true }));
        knop.click();
    });
})();
