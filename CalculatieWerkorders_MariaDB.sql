-- ===========================================================================
--  Calculaties, stap 2: werkorders aanmaken bij klant akkoord (wens gebruiker 10-11-2026).
--
--  CalculatieWerkorders = welke werkorder bij welke groep van een calculatie hoort (GroepID = id in de JSON-boom), met
--  het budget en de begrote kosten en uren zoals ze bij het aanmaken waren. Zo kan een calculatie maar één keer
--  werkorders maken, en kan de nacalculatie later begroot tegen gemaakt zetten.
--
--  WerkorderDelen = de delen van een werkorder (elke urenregel uit de calculatie) met begrote uren en kosten, als
--  voorbereiding op "boeken op delen" in de urenregistratie (volgende stap; nu nog nergens zichtbaar).
--
--  Eenmalig uitvoeren op de MDE-database, na Calculaties_MariaDB.sql. Veilig om nog eens te draaien.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `CalculatieWerkorders` (
    `ID`              INT NOT NULL AUTO_INCREMENT,
    `CalculatieID`    INT NOT NULL,                      -- Calculaties
    `GroepID`         INT NOT NULL,                      -- id van de groep in Calculaties.Data
    `WerkbonID`       INT NOT NULL,                      -- Workorders.Werkbon-id
    `Budget`          DECIMAL(14,2) NOT NULL DEFAULT 0,  -- verkoop incl. aandeel korting (= Workorders.Budget)
    `KostenUren`      DECIMAL(14,2) NOT NULL DEFAULT 0,
    `KostenMateriaal` DECIMAL(14,2) NOT NULL DEFAULT 0,
    `Uren`            DECIMAL(12,2) NOT NULL DEFAULT 0,
    `AangemaaktOp`    DATETIME NOT NULL,
    `AangemaaktDoor`  INT NULL,
    PRIMARY KEY (`ID`),
    KEY `IX_CalculatieWerkorders_Calculatie` (`CalculatieID`),
    UNIQUE KEY `uq_calculatiewerkorder_werkbon` (`WerkbonID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `WerkorderDelen` (
    `DeelID`           INT NOT NULL AUTO_INCREMENT,
    `WerkbonID`        INT NOT NULL,                     -- Workorders.Werkbon-id
    `Naam`             VARCHAR(200) NOT NULL,            -- hierop wordt straks geboekt
    `BegroteUren`      DECIMAL(12,2) NOT NULL DEFAULT 0,
    `BegroteKosten`    DECIMAL(14,2) NOT NULL DEFAULT 0,
    `UrenActiviteitID` INT NULL,                         -- het tarief in de calculatie
    `Volgorde`         INT NOT NULL DEFAULT 0,
    `CalculatieID`     INT NULL,                         -- waar het deel vandaan komt (leeg = met de hand)
    `Actief`           TINYINT(1) NOT NULL DEFAULT 1,
    `AangemaaktOp`     DATETIME NOT NULL,
    `AangemaaktDoor`   INT NULL,
    PRIMARY KEY (`DeelID`),
    UNIQUE KEY `uq_werkorderdeel_naam` (`WerkbonID`, `Naam`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
