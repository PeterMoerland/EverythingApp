// Camerabeelden verversen.
//
// Elk beeld is een gewone <img> die de app om een verse foto vraagt. Dat gebeurt
// hier in de browser en niet via Blazor: zo gaat er geen beeldverkeer over de
// serververbinding, en blijft de rest van de app vlot.
//
// Een nieuw beeld wordt pas opgevraagd als het vorige binnen is. Een trage of
// dode camera houdt de andere daardoor niet op, en er stapelt niets op.

window.mdeCamera = (function () {
    let lopers = [];      // per beeld een tijdklok, om alles weer te kunnen stoppen

    function volg(img, adres, msTussen) {
        let bezig = false;
        let gestopt = false;

        function volgende() {
            if (gestopt || bezig) return;
            bezig = true;

            const vers = new Image();
            vers.onload = function () {
                if (!gestopt) {
                    img.src = vers.src;
                    img.classList.remove("cam-weg");
                }
                bezig = false;
            };
            vers.onerror = function () {
                if (!gestopt) img.classList.add("cam-weg");
                bezig = false;
            };
            // De tijdstempel dwingt een verse ophaling af; anders toont de
            // browser het beeld uit zijn geheugen.
            vers.src = adres + (adres.indexOf("?") < 0 ? "?" : "&") + "t=" + Date.now();
        }

        volgende();
        const klok = setInterval(volgende, msTussen);
        return function () { gestopt = true; clearInterval(klok); };
    }

    // Start het verversen voor alle beelden met data-cam op de pagina.
    function start(msTussen) {
        stop();
        document.querySelectorAll("img[data-cam]").forEach(function (img) {
            lopers.push(volg(img, img.getAttribute("data-cam"), msTussen || 2000));
        });
        return lopers.length;
    }

    function stop() {
        lopers.forEach(function (uit) { uit(); });
        lopers = [];
    }

    return { start: start, stop: stop };
})();
