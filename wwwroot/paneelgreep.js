// Sleepgrepen tussen panelen die onder elkaar staan (Producten: typelijst, exemplaren), en rustig scrollen in
// korte lijsten. Wens collega 5-11-2026: "de vensters schaalbaar; het middelste venster verspringt met 1 scroll
// zo ver dat je soms een exemplaar niet kunt kiezen".
//
// <div class="paneelgreep" data-doel=".product-namen" data-sleutel="producten.namen"></div>
//   Slepen = het paneel dat data-doel aanwijst (in dezelfde ouder) hoger/lager; onthouden per browser.
//   Dubbelklik = terug naar de standaardhoogte.
// <div ... data-rustig-scrollen> : één tik van het muiswiel schuift hoogstens een halve lijst op.
(function () {
    var PREFIX = 'mde.hoogte.';

    function lees(sleutel) { try { return localStorage.getItem(PREFIX + sleutel); } catch (e) { return null; } }
    function schrijf(sleutel, waarde) {
        try { if (waarde === null) localStorage.removeItem(PREFIX + sleutel); else localStorage.setItem(PREFIX + sleutel, waarde); } catch (e) { }
    }

    function doelVan(greep) {
        var ouder = greep.parentElement;
        return ouder ? ouder.querySelector(greep.dataset.doel) : null;
    }

    function zet(doel, hoogte) {
        doel.style.height = hoogte + 'px';
        doel.style.maxHeight = 'none';
        doel.style.flex = '0 0 auto';
    }

    function wis(doel) {
        doel.style.height = '';
        doel.style.maxHeight = '';
        doel.style.flex = '';
    }

    // Grenzen: minstens ~2 regels, en de kolom moet nog ruimte houden voor de rest.
    function begrens(doel, hoogte) {
        var kolom = doel.parentElement;
        var max = kolom ? kolom.clientHeight - 140 : 2000;
        return Math.round(Math.max(48, Math.min(hoogte, Math.max(48, max))));
    }

    window.mdeGreep = {
        // Opgeslagen hoogtes toepassen (na het tekenen van de pagina).
        herstel: function () {
            document.querySelectorAll('.paneelgreep[data-sleutel]').forEach(function (greep) {
                var doel = doelVan(greep);
                var h = parseInt(lees(greep.dataset.sleutel), 10);
                if (doel && h > 0) zet(doel, begrens(doel, h));
            });
        }
    };

    document.addEventListener('pointerdown', function (e) {
        var greep = e.target.closest && e.target.closest('.paneelgreep');
        if (!greep || e.button !== 0) return;
        var doel = doelVan(greep);
        if (!doel) return;
        e.preventDefault();
        var startY = e.clientY, startH = doel.getBoundingClientRect().height;
        greep.classList.add('bezig');
        greep.setPointerCapture(e.pointerId);

        function beweeg(ev) { zet(doel, begrens(doel, startH + ev.clientY - startY)); }
        function los() {
            greep.classList.remove('bezig');
            greep.removeEventListener('pointermove', beweeg);
            greep.removeEventListener('pointerup', los);
            greep.removeEventListener('pointercancel', los);
            if (greep.dataset.sleutel) schrijf(greep.dataset.sleutel, String(Math.round(doel.getBoundingClientRect().height)));
        }
        greep.addEventListener('pointermove', beweeg);
        greep.addEventListener('pointerup', los);
        greep.addEventListener('pointercancel', los);
    });

    document.addEventListener('dblclick', function (e) {
        var greep = e.target.closest && e.target.closest('.paneelgreep');
        if (!greep) return;
        var doel = doelVan(greep);
        if (doel) wis(doel);
        if (greep.dataset.sleutel) schrijf(greep.dataset.sleutel, null);
    });

    // Muiswiel: in een korte lijst schoof één tik (~100 px) verder dan de lijst hoog is.
    document.addEventListener('wheel', function (e) {
        var lijst = e.target.closest && e.target.closest('[data-rustig-scrollen]');
        if (!lijst || e.ctrlKey || lijst.scrollHeight <= lijst.clientHeight) return;
        var regel = lijst.firstElementChild ? lijst.firstElementChild.offsetHeight : 24;
        if (!regel || regel > lijst.clientHeight) regel = 24;
        var px = e.deltaMode === 1 ? e.deltaY * regel : e.deltaMode === 2 ? e.deltaY * lijst.clientHeight : e.deltaY;
        var max = Math.max(regel, Math.floor(lijst.clientHeight * 0.5 / regel) * regel);
        if (Math.abs(px) <= max) return;             // touchpad en kleine stapjes: gewoon
        e.preventDefault();
        lijst.scrollTop += (px > 0 ? 1 : -1) * max;
    }, { passive: false });
})();
