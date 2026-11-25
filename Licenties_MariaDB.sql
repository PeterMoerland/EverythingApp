-- ===========================================================================
--  Tools > Licenties (wens gebruiker 25-11-2026): licentiecodes maken, als eerste voor M-Pede Robotics
--  (Robot.Config.LicenseKey van de MPR-bibliotheek; overgenomen uit MPR_DebugTool.pyw van Gerard).
--
--  1. Recht Licenties: wie het heeft (en Directie) ziet Tools > Licenties en mag codes maken.
--     Zetten in Werknemers > Rechten > Licenties.
--  2. LicentiesMPede: de uitgegeven codes (was het losse bestand licenses_issued.csv). Wissen op het scherm zet
--     Vervallen op 1; de regel blijft staan, zodat na te gaan blijft welke codes er zijn uitgegeven.
--     ProductID = de robot in Producten waar de code voor is (leeg bij een met de hand ingevulde regel).
--
--  De bestaande regels uit licenses_issued.csv neem je over op het scherm zelf (knop "Overnemen uit
--  licenses_issued.csv"); ze staan bewust niet in dit script, want dit script staat in Git.
--  Daarnaast nodig: Licenties:MPedeSleutel in appsettings.json (de 48 hex-tekens uit mpr_license.key).
--
--  Uitvoeren op de MDE-database. Meerdere keren draaien kan geen kwaad. Zonder dit script staat de pagina
--  er wel, met de melding dat de tabel ontbreekt.
--  LET OP: is het script al eerder gedraaid (25-11-2026, middag), draai het dan nog een keer: dan komt de kolom
--  ProductID erbij. Tot dan werkt alles, alleen wordt de robot niet aan de regel gekoppeld.
-- ===========================================================================

ALTER TABLE `Werknemers`
    ADD COLUMN IF NOT EXISTS `Licenties` TINYINT(1) NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS `LicentiesMPede` (
    `LicentieID`  INT NOT NULL AUTO_INCREMENT,
    `Datum`       DATETIME NOT NULL,
    `Serienummer` BIGINT NOT NULL,              -- serienummer van de J3-drive (1 t/m 4294967295)
    `Code`        VARCHAR(12) NOT NULL,         -- 16#XXXXXXXX
    `Robot`       VARCHAR(40) NULL,
    `Notitie`     VARCHAR(120) NULL,
    `Door`        INT NULL,
    `Vervallen`   TINYINT(1) NOT NULL DEFAULT 0,
    `ProductID`   INT NULL,                     -- Producten.ProductenID van de robot
    PRIMARY KEY (`LicentieID`),
    KEY `IX_LicentiesMPede_Serienummer` (`Serienummer`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Was de tabel er al: de kolom voor de robot erbij.
ALTER TABLE `LicentiesMPede`
    ADD COLUMN IF NOT EXISTS `ProductID` INT NULL;

-- Wie het recht krijgt (vul de juiste werknemer-id's in):
-- UPDATE `Werknemers` SET `Licenties` = 1 WHERE `Werknemer-id` IN (0);
-- Wie het nu heeft:
-- SELECT `Werknemer-id`, `NaamVoluit` FROM `Werknemers` WHERE `Licenties` <> 0;
