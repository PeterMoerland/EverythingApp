-- ===========================================================================
--  Beheer > Systeem > Backups (NAS) (wens Directie, 25-11-2026): de backups van Hyper Backup op de Synology NAS.
--  De NAS meldt elke uitkomst zelf aan de app (webhook naar /api/nas-melding); mislukt een backup of blijft hij te
--  lang uit, dan krijgt de Directie een melding in de bel.
--
--  1. NasBackupTaken: de taken die we bewaken. De naam moet gelijk zijn aan de naam in Hyper Backup: daaraan
--     herkent de app de berichten. MaxUren = na zoveel uur zonder "gelukt" komt er een melding.
--  2. NasBackupBerichten: wat de NAS stuurde (het logboek op het scherm).
--
--  Eenmalig uitvoeren op de MDE-database. Meerdere keren draaien kan geen kwaad. Zonder dit script staat de pagina
--  er wel, met de melding dat de tabellen ontbreken.
--  Daarnaast nodig: NasBackup:Sleutel in appsettings.json (zie appsettings.voorbeeld.json) en de webhook op de NAS.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `NasBackupTaken` (
    `TaakID`           INT NOT NULL AUTO_INCREMENT,
    `Naam`             VARCHAR(100) NOT NULL,
    `MaxUren`          INT NOT NULL DEFAULT 30,
    `Actief`           TINYINT(1) NOT NULL DEFAULT 1,
    `Aangemaakt`       DATETIME NOT NULL,
    `LaatstGelukt`     DATETIME NULL,
    `LaatsteBericht`   DATETIME NULL,
    `LaatsteUitkomst`  VARCHAR(20) NULL,
    `LaatsteTekst`     VARCHAR(1000) NULL,
    `UitblijvenGemeld` DATETIME NULL,
    PRIMARY KEY (`TaakID`),
    UNIQUE KEY `UX_NasBackupTaken_Naam` (`Naam`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `NasBackupBerichten` (
    `BerichtID` INT NOT NULL AUTO_INCREMENT,
    `Op`        DATETIME NOT NULL,
    `TaakID`    INT NULL,
    `Uitkomst`  VARCHAR(20) NOT NULL,
    `Tekst`     VARCHAR(1000) NOT NULL,
    PRIMARY KEY (`BerichtID`),
    KEY `IX_NasBackupBerichten_Taak` (`TaakID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- De drie taken van nu (namen zoals in Hyper Backup, 25-11-2026). Later bijwerken kan op het scherm.
INSERT IGNORE INTO `NasBackupTaken` (`Naam`, `MaxUren`, `Actief`, `Aangemaakt`) VALUES
    ('BackupNaarLabNas',  30, 1, NOW()),
    ('BackupNaarRackNas', 30, 1, NOW()),
    ('Synology C2',       30, 1, NOW());
