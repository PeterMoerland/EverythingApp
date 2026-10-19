// Klembord lezen voor Bestellingen ▸ Plakken uit klembord (Components/PlakVenster.razor). Geeft null als de browser
// het niet toestaat (geen toestemming, geen https); dan plakt de gebruiker met Ctrl+V in het vak.
window.mdeKlembord = {
    lees: async function () {
        try { return await navigator.clipboard.readText(); } catch (e) { return null; }
    },

    // Bestellingen ▸ Importeren ▸ Uit screenshot (Components/ScreenshotVenster.razor): een geplakte afbeelding (of PDF)
    // opvangen. Blazor kan grote berichten niet in één keer ontvangen; daarom alleen een seintje naar .NET, dat daarna
    // de inhoud als stroom ophaalt (afbeelding()).
    _laatste: null,
    koppelPlakken: function (element, dotnet) {
        if (!element || element._mdeGekoppeld) return;
        element._mdeGekoppeld = true;
        element.addEventListener('paste', async function (e) {
            const items = (e.clipboardData && e.clipboardData.items) || [];
            for (const it of items) {
                if (it.kind === 'file' && (it.type.startsWith('image/') || it.type === 'application/pdf')) {
                    e.preventDefault();
                    const f = it.getAsFile();
                    window.mdeKlembord._laatste = new Uint8Array(await f.arrayBuffer());
                    await dotnet.invokeMethodAsync('AfbeeldingGeplakt', f.type || 'image/png', window.mdeKlembord._laatste.length);
                    return;
                }
            }
            await dotnet.invokeMethodAsync('GeenAfbeelding');
        });
    },
    afbeelding: function () {
        const b = window.mdeKlembord._laatste;
        window.mdeKlembord._laatste = null;
        return b;   // Blazor maakt er zelf een stroom van (InvokeAsync<IJSStreamReference>)
    }
};
