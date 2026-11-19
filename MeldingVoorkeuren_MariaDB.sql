-- ===========================================================================
--  MeldingVoorkeuren: welke meldingen een werknemer wel of niet wil krijgen
--  (Werknemers ▸ je eigen naam ▸ tabblad Meldingen). Eerste soort: 'activiteit'
--  = Activity board toewijzing (een collega wijst je toe of maakt een activiteit
--  voor je aan).
--
--  Alleen wat iemand zelf gekozen heeft staat hierin; staat er geen regel, dan
--  geldt de standaard van die soort (in de app: Services/MeldingVoorkeuren.cs).
--  Zolang deze tabel ontbreekt krijgt iedereen alle meldingen en is het tabblad
--  alleen-lezen.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `MeldingVoorkeuren` (
    `WerknemerID`  INT NOT NULL,                     -- Werknemers.Werknemer-id: van wie
    `Soort`        VARCHAR(30) NOT NULL,             -- gelijk aan Meldingen.Soort, bv. 'activiteit'
    `Aan`          TINYINT(1) NOT NULL DEFAULT 1,    -- 1 = wil deze melding, 0 = niet
    `last_updated` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (`WerknemerID`, `Soort`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
