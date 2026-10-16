// Klembord lezen voor Bestellingen ▸ Plakken uit klembord (Components/PlakVenster.razor). Geeft null als de browser
// het niet toestaat (geen toestemming, geen https); dan plakt de gebruiker met Ctrl+V in het vak.
window.mdeKlembord = {
    lees: async function () {
        try { return await navigator.clipboard.readText(); } catch (e) { return null; }
    }
};
