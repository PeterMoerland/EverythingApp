-- ===========================================================================
--  Gesprek bij een verbeterverzoek (wens collega, 6-11-2026): onder elk verzoek kunnen collega's
--  verder praten over een voorstel. Bij een nieuw bericht krijgen de indiener, wie gereageerd heeft
--  en wie al in het gesprek schreef een melding in de bel.
--  Hoort bij Verbeterverzoeken_MariaDB.sql (die tabel moet er al zijn).
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `VerbeterverzoekBerichten` (
    `BerichtID`    INT NOT NULL AUTO_INCREMENT,
    `VerzoekID`    INT NOT NULL,                -- Verbeterverzoeken.VerzoekID
    `Op`           DATETIME NOT NULL,
    `Door`         INT NULL,                    -- Werknemers.Werknemer-id
    `Werkplek`     VARCHAR(100) NULL,           -- naam van de gedeelde werkplek, als het daar geschreven is
    `Tekst`        VARCHAR(2000) NOT NULL,
    PRIMARY KEY (`BerichtID`),
    KEY `IX_VerbeterverzoekBerichten_Verzoek` (`VerzoekID`, `Op`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
