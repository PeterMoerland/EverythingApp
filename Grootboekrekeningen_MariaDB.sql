-- ===========================================================================
--  Grootboekrekeningen (wens gebruiker 11-11-2026): de rekeningen per administratie (BV), te onderhouden in Financiën ▸
--  Grootboekrekeningen en in te lezen uit Excel (kolommen NaamAdministratie, CodeGrootboekrekening, NaamGrootboekrekening,
--  CodeRelatiekostenplaats). Elk afschriftrecord (banktransacties) kan een grootboekrekening krijgen.
--
--  Code is tekst: de voorloopnul hoort erbij (01100). Administratie = de naam zoals in de export; BvID = de eigen BV met
--  die naam (Relations), leeg als de naam niet herkend is. Een rekening wordt niet gewist maar op niet-actief gezet:
--  er kunnen afschriften aan hangen.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `Grootboekrekeningen` (
    `GrootboekID`             INT NOT NULL AUTO_INCREMENT,
    `BvID`                    INT NULL,                   -- Relations.id_supplier van de eigen BV
    `Administratie`           VARCHAR(100) NOT NULL,      -- NaamAdministratie uit de export
    `Code`                    VARCHAR(20)  NOT NULL,      -- CodeGrootboekrekening, als tekst
    `Naam`                    VARCHAR(150) NOT NULL,      -- NaamGrootboekrekening
    `CodeRelatieKostenplaats` VARCHAR(50)  NULL,
    `Actief`                  TINYINT(1)   NOT NULL DEFAULT 1,
    `GewijzigdOp`             DATETIME     NULL,
    `GewijzigdDoor`           INT          NULL,
    PRIMARY KEY (`GrootboekID`),
    UNIQUE KEY `uq_grootboek` (`Administratie`, `Code`),
    KEY `IX_Grootboek_Bv` (`BvID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- de grootboekrekening van een afschriftrecord
ALTER TABLE `banktransacties` ADD COLUMN IF NOT EXISTS `GrootboekID` INT NULL;
CREATE INDEX IF NOT EXISTS `IX_banktransacties_Grootboek` ON `banktransacties` (`GrootboekID`);
