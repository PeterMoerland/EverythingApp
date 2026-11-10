-- ===========================================================================
--  Calculaties (wens gebruiker 10-11-2026): de calculatieboom (voorbeeld "Calculatieboom FIT Clean") in de app,
--  per offerte één calculatie. Projecten ▸ Offertes en calculaties ▸ Calculaties.
--
--  Calculaties.Data = de hele boom als JSON (instellingen, groepen, regels, notities, uitsplitsingen), zoals de
--  boompagina (wwwroot/calculatieboom.html) hem bewaart. Kostprijs/Verkoop/Uren = de totalen bij het laatste opslaan,
--  alleen voor de lijst. Versie gaat bij elke opslag één omhoog (twee mensen tegelijk: de tweede krijgt een melding).
--  Status 0 = concept, 1 = bevroren (klant akkoord; daarna alleen nog notities).
--
--  CalculatieTarieven = de kostprijs per uur per activiteit (UrenActiviteit), standaard voor nieuwe urenregels; een
--  calculatie bewaart zijn eigen kostprijs per regel, dus een nieuw tarief verandert bestaande calculaties niet.
--  Beginwaarden = blad Uurtarief uit FIT Clean Calculatie.xlsm.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `Calculaties` (
    `CalculatieID`   INT NOT NULL AUTO_INCREMENT,
    `OfferteID`      INT NOT NULL,                       -- Offertes.OfferteID (één calculatie per offerte)
    `Status`         TINYINT NOT NULL DEFAULT 0,         -- 0 concept, 1 bevroren (klant akkoord)
    `Versie`         INT NOT NULL DEFAULT 1,
    `Data`           LONGTEXT NOT NULL,                  -- de boom als JSON
    `Kostprijs`      DECIMAL(14,2) NOT NULL DEFAULT 0,
    `Verkoop`        DECIMAL(14,2) NOT NULL DEFAULT 0,
    `Uren`           DECIMAL(12,2) NOT NULL DEFAULT 0,
    `AangemaaktOp`   DATETIME NOT NULL,
    `AangemaaktDoor` INT NULL,                           -- Werknemers
    `GewijzigdOp`    DATETIME NULL,
    `GewijzigdDoor`  INT NULL,
    `BevrorenOp`     DATETIME NULL,
    `BevrorenDoor`   INT NULL,
    PRIMARY KEY (`CalculatieID`),
    UNIQUE KEY `uq_calculatie_offerte` (`OfferteID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `CalculatieTarieven` (
    `UrenActiviteitID` INT NOT NULL,                     -- UrenActiviteit
    `Kostprijs`        DECIMAL(10,2) NOT NULL,           -- per uur
    `GewijzigdOp`      DATETIME NULL,
    `GewijzigdDoor`    INT NULL,
    PRIMARY KEY (`UrenActiviteitID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

INSERT IGNORE INTO `CalculatieTarieven` (`UrenActiviteitID`, `Kostprijs`) VALUES
    (1, 63.33),   -- Mechanical Engineering
    (2, 46.39),   -- Electrical Engineering
    (3, 85.00),   -- Management
    (5, 43.46),   -- Assembly Electrical
    (6, 43.46),   -- Assembly Mechanical
    (7, 43.46),   -- Installation
    (8, 56.09),   -- Software Engineering
    (9, 63.33),   -- Testing
    (10, 65.00),  -- Transport
    (11, 63.33),  -- Documentation
    (12, 63.33),  -- Meeting
    (13, 65.00),  -- Travel
    (14, 63.33);  -- Purchase
