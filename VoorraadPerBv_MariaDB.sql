-- ===========================================================================
--  Voorraad per BV, stap 1 (wens gebruiker 10-11-2026): MDE Automation en M-Pede Robotics krijgen elk hun eigen
--  voorraad. Eén artikelbestand, één lokatieboom; de voorraad hoort bij de BV van de bovenste lokatie (type Bedrijf) waar
--  hij onder ligt. Locations.BvID staat op die bovenste lokatie; alles eronder erft hem.
--  - De bestaande bovenste lokatie "MDE" (1) wordt MDE Automation (5254): alle huidige voorraad blijft daar.
--  - Er komt een tweede bovenste lokatie "M-Pede Robotics" (afkorting MPR) voor de BV met afkorting MPR. Daaronder komen
--    de plekken van MPR (gebouw, stellingen, ...); verplaatsen naar MPR = de voorraad gaat naar die BV.
--  Wees (dragers die nog nergens staan) hoort ook bij MDE Automation; een lokatie zonder BV boven zich telt als
--  "niet ingedeeld".
--
--  Eenmalig uitvoeren op de MDE-database, VOOR het publiceren. Veilig om nog eens te draaien.
-- ===========================================================================

ALTER TABLE `Locations`
    ADD COLUMN IF NOT EXISTS `BvID` INT NULL;      -- Relations.id_supplier van de eigen BV; alleen op de bovenste lokatie (type Bedrijf)

-- MDE (1) en Wees (10, de systeemplek voor dragers die nog nergens staan; ze staan fysiek bij MDE Automation)
UPDATE `Locations` SET `BvID` = 5254 WHERE `LocationID` IN (1, 10) AND `BvID` IS NULL;

INSERT INTO `Locations` (`ParentLocationID`, `LocationTypeID`, `Omschrijving`, `Afkorting`, `Voorraad`, `Systeemlocatie`, `BvID`, `AangemaaktOp`, `GUID`)
SELECT NULL, 17, 'M-Pede Robotics', 'MPR', 1, 0, r.`id_supplier`, NOW(), REPLACE(UUID(), '-', '')
  FROM `Relations` r
 WHERE r.`Afkorting` = 'MPR'
   AND NOT EXISTS (SELECT 1 FROM `Locations` x WHERE x.`BvID` = r.`id_supplier`)
 LIMIT 1;
