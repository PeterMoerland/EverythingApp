-- ===========================================================================
--  BV-structuur (wens gebruiker 9-11-2026): de app geschikt maken voor meerdere BV's (M-Pede Robotics naast
--  MDE Automation, met uren en materialen over en weer gefactureerd). Eerst de structuur vastleggen:
--  - Relations.Opgeheven: een eigen BV die vervallen is (bv. MDE Robotics, MDE Products). Oude facturen en projecten
--    houden hun BV; in de keuzelijsten staat hij met "(opgeheven)" achteraan.
--  - BvAandeelhouders: wie welk deel van een BV bezit (een BV kan meerdere aandeelhouders hebben, bv. MDE Holding:
--    MDE Vastgoed 90 %, F.M. Moerland Holding 10 %). Vanaf/Tot = historie; een wijziging = de oude regel een Tot geven
--    en een nieuwe regel toevoegen.
--  De vinkjes Werkmaatschappij (eigen BV, factureert) en MDEBV (hoort bij de MDE-groep, telt mee in Liquiditeit)
--  blijven zoals ze zijn.
--
--  Eenmalig uitvoeren op de MDE-database, VOOR het publiceren. Veilig om nog eens te draaien.
-- ===========================================================================

ALTER TABLE `Relations`
    ADD COLUMN IF NOT EXISTS `Opgeheven` DATE NULL;

CREATE TABLE IF NOT EXISTS `BvAandeelhouders` (
    `AandeelID`       INT NOT NULL AUTO_INCREMENT,
    `BvID`            INT NOT NULL,                -- Relations.id_supplier: de BV waarvan aandelen gehouden worden
    `AandeelhouderID` INT NOT NULL,                -- Relations.id_supplier: wie de aandelen houdt
    `Percentage`      DECIMAL(6,3) NOT NULL,       -- 0 < p <= 100
    `Vanaf`           DATE NULL,
    `Tot`             DATE NULL,                   -- leeg = nog steeds
    `Opmerking`       VARCHAR(300) NULL,
    `GewijzigdOp`     DATETIME NOT NULL,
    `GewijzigdDoor`   INT NULL,
    PRIMARY KEY (`AandeelID`),
    KEY `IX_BvAandeelhouders_Bv` (`BvID`),
    KEY `IX_BvAandeelhouders_Houder` (`AandeelhouderID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- De vervallen BV's (kan ook in de app: Relaties > tab BV > Opgeheven per):
-- UPDATE `Relations` SET `Opgeheven` = CURDATE() WHERE `id_supplier` IN (5645, 5646);   -- MDE Robotics, MDE Products

-- Documenten van een eigen BV (Financiën > BV-structuur), alleen voor Financial en Directie (toegevoegd 9-11-2026;
-- dit script mag daarvoor nog een keer gedraaid worden).
INSERT IGNORE INTO `FileStoreType` (`FilestoreTypeID`, `FileStoreTypeDescription`) VALUES (360, 'BV-document');
