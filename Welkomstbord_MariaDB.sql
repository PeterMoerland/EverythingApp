-- ===========================================================================
--  Welkomstbord: het welkomstscherm op de tv in de hal.
--  Relaties ▸ Welkomstscherm: per bezoek een datum (van/tot), eventueel tijden, de relatie, de gastheer en een tekst.
--  De tv toont /hal/<sleutel> (zonder inlog; sleutel in appsettings, Welkomstscherm:Sleutel) en laat zien wat op dat
--  moment geldt; anders een algemeen welkom.
--  Instellen mag Directie of Budgetverantwoordelijke.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `Welkomstbord` (
    `WelkomID`        INT NOT NULL AUTO_INCREMENT,
    `DatumVan`        DATE NOT NULL,
    `DatumTot`        DATE NOT NULL,               -- gelijk aan DatumVan bij een bezoek van één dag
    `TijdVan`         TIME NULL,                   -- leeg = vanaf het begin van de dag
    `TijdTot`         TIME NULL,                   -- leeg = tot het eind van de dag
    `RelatieID`       INT NULL,                    -- Relations.id_supplier
    `Tekst`           VARCHAR(1000) NULL,          -- bv. "Welkom Jan en Piet van Koninklijke Mosa"
    `GastheerID`      INT NULL,                    -- Werknemers.Werknemer-id
    `AangemaaktOp`    DATETIME NOT NULL,
    `AangemaaktDoor`  INT NULL,
    `GewijzigdOp`     DATETIME NULL,
    `GewijzigdDoor`   INT NULL,
    PRIMARY KEY (`WelkomID`),
    KEY `IX_Welkomstbord_Datum` (`DatumVan`, `DatumTot`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
