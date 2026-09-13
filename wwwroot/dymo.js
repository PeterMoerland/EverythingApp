// Printen op een Dymo via DYMO Connect.
//
// DYMO Connect draait op de pc een kleine webservice. Deze code praat daar
// rechtstreeks mee, dus er is geen extra bibliotheek nodig — alleen DYMO Connect
// zelf op de werkplek van wie labels print.
//
// Het adres verschilt per versie en per installatie, dus we proberen ze op
// volgorde en onthouden welke werkte.

window.mdeDymo = (function () {
    const ADRESSEN = [
        "https://127.0.0.1:41951/DYMO/DLS/Printing",
        "https://localhost:41951/DYMO/DLS/Printing",
        "http://127.0.0.1:41951/DYMO/DLS/Printing",
        "http://localhost:41951/DYMO/DLS/Printing"
    ];

    let basis = null;          // het adres dat werkte
    let laatsteFout = "";      // wat er misging, voor de diagnose

    async function haal(adres, pad) {
        const antwoord = await fetch(adres + pad, { method: "GET" });
        if (!antwoord.ok) throw new Error("gaf " + antwoord.status);
        return await antwoord.text();
    }

    // Zoekt het adres waarop de webservice antwoordt.
    async function zoekBasis() {
        if (basis) return basis;

        const fouten = [];
        for (const adres of ADRESSEN) {
            try {
                const tekst = await haal(adres, "/StatusConnected");
                if (tekst.trim().toLowerCase().indexOf("true") >= 0) {
                    basis = adres;
                    return basis;
                }
                fouten.push(adres + ": antwoordde '" + tekst.trim().slice(0, 40) + "'");
            } catch (e) {
                fouten.push(adres + ": " + (e && e.message ? e.message : e));
            }
        }

        laatsteFout = fouten.join(" | ");
        return null;
    }

    // Is DYMO Connect aanwezig en bereikbaar?
    async function beschikbaar() {
        return (await zoekBasis()) !== null;
    }

    // Waarom lukte het niet? Tekst om aan de gebruiker te tonen.
    function uitleg() {
        return laatsteFout;
    }

    // De namen van de aangesloten labelprinters.
    async function printers() {
        const adres = await zoekBasis();
        if (!adres) return [];

        try {
            const xml = await haal(adres, "/GetPrinters");
            const doc = new DOMParser().parseFromString(xml, "text/xml");
            const namen = [];

            // Hoe het antwoord precies heet verschilt per versie van DYMO Connect,
            // dus we zoeken niet op een vaste elementnaam maar op elk element met
            // een <Name> eronder. Tape-printers laten we weg.
            //
            // Op IsConnected filteren we bewust niet: de LabelWriter 450-serie meldt
            // via USB vaak 'False' terwijl printen gewoon lukt. Liever een printer in
            // de lijst die het misschien niet doet, dan een lege lijst.
            let ergensAan = false;

            doc.querySelectorAll("*").forEach(function (p) {
                if (p.nodeName === "TapePrinter") return;

                let naam = null;
                for (let i = 0; i < p.children.length; i++)
                    if (p.children[i].nodeName === "Name") naam = p.children[i];
                if (!naam || !naam.textContent.trim()) return;

                const aan = p.querySelector("IsConnected");
                if (aan && aan.textContent.trim().toLowerCase() === "true") ergensAan = true;

                const tekst = naam.textContent.trim();
                if (namen.indexOf(tekst) < 0) namen.push(tekst);
            });

            if (namen.length === 0)
                laatsteFout = "De service antwoordt, maar meldt geen printer. Antwoord: "
                            + xml.trim().slice(0, 200);
            else if (!ergensAan)
                laatsteFout = "De printer meldt zich als 'niet aan'. Printen kan alsnog lukken; "
                            + "lukt het niet, kijk dan of de printer aanstaat en papier heeft.";
            else
                laatsteFout = "";
            return namen;
        } catch (e) {
            laatsteFout = "GetPrinters " + (e && e.message ? e.message : e);
            return [];
        }
    }

    // Eén label printen. labelXml komt van de server.
    async function print(printerNaam, labelXml, aantal, rol) {
        const adres = await zoekBasis();
        if (!adres) throw new Error("DYMO Connect is niet bereikbaar. " + laatsteFout);

        const params = new URLSearchParams();
        params.append("printerName", printerNaam);
        params.append("printParamsXml",
            '<LabelWriterPrintParams>' +
            '<Copies>' + (aantal || 1) + '</Copies>' +
            '<JobTitle>MDE</JobTitle>' +
            '<FlowDirection>LeftToRight</FlowDirection>' +
            '<PrintQuality>Text</PrintQuality>' +
            '<TwinTurboRoll>' + (rol || "Auto") + '</TwinTurboRoll>' +
            '</LabelWriterPrintParams>');
        params.append("labelXml", labelXml);
        // Sommige versies van de webservice eisen dit veld, ook als het leeg is.
        params.append("labelSetXml", "");

        const antwoord = await fetch(adres + "/PrintLabel", {
            method: "POST",
            headers: { "Content-Type": "application/x-www-form-urlencoded" },
            body: params.toString()
        });

        if (!antwoord.ok) {
            // De uitleg van DYMO zelf meesturen; die zegt meestal precies wat er mis is
            // met de labeltekening (bijvoorbeeld een papiernaam die hij niet kent).
            let tekst = "";
            try { tekst = (await antwoord.text()).slice(0, 300); } catch (e) { }
            throw new Error("DYMO gaf " + antwoord.status + " bij het printen. " + tekst);
        }
        return true;
    }

    // Alles op een rij voor het diagnosevenster.
    async function diagnose() {
        basis = null;               // opnieuw proberen, niet het onthouden adres
        laatsteFout = "";

        const regels = [];
        for (const adres of ADRESSEN) {
            try {
                const tekst = await haal(adres, "/StatusConnected");
                regels.push("OK   " + adres + " → " + tekst.trim().slice(0, 20));
            } catch (e) {
                regels.push("mis  " + adres + " → " + (e && e.message ? e.message : e));
            }
        }

        const gevonden = await zoekBasis();
        if (gevonden) {
            try {
                const xml = await haal(gevonden, "/GetPrinters");
                regels.push("");
                regels.push("GetPrinters:");
                regels.push(xml.slice(0, 1500));
            } catch (e) {
                regels.push("GetPrinters mislukt: " + (e && e.message ? e.message : e));
            }
        }

        return regels.join("\n");
    }

    return {
        beschikbaar: beschikbaar,
        printers: printers,
        print: print,
        uitleg: uitleg,
        diagnose: diagnose
    };
})();
