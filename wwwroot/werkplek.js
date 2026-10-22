// Werkplek (gedeelde pc): elke minuut aan de app doorgeven of er in die minuut geklikt, getypt, gescrold of met de muis
// bewogen is. De app schuift dan de minuten tot afmelden van de aangemelde medewerker op, en ziet of de tijd om is of dat
// een ander tabblad iemand anders aanmeldde (Components/Layout/WerkplekMedewerker.razor).
// Alleen wat in dit browservenster gebeurt telt: werken in een ander programma (TwinCAT, Inventor) ziet de browser niet.
(function () {
    'use strict';
    let doel = null, nummer = 0, actief = false, klok = null;
    function merk() { actief = true; }
    ['pointerdown', 'pointermove', 'keydown', 'wheel', 'touchstart'].forEach(function (t) {
        document.addEventListener(t, merk, { passive: true, capture: true });
    });
    window.mdeWerkplek = {
        // Geeft een nummer terug; los(nummer) van een ouder exemplaar zet een nieuwere koppeling dan niet stil.
        koppel: function (dotnet) {
            doel = dotnet;
            nummer++;
            if (klok) clearInterval(klok);
            klok = setInterval(function () {
                if (!doel) return;
                const was = actief; actief = false;
                // lukt het doorgeven niet (verbinding even weg), dan de activiteit bewaren voor de volgende minuut
                doel.invokeMethodAsync('Tik', was).catch(function () { if (was) actief = true; });
            }, 60000);
            return nummer;
        },
        los: function (n) {
            if (n && n !== nummer) return;
            doel = null;
            if (klok) { clearInterval(klok); klok = null; }
        }
    };
})();
