// Snelbalk bovenin (Components/Layout/Snelbalk.razor): paginatitel voor een nieuwe tegel, een eigen plaatje
// verkleinen tot 64x64 (vierkant uit het midden, PNG) en Alt+1 … Alt+0 = tegel 1 … 10 openen.
(function () {
    'use strict';
    window.mdeSnel = {
        titel: function () { return document.title || ''; },

        // Het gekozen bestand uit <input type="file" id="…">; geeft een data-URL (PNG 64x64) of '' als het geen plaatje is.
        plaatje: function (inputId) {
            return new Promise(function (klaar) {
                const inp = document.getElementById(inputId);
                const f = inp && inp.files && inp.files[0];
                if (!f || !/^image\//.test(f.type)) { klaar(''); return; }
                const url = URL.createObjectURL(f);
                const img = new Image();
                img.onload = function () {
                    const z = 64, c = document.createElement('canvas');
                    c.width = z; c.height = z;
                    const s = Math.max(z / img.naturalWidth, z / img.naturalHeight);
                    const w = img.naturalWidth * s, h = img.naturalHeight * s;
                    const ctx = c.getContext('2d');
                    ctx.imageSmoothingQuality = 'high';
                    ctx.drawImage(img, (z - w) / 2, (z - h) / 2, w, h);
                    URL.revokeObjectURL(url);
                    try { klaar(c.toDataURL('image/png')); } catch (e) { klaar(''); }
                };
                img.onerror = function () { URL.revokeObjectURL(url); klaar(''); };
                img.src = url;
            });
        },

        focus: function (sel) { const el = document.querySelector(sel); if (el) { el.focus(); if (el.select) el.select(); } }
    };

    document.addEventListener('keydown', function (e) {
        if (!e.altKey || e.ctrlKey || e.metaKey || e.shiftKey) return;
        const m = /^Digit([0-9])$/.exec(e.code || '');
        if (!m) return;
        const n = m[1] === '0' ? 10 : parseInt(m[1]);
        const a = document.querySelector('.snel-tegel[data-nr="' + n + '"]');
        if (a) { e.preventDefault(); a.click(); }
    });
})();
