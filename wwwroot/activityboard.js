// Activity board: kaarten en problemen slepen (muis en touch), zoals prototype v0.5 (spec §4).
// Het scherm (ActivityBoard.razor) koppelt zich met mdeAb.koppel(dotnet); deze code verplaatst alleen
// een eigen plaatshouder en een zwevende kopie, en meldt bij loslaten wat er moet gebeuren:
//   KaartLosgelaten(id, lane, status|null, prevId|null, nextId|null)   status null = op een baankop
//   ProblemenGeordend([ids], verplaatstId)
//   KaartGeklikt(id)                                                   klik zonder slepen: de kaart is gekozen
//   KaartDubbel(id)                                                    dubbelklik (of een tik op een aanraakscherm): openen
//   SleepBezig(true|false)                                             zolang er gesleept wordt geen herlaad
//   LeegGeklikt()                                                      klik naast de kaarten: geen kaart meer gekozen
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
        focusEl: function (sel) { const el = document.querySelector(sel); if (el) { el.focus(); if (el.select) el.select(); } },
        // een tekstvak dat je gaat bewerken: de cursor achteraan, niets geselecteerd
        focusEind: function (sel) {
            const el = document.querySelector(sel);
            if (!el) return;
            el.focus();
            try { const n = el.value.length; el.setSelectionRange(n, n); } catch (e) { }
        }
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
        // muis: een klik kiest de kaart, dubbelklik opent hem (zie dblclick hieronder); aanraken: een tik opent hem meteen
        if (x.soort === 'kaart') roep(x.touch ? 'KaartDubbel' : 'KaartGeklikt', parseInt(x.id));
        else { const g = x.el.querySelector('.ab-greep'); if (g) g.focus(); }
    });
    document.addEventListener('pointercancel', function () { stop(false); });
    // Esc sluit de pop-up met eigenschappen, ook als de cursor er niet (meer) in staat: na een keuze uit "+ type toevoegen…"
    // wordt die lijst opnieuw gemaakt en is de focus weg. Staat de cursor in de pop-up, dan vangt die Esc zelf (PaneelToets).
    document.addEventListener('keydown', function (e) {
        if (!doel || s || e.key !== 'Escape' || !document.querySelector('.ab-drawer')) return;
        if (document.querySelector('.ab-modal, .ab-menu')) return;
        if (e.target.closest && e.target.closest('.ab-drawer')) return;
        roep('SluitPopup');
    });

    // Dubbelklik op een kaart opent de eigenschappen (besluit gebruiker 19-11-2026; ook via het rechtermuismenu en Enter).
    document.addEventListener('dblclick', function (e) {
        if (!doel) return;
        const kaart = e.target.closest && e.target.closest('.ab-kaart[data-id]');
        if (!kaart || e.target.closest('button, input, select, textarea, a')) return;
        roep('KaartDubbel', parseInt(kaart.dataset.id));
    });

    // Enter opent de gekozen kaart (rode rand), ook als de cursor er niet op staat: na een klik op een kaart heeft die de
    // focus en regelt Blazor het zelf (KaartToets); dit is voor de keren dat de focus elders is (body na verversen, na
    // het sluiten van een venster). Niet in een invoerveld, op een knop of met een pop-up, venster of menu open.
    document.addEventListener('keydown', function (e) {
        if (!doel || s || e.key !== 'Enter' || e.ctrlKey || e.altKey || e.metaKey || e.shiftKey) return;
        if (document.querySelector('.ab-drawer, .ab-modal, .ab-menu')) return;
        const t = e.target;
        if (t && t.closest && t.closest('input, textarea, select, button, a, [role="button"], [tabindex], [contenteditable="true"]')) return;
        const k = document.querySelector('.page.ab .ab-kaart.gekozen[data-id], .page.ab tr.ab-rij.gekozen[data-id]');
        if (!k) return;
        e.preventDefault();
        roep('KaartDubbel', parseInt(k.dataset.id));
    });

    // Klik op een lege plek van het board (geen kaart, regel, knop of invoerveld): er is geen kaart meer gekozen.
    // Tijdens slepen heeft body de muis vast: dan is het doel body. Alleen melden als er iets gekozen is.
    document.addEventListener('click', function (e) {
        if (!doel || s || e.button !== 0 || !document.querySelector('.ab-kaart.gekozen, tr.ab-rij.gekozen')) return;
        const t = e.target;
        if (!t.closest || !t.closest('.page.ab')) return;
        if (t.closest('button, input, select, textarea, a, label, [role="button"], tr.ab-rij, .ab-snel')) return;
        // de schuifbalk van het bord of de tabel is geen lege plek
        if ((t.id === 'ab-wrap' || t.classList.contains('ab-tabwrap')) && (e.offsetX >= t.clientWidth || e.offsetY >= t.clientHeight)) return;
        roep('LeegGeklikt');
    });
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

    // Afvinklijst: een punt of kopje slepen aan de greep (HTML5 drag and drop; het loslaten zelf handelt Blazor af met
    // @ondrop: PuntDrop/PuntDropEinde). De browser staat loslaten alleen toe als dragover geannuleerd is; het
    // @ondragover:preventDefault in de pagina deed dat niet, dus slepen deed niets (melding Arnold 24-11-2026). Een eigen
    // gegevenssoort: Firefox begint anders niet met slepen, en gewone tekst zou in het invoervak van een punt belanden.
    let puntSleep = false;
    document.addEventListener('dragstart', function (e) {
        puntSleep = !!(e.target.closest && e.target.closest('.ab-pgreep'));
        if (!puntSleep || !e.dataTransfer) return;
        try { e.dataTransfer.setData('application/x-mde-punt', '1'); e.dataTransfer.effectAllowed = 'move'; } catch (x) { }
    });
    document.addEventListener('dragend', function () { puntSleep = false; document.querySelectorAll('.ab-pdoel').forEach(function (c) { c.classList.remove('ab-pdoel'); }); });
    document.addEventListener('dragover', function (e) {
        if (!puntSleep) return;
        const rij = e.target.closest && e.target.closest('.ab-punt, .ab-pkop, .ab-pnieuw');
        document.querySelectorAll('.ab-pdoel').forEach(function (c) { if (c !== rij) c.classList.remove('ab-pdoel'); });
        if (!rij) return;
        e.preventDefault();
        if (e.dataTransfer) e.dataTransfer.dropEffect = 'move';
        rij.classList.add('ab-pdoel');
    });
    document.addEventListener('drop', function (e) {
        if (!puntSleep) return;
        e.preventDefault();
        document.querySelectorAll('.ab-pdoel').forEach(function (c) { c.classList.remove('ab-pdoel'); });
    });

    // Afbeeldingen (punt van Arnold 24-11-2026): een bestand uit Verkenner op een tekstveld van de open activiteit
    // slepen (.ab-tveld) zet het daar meteen bij; op een kaartje van het bord (.ab-kaart) vraagt het scherm eerst onder
    // welk veld (AfbeeldingOpKaart, daarna mdeAb.stuurWacht). Het uploaden gaat naar /activityboard/afbeelding; daarna
    // AfbeeldingenGewijzigd(activiteit, aantal, fout). Buiten zo'n plek mag je op het board niets loslaten, anders
    // opent de browser de afbeelding in plaats van het board.
    let wacht = null;   // { id, files }: losgelaten op een kaartje, wacht op de keuze van het veld
    function heeftBestanden(e) { const t = e.dataTransfer && e.dataTransfer.types; return !!t && Array.prototype.indexOf.call(t, 'Files') >= 0; }
    function afbDoel(e) {
        if (!e.target.closest) return null;
        if (document.querySelector('.ab-modal')) return null;
        if (document.querySelector('.ab-drawer')) return e.target.closest('.ab-drawer .ab-tveld[data-veld]');
        return e.target.closest('.ab-kaart[data-id]');
    }
    function wisAfbDoel() { document.querySelectorAll('.ab-afbdoel').forEach(function (c) { c.classList.remove('ab-afbdoel'); }); }
    function stuur(id, veld, files) {
        const fd = new FormData();
        files.forEach(function (f) { fd.append('bestand', f, f.name); });
        fetch(new URL('activityboard/afbeelding?activiteit=' + id + '&veld=' + encodeURIComponent(veld), document.baseURI), { method: 'POST', body: fd, credentials: 'same-origin' })
            .then(function (r) { return r.ok ? r.json() : r.text().then(function (t) { throw new Error(t || ('fout ' + r.status)); }); })
            .then(function (j) { roep('AfbeeldingenGewijzigd', id, j.toegevoegd || 0, (j.geweigerd || []).join('; ')); })
            .catch(function (x) { roep('AfbeeldingenGewijzigd', id, 0, String((x && x.message) || x)); });
    }
    window.mdeAb.stuurWacht = function (veld) { const w = wacht; wacht = null; if (w && veld) stuur(w.id, veld, w.files); };
    window.mdeAb.vergeetWacht = function () { wacht = null; };
    document.addEventListener('dragover', function (e) {
        if (!doel || !heeftBestanden(e) || !document.querySelector('.page.ab')) return;
        e.preventDefault();
        const d = afbDoel(e);
        wisAfbDoel();
        e.dataTransfer.dropEffect = d ? 'copy' : 'none';
        if (d) d.classList.add('ab-afbdoel');
    });
    document.addEventListener('dragleave', function (e) { if (!e.relatedTarget) wisAfbDoel(); });
    document.addEventListener('drop', function (e) {
        if (!doel || !heeftBestanden(e) || !document.querySelector('.page.ab')) return;
        e.preventDefault();
        const d = afbDoel(e);
        wisAfbDoel();
        const files = Array.prototype.slice.call(e.dataTransfer.files || []);
        if (!d || !files.length) return;
        if (d.classList.contains('ab-tveld')) stuur(parseInt(d.dataset.act), d.dataset.veld, files);
        else { wacht = { id: parseInt(d.dataset.id), files: files }; roep('AfbeeldingOpKaart', wacht.id, files.length); }
    });

    // Plakken met Ctrl+V (wens gebruiker 24-11-2026): staat de pop-up van een activiteit open en zit er een plaatje op
    // het klembord (schermafdruk, gekopieerde afbeelding), dan komt het bij het veld waar de cursor in staat (het
    // tekstvak of het vakje met afbeeldingen ernaast), anders bij het veld onder de muis, en anders vraagt het scherm
    // onder welk veld. Tekst plakken blijft gewoon tekst plakken.
    document.addEventListener('paste', function (e) {
        if (!doel || !document.querySelector('.ab-drawer') || document.querySelector('.ab-modal')) return;
        const eerste = document.querySelector('.ab-drawer .ab-tveld[data-veld]');
        if (!eerste) return;      // nieuw kaartje zonder nummer, of het script is niet uitgevoerd
        const plaatjes = Array.prototype.filter.call((e.clipboardData && e.clipboardData.files) || [], function (f) { return f.type.indexOf('image/') === 0; });
        if (!plaatjes.length) return;
        e.preventDefault();
        const d = new Date(), twee = function (n) { return (n < 10 ? '0' : '') + n; };
        const stempel = d.getFullYear() + '-' + twee(d.getMonth() + 1) + '-' + twee(d.getDate()) + ' ' + twee(d.getHours()) + twee(d.getMinutes()) + twee(d.getSeconds());
        const files = plaatjes.map(function (f, i) {
            // een schermafdruk heet altijd "image.png": geef hem een naam met datum en tijd
            const ext = f.type === 'image/jpeg' ? '.jpg' : '.' + (f.type.split('/')[1] || 'png').replace('+xml', '');
            const naam = f.name && !/^image\.\w+$/i.test(f.name) ? f.name : 'plaatje ' + stempel + (plaatjes.length > 1 ? ' ' + (i + 1) : '') + ext;
            return new File([f], naam, { type: f.type });
        });
        const veld = (e.target.closest && e.target.closest('.ab-drawer .ab-tveld[data-veld]')) || document.querySelector('.ab-drawer .ab-tveld[data-veld]:hover');
        if (veld) stuur(parseInt(veld.dataset.act), veld.dataset.veld, files);
        else { wacht = { id: parseInt(eerste.dataset.act), files: files }; roep('AfbeeldingOpKaart', wacht.id, files.length); }
    });

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
        // de randen van wat er schuift: het werkscherm (onder de bovenbalk), anders het venster
        const m = document.querySelector('main.inhoud');
        const mr = m && m.scrollHeight > m.clientHeight ? m.getBoundingClientRect() : { top: 0, bottom: window.innerHeight };
        if (s.y < mr.top + rand) paginaSchuif(-stap); else if (s.y > mr.bottom - rand) paginaSchuif(stap);
        beweeg();
        s.raf = requestAnimationFrame(autoScroll);
    }

    // De pagina schuiven tijdens het slepen: het werkscherm (main.inhoud) als dat de schuifbalk heeft, anders het venster
    function paginaSchuif(dy) {
        const m = document.querySelector('main.inhoud');
        if (m && m.scrollHeight > m.clientHeight) m.scrollBy(0, dy); else window.scrollBy(0, dy);
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
