// Activity board: kaarten en problemen slepen (muis en touch), zoals prototype v0.5 (spec §4).
// Het scherm (ActivityBoard.razor) koppelt zich met mdeAb.koppel(dotnet); deze code verplaatst alleen
// een eigen plaatshouder en een zwevende kopie, en meldt bij loslaten wat er moet gebeuren:
//   KaartLosgelaten(id, lane, status|null, prevId|null, nextId|null)   status null = op een baankop
//   ProblemenGeordend([ids], verplaatstId)
//   KaartGeklikt(id)                                                   klik of tik zonder slepen
//   SleepBezig(true|false)                                             zolang er gesleept wordt geen herlaad
(function () {
    'use strict';
    let doel = null;      // DotNetObjectReference van het scherm
    let s = null;         // lopende sleep

    window.mdeAb = {
        koppel: function (dotnet) { doel = dotnet; },
        los: function () { stop(false); doel = null; },
        // Focus terug op een kaart of tabelregel na het sluiten van het detailpaneel
        focus: function (id) {
            const el = document.querySelector('.ab-kaart[data-id="' + id + '"], tr.ab-rij[data-id="' + id + '"]');
            if (el) el.focus({ preventScroll: true });
        },
        focusEl: function (sel) { const el = document.querySelector(sel); if (el) { el.focus(); if (el.select) el.select(); } }
    };

    function roep(naam) {
        if (!doel) return;
        const args = Array.prototype.slice.call(arguments, 1);
        doel.invokeMethodAsync.apply(doel, [naam].concat(args)).catch(function () { });
    }

    document.addEventListener('pointerdown', function (e) {
        if (!doel || s || e.button !== 0) return;
        if (e.target.closest('.ab-modal, .ab-snel, .ab-drawer')) return;
        const greep = e.target.closest('#ab-problijst .ab-greep');
        let kaart = !greep && e.target.closest('.ab-kaart[data-id]');
        if (kaart && e.target.closest('button, input, select, textarea, a')) kaart = null;
        if (!greep && !kaart) return;
        const el = greep ? greep.closest('.ab-prob') : kaart;
        s = {
            soort: greep ? 'prob' : 'kaart', el: el, id: el.dataset.id, pid: e.pointerId,
            x0: e.clientX, y0: e.clientY, x: e.clientX, y: e.clientY, gestart: false, touch: e.pointerType === 'touch'
        };
        if (greep) e.preventDefault();
        // touch: 300 ms vasthouden start het slepen; een tik opent de kaart
        if (s.touch) s.timer = setTimeout(function () { if (s && !s.gestart) begin(); }, 300);
    });

    document.addEventListener('pointermove', function (e) {
        if (!s || e.pointerId !== s.pid) return;
        if (e.pointerType === 'mouse' && !(e.buttons & 1)) { stop(false); return; }
        s.x = e.clientX; s.y = e.clientY;
        if (!s.gestart) {
            const d = Math.hypot(e.clientX - s.x0, e.clientY - s.y0);
            if (s.touch) { if (d > 10) { clearTimeout(s.timer); s = null; } return; }
            if (d < 5) return;
            begin();
        }
        e.preventDefault();
        beweeg();
    }, { passive: false });

    document.addEventListener('pointerup', function (e) {
        if (!s || e.pointerId !== s.pid) return;
        clearTimeout(s.timer);
        if (s.gestart) { stop(true); return; }
        const x = s; s = null;
        if (x.soort === 'kaart') roep('KaartGeklikt', parseInt(x.id));
        else { const g = x.el.querySelector('.ab-greep'); if (g) g.focus(); }
    });
    document.addEventListener('pointercancel', function () { stop(false); });
    // prioriteit van een probleem met pijltje omhoog/omlaag op de greep; de focus blijft op dat probleem
    document.addEventListener('keydown', function (e) {
        if (!doel || s || (e.key !== 'ArrowUp' && e.key !== 'ArrowDown')) return;
        const greep = e.target.closest && e.target.closest('#ab-problijst .ab-greep');
        if (!greep) return;
        e.preventDefault();
        const id = parseInt(greep.closest('.ab-prob').dataset.id);
        doel.invokeMethodAsync('ProbleemSchuif', id, e.key === 'ArrowUp' ? -1 : 1).then(function () {
            const g = document.querySelector('#ab-problijst .ab-prob[data-id="' + id + '"] .ab-greep');
            if (g) g.focus();
        }).catch(function () { });
    });
    document.addEventListener('keydown', function (e) { if (s && s.gestart && e.key === 'Escape') { e.preventDefault(); stop(false); } }, true);
    // tijdens slepen niet ook tekst selecteren of het contextmenu van touch openen
    document.addEventListener('contextmenu', function (e) { if (s) e.preventDefault(); });

    function begin() {
        s.gestart = true;
        const r = s.el.getBoundingClientRect();
        s.dx = s.x0 - r.left; s.dy = s.y0 - r.top;
        s.ghost = s.el.cloneNode(true);
        s.ghost.classList.add('ab-ghost');
        s.ghost.removeAttribute('data-id');
        s.ghost.style.width = r.width + 'px';
        document.body.appendChild(s.ghost);
        s.plek = document.createElement(s.soort === 'kaart' ? 'div' : 'li');
        s.plek.className = s.soort === 'kaart' ? 'ab-plek' : 'ab-plekprob';
        s.plek.style.height = r.height + 'px';
        s.el.parentNode.insertBefore(s.plek, s.el);
        s.bronCel = s.el.parentNode;
        s.oudPrev = buur(s.plek, -1); s.oudNext = buur(s.plek, 1);
        s.el.style.display = 'none';
        document.body.classList.add('ab-sleepmodus');
        try { document.body.setPointerCapture(s.pid); } catch (x) { }
        roep('SleepBezig', true);
        zetGhost();
        s.raf = requestAnimationFrame(autoScroll);
    }

    function zetGhost() { s.ghost.style.transform = 'translate(' + (s.x - s.dx) + 'px,' + (s.y - s.dy) + 'px) rotate(1.5deg)'; }

    // vorige/volgende kaart naast de plaatshouder (de verborgen kaart zelf telt niet)
    function buur(el, richting) {
        let n = richting < 0 ? el.previousElementSibling : el.nextElementSibling;
        while (n && (!n.classList.contains(s.soort === 'kaart' ? 'ab-kaart' : 'ab-prob') || n === s.el))
            n = richting < 0 ? n.previousElementSibling : n.nextElementSibling;
        return n ? n.dataset.id : null;
    }

    function wisDoel() { document.querySelectorAll('.ab-doel').forEach(function (c) { c.classList.remove('ab-doel'); }); }

    function beweeg() {
        zetGhost();
        const onder = document.elementFromPoint(s.x, s.y);
        if (s.soort === 'prob') {
            const lijst = document.getElementById('ab-problijst'); if (!lijst) return;
            const items = Array.prototype.filter.call(lijst.children, function (c) { return c.classList.contains('ab-prob') && c !== s.el; });
            let voor = null;
            for (const c of items) { const r = c.getBoundingClientRect(); if (s.y < r.top + r.height / 2) { voor = c; break; } }
            if (voor) lijst.insertBefore(s.plek, voor); else if (items.length) items[items.length - 1].after(s.plek);
            return;
        }
        wisDoel();
        s.kopDoel = null;
        const kop = onder && onder.closest('.ab-baankop');
        if (kop) {
            // ingeklapte baan: alleen het probleem wijzigt; uitgeklapte baan: bovenaan in de kolom onder de muis
            if (kop.classList.contains('dicht')) { kop.classList.add('ab-doel'); s.kopDoel = kop.dataset.lane; s.plek.style.display = 'none'; return; }
            const cel = celOnderKop(kop);
            if (cel) { plaatsIn(cel, true); return; }
        }
        const cel = onder && onder.closest('.ab-cel');
        if (cel) plaatsIn(cel, false);
    }

    function celOnderKop(kop) {
        let n = kop.nextElementSibling;
        while (n && n.classList.contains('ab-cel')) {
            const r = n.getBoundingClientRect();
            if (s.x >= r.left && s.x < r.right) return n;
            n = n.nextElementSibling;
        }
        return null;
    }

    function plaatsIn(cel, bovenaan) {
        s.plek.style.display = '';
        cel.classList.add('ab-doel');
        const kaarten = Array.prototype.filter.call(cel.children, function (c) { return c.classList.contains('ab-kaart') && c !== s.el; });
        let voor = null;
        if (bovenaan) voor = kaarten[0] || null;
        else for (const c of kaarten) { const r = c.getBoundingClientRect(); if (s.y < r.top + r.height / 2) { voor = c; break; } }
        const knop = cel.querySelector('.ab-nieuw');
        if (voor) cel.insertBefore(s.plek, voor);
        else if (knop) cel.insertBefore(s.plek, knop);
        else cel.appendChild(s.plek);
    }

    function autoScroll() {
        if (!s || !s.gestart) return;
        const wrap = document.getElementById('ab-wrap');
        const rand = 50, stap = 14;
        if (wrap && s.soort === 'kaart') {
            const r = wrap.getBoundingClientRect();
            if (s.x < r.left + rand) wrap.scrollLeft -= stap; else if (s.x > r.right - rand) wrap.scrollLeft += stap;
            if (s.y < r.top + rand) wrap.scrollTop -= stap; else if (s.y > r.bottom - rand) wrap.scrollTop += stap;
        }
        if (s.y < rand) window.scrollBy(0, -stap); else if (s.y > window.innerHeight - rand) window.scrollBy(0, stap);
        beweeg();
        s.raf = requestAnimationFrame(autoScroll);
    }

    function stop(ok) {
        if (!s) return;
        const x = s; s = null;
        clearTimeout(x.timer);
        if (!x.gestart) return;
        cancelAnimationFrame(x.raf);
        try { document.body.releasePointerCapture(x.pid); } catch (e) { }
        document.body.classList.remove('ab-sleepmodus');
        x.ghost.remove();
        wisDoel();
        if (x.soort === 'kaart') {
            let melding = null;
            const cel = x.plek.parentNode && x.plek.parentNode.classList && x.plek.parentNode.classList.contains('ab-cel') ? x.plek.parentNode : null;
            if (ok && x.kopDoel) melding = [x.kopDoel, null, null, null];
            else if (ok && cel && x.plek.style.display !== 'none') {
                s = x;   // buur() leest s
                const prev = buur(x.plek, -1), next = buur(x.plek, 1);
                s = null;
                if (!(cel === x.bronCel && prev === x.oudPrev && next === x.oudNext))   // zelfde plek: niets doen
                    melding = [cel.dataset.lane, cel.dataset.status, prev, next];
            }
            x.plek.remove(); x.el.style.display = '';
            roep('SleepBezig', false);
            if (melding) roep('KaartLosgelaten', parseInt(x.id), melding[0], melding[1], melding[2], melding[3]);
        } else {
            const ids = [];
            const lijst = document.getElementById('ab-problijst');
            if (lijst) Array.prototype.forEach.call(lijst.children, function (c) {
                if (c === x.plek) ids.push(parseInt(x.id));
                else if (c !== x.el && c.classList.contains('ab-prob')) ids.push(parseInt(c.dataset.id));
            });
            x.plek.remove(); x.el.style.display = '';
            roep('SleepBezig', false);
            if (ok) roep('ProblemenGeordend', ids, parseInt(x.id));
        }
    }
})();
