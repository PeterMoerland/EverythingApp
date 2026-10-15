// Werkplek (gedeelde pc): elke minuut aan de app doorgeven of er in die minuut geklikt, getypt of gescrold is.
// De app schuift dan de 30 minuten van de aangemelde medewerker op, en ziet of de tijd om is of dat een ander
// tabblad iemand anders aanmeldde (Components/Layout/WerkplekMedewerker.razor).
(function () {
    'use strict';
    let doel = null, actief = false, klok = null;
    function merk() { actief = true; }
    ['pointerdown', 'keydown', 'wheel', 'touchstart'].forEach(function (t) {
        document.addEventListener(t, merk, { passive: true, capture: true });
    });
    window.mdeWerkplek = {
        koppel: function (dotnet) {
            doel = dotnet;
            if (klok) clearInterval(klok);
            klok = setInterval(function () {
                if (!doel) return;
                const was = actief; actief = false;
                doel.invokeMethodAsync('Tik', was).catch(function () { });
            }, 60000);
        },
        los: function () { doel = null; if (klok) { clearInterval(klok); klok = null; } }
    };
})();
