// Kabelconfigurator (Components/Pages/Kabelconfigurator.razor): de tekening afdrukken (PDF via de printdialoog van de
// browser), als SVG-bestand bewaren en de spec als tekst op het klembord zetten.
window.mdeKabel = {
    // Zet de tekening in een onzichtbaar iframe en start daar de printdialoog; "Opslaan als PDF" geeft het bestand voor
    // LDZY. Een iframe en geen nieuw venster: dat heeft geen last van een pop-upblokkering. Geeft false als het niet lukt.
    afdrukken(svg, titel) {
        try {
            const oud = document.getElementById('kabel-print');
            if (oud) oud.remove();
            const veilig = (titel || 'Kabel').replace(/[<>&]/g, '');
            const f = document.createElement('iframe');
            f.id = 'kabel-print';
            f.setAttribute('style', 'position:fixed;right:0;bottom:0;width:0;height:0;border:0;visibility:hidden');
            f.srcdoc = '<!doctype html><html><head><meta charset="utf-8"><title>' + veilig + '</title>' +
                '<style>@page { size: A4 landscape; margin: 8mm; } html, body { margin: 0; background: #fff; } ' +
                'svg { width: 100%; height: auto; display: block; }</style></head><body>' + svg + '</body></html>';
            f.onload = () => {
                setTimeout(() => {
                    try { f.contentWindow.focus(); f.contentWindow.print(); }
                    catch (e) { console.warn('afdrukken', e); }
                    // het iframe pas later weghalen: tijdens de printdialoog moet het blijven bestaan
                    setTimeout(() => f.remove(), 60000);
                }, 150);
            };
            document.body.appendChild(f);
            return true;
        } catch (e) {
            console.warn('afdrukken', e);
            return false;
        }
    },

    download(naam, svg) {
        const blob = new Blob([svg], { type: 'image/svg+xml;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = naam;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    },

    async kopieer(tekst) {
        try { await navigator.clipboard.writeText(tekst); return true; }
        catch (e) { return false; }
    },

    // Selecteert de tekst in het tekstvak (klasse) zodat Ctrl+C genoeg is.
    selecteer(klasse) {
        const t = document.querySelector('.' + klasse + ' textarea');
        if (t) t.select();
    }
};
