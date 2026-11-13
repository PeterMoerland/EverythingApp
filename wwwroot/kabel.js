// Kabelconfigurator (Components/Pages/Kabelconfigurator.razor): de aanvraagtekening als PDF-bestand bewaren (de server
// maakt de PDF, hier alleen het downloaden) en de spec als tekst op het klembord zetten.
window.mdeKabel = {
    // Een bestand dat de server net gemaakt heeft (bytes) laten downloaden onder de gegeven naam.
    downloadBestand(naam, bytes, soort) {
        try {
            const blob = new Blob([bytes], { type: soort || 'application/octet-stream' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = naam;
            document.body.appendChild(a);
            a.click();
            a.remove();
            setTimeout(() => URL.revokeObjectURL(url), 2000);
            return true;
        } catch (e) {
            console.warn('downloaden', e);
            return false;
        }
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
