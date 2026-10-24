// Barcodes scannen met de camera van de telefoon.
//
// Leest met de BarcodeDetector. Op Android zit die in de browser ingebouwd;
// op de iPhone (en in Firefox) halen we eenmalig een nabootsing op die op
// WebAssembly draait (de moderne ZXing-C++). Die leest kleine en licht
// onscherpe streepjescodes bijna zo goed als een echte scanapp — veel beter
// dan de oude JavaScript-bibliotheek die hier eerst zat.
//
// De lezer krijgt niet het hele beeld maar de brede strook in het midden,
// waar de rode richtlijn op het scherm naar wijst: minder zoekwerk per
// beeld, dus sneller en trefzekerder.
//
// Let op: de camera werkt alleen op een https-adres. Op http weigert de
// browser hem stilletjes; dan melden we dat netjes.

window.mdeScan = (function () {
    let stroom = null;        // de camerastroom, om hem weer uit te kunnen zetten
    let bezig = false;
    let laatste = "";         // vorige code, tegen dubbel vuren op hetzelfde label
    let laatsteTijd = 0;
    let NageaapteDetector = null;   // de opgehaalde nabootsing, eenmalig

    function veilig() {
        return window.isSecureContext === true;
    }

    // ---- het piepje bij een geslaagde scan ----
    // Browsers laten geluid pas toe na een aanraking van de gebruiker; daarom
    // wordt het geluid bij de eerste tik op de pagina ontgrendeld.
    let audio = null;
    function ontgrendelGeluid() {
        try {
            if (!audio) audio = new (window.AudioContext || window.webkitAudioContext)();
            if (audio.state === "suspended") audio.resume();
        } catch (e) { /* dan geen piepje; trillen blijft over */ }
    }
    document.addEventListener("touchend", ontgrendelGeluid, true);
    document.addEventListener("click", ontgrendelGeluid, true);

    function piep() {
        if (!audio || audio.state !== "running") return;
        try {
            const toon = audio.createOscillator();
            const sterkte = audio.createGain();
            toon.type = "square";
            toon.frequency.value = 1245;                    // kort en helder
            sterkte.gain.setValueAtTime(0.2, audio.currentTime);
            sterkte.gain.exponentialRampToValueAtTime(0.001, audio.currentTime + 0.15);
            toon.connect(sterkte);
            sterkte.connect(audio.destination);
            toon.start();
            toon.stop(audio.currentTime + 0.15);
        } catch (e) { /* geen ramp */ }
    }

    // Geeft door of dit een echte scan is (zelfde label binnen twee tellen
    // is dezelfde scan) en meldt hem dan bij Blazor.
    async function meld(tekst, dotnet) {
        // Sommige lezers zetten de AIM-code van het soort barcode ervoor (bv. "]C1" = Code 128 / GS1-128) en
        // geven FNC1/GS als teken \x1D mee: dat hoort niet bij het nummer (melding 24-10-2026: "]C1MDE56262").
        tekst = (tekst || "").replace(/^\][A-Za-z][0-9A-Za-z]/, "").replace(/[\x00-\x1F]/g, "").trim();
        const nu = Date.now();
        if (!tekst || (tekst === laatste && nu - laatsteTijd <= 2000)) return;
        laatste = tekst;
        laatsteTijd = nu;
        piep();
        if (navigator.vibrate) navigator.vibrate(80);
        await dotnet.invokeMethodAsync("Gescand", tekst);
    }

    // De BarcodeDetector-nabootsing eenmalig ophalen (iPhone/Firefox).
    // Komt van een CDN; de WebAssembly-motor wordt daarna door de browser
    // in de cache gehouden.
    async function haalDetector() {
        if (NageaapteDetector) return NageaapteDetector;
        const mod = await import("https://cdn.jsdelivr.net/npm/barcode-detector@3/dist/es/index.min.js");
        NageaapteDetector = mod.BarcodeDetector;
        return NageaapteDetector;
    }

    async function start(videoId, dotnet) {
        stop();   // een eventuele vorige sessie eerst netjes afsluiten

        if (!veilig()) return "De camera werkt alleen via https. Typ het nummer, of gebruik een bluetooth-scanner.";
        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia)
            return "Deze browser kan niet bij de camera. Typ het nummer, of gebruik een bluetooth-scanner.";

        const video = document.getElementById(videoId);
        if (!video) return "Camerabeeld niet gevonden op de pagina.";

        let lezer;
        try {
            // Zonder ingebouwde lezer eerst de nabootsing halen; gaat dat
            // mis, dan hoeft de camera niet eens aan.
            const Detector = ("BarcodeDetector" in window)
                ? window.BarcodeDetector
                : await haalDetector();

            // Zonder resolutieverzoek geeft de browser 640x480 — daar zijn de
            // dunne streepjes van een Code 128 niet in te onderscheiden.
            stroom = await navigator.mediaDevices.getUserMedia({
                video: {
                    facingMode: "environment",          // de achtercamera
                    width: { ideal: 1920 },
                    height: { ideal: 1080 }
                },
                audio: false
            });

            // Waar de camera het ondersteunt: doorlopend scherpstellen en
            // 2x inzoomen. De kleine Dymo-labels vullen dan meer van het
            // beeld, en een camera die blijft focussen leest veel sneller.
            try {
                const spoor = stroom.getVideoTracks()[0];
                const kan = spoor.getCapabilities ? spoor.getCapabilities() : {};
                const wens = [];
                if (kan.focusMode && kan.focusMode.indexOf("continuous") >= 0)
                    wens.push({ focusMode: "continuous" });
                if (kan.zoom && kan.zoom.max >= 1.4)
                    wens.push({ zoom: Math.min(1.4, kan.zoom.max) });
                if (wens.length > 0)
                    await spoor.applyConstraints({ advanced: wens });
            } catch (e) { /* niet erg; dan blijft de standaardinstelling */ }

            video.srcObject = stroom;
            await video.play();

            // De echte resolutie terugmelden aan de pagina, zodat te zien is
            // of het verzoek om HD ook gehonoreerd wordt.
            try {
                const inst = stroom.getVideoTracks()[0].getSettings();
                if (inst.width) await dotnet.invokeMethodAsync("CameraInfo", inst.width + "×" + inst.height);
            } catch (e) { /* alleen ter informatie */ }

            lezer = new Detector({ formats: ["code_128", "ean_13", "qr_code"] });
        } catch (e) {
            stop();   // anders blijft het cameralampje branden na een fout
            return "Camera starten mislukt: " + (e && e.name === "NotAllowedError"
                ? "geef de browser toestemming (adresbalk > camera)."
                : (e && e.message ? e.message : e));
        }

        bezig = true;

        // Het middenstuk van het beeld gaat naar de lezer; hierop tekenen we
        // elk beeld opnieuw.
        const doek = document.createElement("canvas");
        const ctx = doek.getContext("2d", { willReadFrequently: true });

        const kijk = async function () {
            if (!bezig) return;
            // Alles binnen try: één onverwachte fout mag de lus niet
            // stilletjes beëindigen — dan zou een tweede scan nooit meer lukken.
            try {
                const vb = video.videoWidth, vh = video.videoHeight;
                const kb = video.clientWidth, kh = video.clientHeight;
                if (vb > 0 && vh > 0 && kb > 0 && kh > 0) {
                    // Precies het stuk dat in het venster zichtbaar is.
                    // object-fit: cover schaalt tot het venster gevuld is en
                    // snijdt de rest af; hier rekenen we terug welk deel van
                    // het camerabeeld dat is, zodat de lezer niets ziet dat
                    // de gebruiker ook niet ziet.
                    const schaal = Math.max(kb / vb, kh / vh);
                    const zb = Math.min(vb, Math.round(kb / schaal));
                    const zh = Math.min(vh, Math.round(kh / schaal));
                    const zx = Math.round((vb - zb) / 2);
                    const zy = Math.round((vh - zh) / 2);
                    doek.width = zb;
                    doek.height = zh;
                    ctx.drawImage(video, zx, zy, zb, zh, 0, 0, zb, zh);
                    const codes = await lezer.detect(doek);
                    if (codes.length > 0) await meld(codes[0].rawValue, dotnet);
                }
            } catch (e) { /* één mislukte poging is geen ramp; volgende beeld */ }
            setTimeout(kijk, 160);
        };
        kijk();

        return "";   // leeg = gelukt
    }

    function stop() {
        bezig = false;
        laatste = "";
        laatsteTijd = 0;
        if (stroom) {
            stroom.getTracks().forEach(function (t) { t.stop(); });
            stroom = null;
        }
    }

    return { start: start, stop: stop };
})();
