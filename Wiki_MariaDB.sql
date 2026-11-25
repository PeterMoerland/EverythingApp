-- ===========================================================================
--  Wiki (opzet van Freek, fase 1; 25-11-2026): ruimtes met artikelen in markdown, met versies.
--
--  1. WikiRuimtes: de ruimtes in de zijbalk (naam, icoon, volgorde). OuderID = de ruimte waar deze onder hangt
--     (leeg = bovenaan): zo vormen de ruimtes een boom (de app laat hoogstens acht lagen toe). Toevoegen en hernoemen kan op het scherm.
--  2. WikiArtikelen: de artikelen met hun geldende tekst. Actief = 0 betekent inactief: uit de lijsten en het zoeken,
--     maar niet weg. ProjectID = het project waar het artikel bij hoort (mag leeg).
--  3. WikiVersies: elke opgeslagen versie (titel, tekst, wie, wanneer, wat er veranderd is).
--  4. Bijlagen en plaatjes staan in de filestore (soort 346, MasterID = ArtikelID). FileStore.StoreType verwijst naar
--     FileStoreType: zonder die regel weigert de database het bestand.
--
--  Uitvoeren op de MDE-database. Meerdere keren draaien kan geen kwaad. Zonder dit script staat de pagina
--  er wel, met de melding dat de tabellen ontbreken.
--  LET OP: is het script al eerder gedraaid (25-11-2026, ochtend), draai het dan nog een keer: dan komt de kolom
--  OuderID erbij voor de subruimtes. Tot dan werkt de wiki gewoon, met een platte lijst van ruimtes.
-- ===========================================================================

INSERT IGNORE INTO `FileStoreType` (`FilestoreTypeID`, `FileStoreTypeDescription`) VALUES (346, 'Wiki bijlage');

CREATE TABLE IF NOT EXISTS `WikiRuimtes` (
    `RuimteID`     INT NOT NULL AUTO_INCREMENT,
    `Naam`         VARCHAR(100) NOT NULL,
    `Icoon`        VARCHAR(16) NULL,
    `Omschrijving` VARCHAR(300) NULL,
    `Volgorde`     INT NOT NULL DEFAULT 0,
    `Actief`       TINYINT(1) NOT NULL DEFAULT 1,
    `OuderID`      INT NULL,
    PRIMARY KEY (`RuimteID`),
    KEY `IX_WikiRuimtes_Ouder` (`OuderID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Was de tabel er al: de kolom voor de boom erbij. De naam hoeft niet meer in de hele wiki uniek te zijn (een
-- subruimte "Algemeen" mag onder meer dan een ruimte hangen); de app bewaakt dat hij per ouder een keer voorkomt.
ALTER TABLE `WikiRuimtes`
    ADD COLUMN IF NOT EXISTS `OuderID` INT NULL,
    ADD INDEX IF NOT EXISTS `IX_WikiRuimtes_Ouder` (`OuderID`),
    DROP INDEX IF EXISTS `UX_WikiRuimtes_Naam`;

CREATE TABLE IF NOT EXISTS `WikiArtikelen` (
    `ArtikelID`      INT NOT NULL AUTO_INCREMENT,
    `RuimteID`       INT NOT NULL,
    `Titel`          VARCHAR(200) NOT NULL,
    `Sectie`         VARCHAR(100) NULL,
    `Tags`           VARCHAR(500) NULL,
    `Inhoud`         MEDIUMTEXT NULL,
    `Versie`         INT NOT NULL DEFAULT 1,
    `Actief`         TINYINT(1) NOT NULL DEFAULT 1,
    `ProjectID`      INT NULL,
    `AangemaaktDoor` INT NULL,
    `AangemaaktOp`   DATETIME NOT NULL,
    `GewijzigdDoor`  INT NULL,
    `GewijzigdOp`    DATETIME NOT NULL,
    PRIMARY KEY (`ArtikelID`),
    KEY `IX_WikiArtikelen_Ruimte` (`RuimteID`),
    KEY `IX_WikiArtikelen_Project` (`ProjectID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `WikiVersies` (
    `VersieID`  INT NOT NULL AUTO_INCREMENT,
    `ArtikelID` INT NOT NULL,
    `Versie`    INT NOT NULL,
    `Titel`     VARCHAR(200) NOT NULL,
    `Inhoud`    MEDIUMTEXT NULL,
    `Opmerking` VARCHAR(500) NULL,
    `Door`      INT NULL,
    `Op`        DATETIME NOT NULL,
    PRIMARY KEY (`VersieID`),
    UNIQUE KEY `UX_WikiVersies` (`ArtikelID`, `Versie`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- De ruimtes uit de opzet van Freek; alleen als de tabel nog leeg is (anders komen ze bij opnieuw draaien dubbel).
INSERT INTO `WikiRuimtes` (`Naam`, `Icoon`, `Omschrijving`, `Volgorde`)
SELECT x.* FROM (
              SELECT 'MDE Automation' AS n, '🏭' AS i, 'Wie zijn we, hoe werken we, klanten en markten' AS o, 10 AS v
    UNION ALL SELECT 'Projecten',      '📄', 'Werkwijze, lessons learned en kennis per project', 20
    UNION ALL SELECT 'Mechanica',      '⚙️', 'Ontwerpen, tekenen, uitbesteden', 30
    UNION ALL SELECT 'Software',       '💻', 'PLC, HMI, vision, robots', 40
    UNION ALL SELECT 'Elektro',        '⚡', 'Schema''s, kasten, bekabeling', 50
    UNION ALL SELECT 'Werkplaats',     '🔧', 'Opbouw, assemblage, testen', 60
    UNION ALL SELECT 'Financieel',     '💵', 'Inkoop, facturen, administratie', 70
    UNION ALL SELECT 'Management',     '👔', 'Beleid en afspraken', 80
) x WHERE NOT EXISTS (SELECT 1 FROM `WikiRuimtes`);
