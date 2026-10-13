-- Halffabricaten onderverdelen (Motor, PCBA, Kabel, …) en koppelen aan een artikel.
--
-- De tabel ProductNames wordt ook door Access gebruikt; daarom geen kolommen
-- daaraan toevoegen maar een zijtabel per productnaam:
--   ProductGroepen     de indeling (uit te breiden in Beheer)
--   ProductNameExtra   per ProductNameID: groep en artikel (Artikelen.ArtikelID —
--                      daar staat bv. hoe een kabel is opgebouwd)

CREATE TABLE IF NOT EXISTS `ProductGroepen` (
    `GroepID`  INT         NOT NULL AUTO_INCREMENT,
    `Naam`     VARCHAR(40) NOT NULL,
    `Volgorde` INT         NOT NULL DEFAULT 0,
    PRIMARY KEY (`GroepID`),
    UNIQUE KEY `uq_groep_naam` (`Naam`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

INSERT IGNORE INTO `ProductGroepen` (`Naam`, `Volgorde`) VALUES ('PCBA', 1), ('Motor', 2), ('Kabel', 3);

CREATE TABLE IF NOT EXISTS `ProductNameExtra` (
    `ProductNameID` INT NOT NULL,
    `GroepID`       INT NULL,
    `ArtikelID`     INT NULL COMMENT 'Artikelen.ArtikelID: het artikel (met samenstelling) dat dit halffabricaat is',
    PRIMARY KEY (`ProductNameID`),
    KEY `ix_extra_groep` (`GroepID`),
    KEY `ix_extra_artikel` (`ArtikelID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Welke stappen het scherm Productie ▸ Half-fabricaten toont voor dit halffabricaat
-- (Producten ▸ producttype ▸ Techniek ▸ 'Stappen in Half-fabricaten'). Een kabel hoeft
-- niet geprogrammeerd of automatisch getest te worden. 1 = tonen (standaard), 0 = weglaten.
ALTER TABLE `ProductNameExtra`
    ADD COLUMN IF NOT EXISTS `ToonUitlezen`     TINYINT(1) NOT NULL DEFAULT 1 COMMENT 'knop Automatisch uitlezen',
    ADD COLUMN IF NOT EXISTS `ToonProgrammeren` TINYINT(1) NOT NULL DEFAULT 1 COMMENT 'stap Programmeren',
    ADD COLUMN IF NOT EXISTS `ToonTesten`       TINYINT(1) NOT NULL DEFAULT 1 COMMENT 'stap Testen',
    ADD COLUMN IF NOT EXISTS `ToonOpbouw`       TINYINT(1) NOT NULL DEFAULT 1 COMMENT 'stap Opbouw en eindcontrole',
    ADD COLUMN IF NOT EXISTS `ToonDuurtest`     TINYINT(1) NOT NULL DEFAULT 1 COMMENT 'stap Duurtest (Producten: log van de testrig ophalen)',
    ADD COLUMN IF NOT EXISTS `ToonNodeAdres`    TINYINT(1) NOT NULL DEFAULT 0 COMMENT 'stap EtherCAT node-adressen (station alias lezen/schrijven)';

-- Voorbeeld: de RZ-print is een PCBA.
INSERT INTO `ProductNameExtra` (`ProductNameID`, `GroepID`)
SELECT n.`ProductNameID`, g.`GroepID` FROM `ProductNames` n, `ProductGroepen` g
 WHERE n.`ProductName` LIKE 'MPR-PCBA-%' AND g.`Naam` = 'PCBA'
ON DUPLICATE KEY UPDATE `GroepID` = VALUES(`GroepID`);
