-- ===========================================================================
--  Tools > Claude Shares (wens van Arnold, 24-11-2026): .md- en .txt-bestanden bij een project, met versies.
--  Bedoeld om de kennis uit een Claude-gesprek (PLC, HMI, Aurora) over te dragen aan een collega.
--
--  1. Recht Engineering: wie het heeft (en Directie) ziet Tools > Claude Shares en mag documenten toevoegen.
--     Zetten in Werknemers > Rechten > Engineering (Claude Shares).
--  2. ClaudeShares: een document bij een project (naam, vervallen of niet).
--  3. ClaudeShareVersies: de versies; de hoogste versie is de geldende, de rest is het archief. Het bestand zelf staat
--     in de filestore (soort 345, MasterID = ShareID); FileStoreID verwijst ernaar.
--     FileStore.StoreType verwijst naar FileStoreType (FK_FileStore_StoreType): zonder die regel weigert de database
--     het bestand.
--
--  Uitvoeren op de MDE-database. Meerdere keren draaien kan geen kwaad. LET OP: de eerste versie van dit script
--  (24-11-2026, ochtend) bewaarde de tekst in de kolom Inhoud; draai het daarom nog een keer, dan komen FileStoreID
--  en Grootte erbij en gaat de (nooit gebruikte) kolom Inhoud weg. Zonder dit script staat de pagina er wel, met de
--  melding dat de tabellen ontbreken.
-- ===========================================================================

ALTER TABLE `Werknemers`
    ADD COLUMN IF NOT EXISTS `Engineering` TINYINT(1) NOT NULL DEFAULT 0;

INSERT IGNORE INTO `FileStoreType` (`FilestoreTypeID`, `FileStoreTypeDescription`) VALUES (345, 'Claude Shares document');

CREATE TABLE IF NOT EXISTS `ClaudeShares` (
    `ShareID`        INT NOT NULL AUTO_INCREMENT,
    `ProjectID`      INT NOT NULL,
    `Naam`           VARCHAR(150) NOT NULL,
    `Vervallen`      TINYINT(1) NOT NULL DEFAULT 0,
    `AangemaaktDoor` INT NULL,
    `AangemaaktOp`   DATETIME NOT NULL,
    PRIMARY KEY (`ShareID`),
    KEY `IX_ClaudeShares_Project` (`ProjectID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `ClaudeShareVersies` (
    `VersieID`     INT NOT NULL AUTO_INCREMENT,
    `ShareID`      INT NOT NULL,
    `Versie`       INT NOT NULL,
    `Bestandsnaam` VARCHAR(255) NOT NULL,
    `FileStoreID`  INT NOT NULL DEFAULT 0,
    `Grootte`      INT NOT NULL DEFAULT 0,       -- aantal tekens
    `Opmerking`    VARCHAR(500) NULL,
    `Door`         INT NULL,
    `Op`           DATETIME NOT NULL,
    PRIMARY KEY (`VersieID`),
    UNIQUE KEY `UX_ClaudeShareVersies` (`ShareID`, `Versie`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Was de tabel er al van de eerste versie van dit script: de kolommen bijwerken (de tabel was toen nog leeg).
ALTER TABLE `ClaudeShareVersies`
    ADD COLUMN IF NOT EXISTS `FileStoreID` INT NOT NULL DEFAULT 0 AFTER `Bestandsnaam`,
    ADD COLUMN IF NOT EXISTS `Grootte`     INT NOT NULL DEFAULT 0 AFTER `FileStoreID`,
    DROP COLUMN IF EXISTS `Inhoud`;

-- Wie het recht krijgt (vul de juiste werknemer-id's in):
-- UPDATE `Werknemers` SET `Engineering` = 1 WHERE `Werknemer-id` IN (0);
-- Wie het nu heeft:
-- SELECT `Werknemer-id`, `NaamVoluit` FROM `Werknemers` WHERE `Engineering` <> 0;
