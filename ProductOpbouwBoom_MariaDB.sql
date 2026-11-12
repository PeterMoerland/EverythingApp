-- Opbouwboom per producttype (wens gebruiker 12-11-2026): fases, subfases en stappen in één boom.
--
-- Alles staat in ProductOpbouwStappen; een item kan onder een ander item hangen (ParentStapID), zo diep als nodig.
-- Een item met iets eronder is een fase of subfase; een item zonder iets eronder is een stap die je afvinkt.
-- OpVolgorde bij een (sub)fase: wat eronder hangt moet van boven naar beneden. De hoofdfases (bovenste niveau van een
-- type met fases) gaan altijd op volgorde en worden met de knop "Fase klaar" afgerond.
--
-- Dit script zet de fases uit ProductFases_MariaDB.sql (eerst draaien als dat nog niet gebeurd is) om naar items in de
-- boom, hangt de bestaande stappen eronder en neemt de stand per exemplaar over. De tabellen ProductFases en
-- ProductFaseStatus en de kolommen FaseID blijven staan maar zijn daarna NIET MEER IN GEBRUIK.
-- Mag vaker gedraaid worden.

-- 1. De boom
ALTER TABLE `ProductOpbouwStappen`
    ADD COLUMN IF NOT EXISTS `ParentStapID` INT NULL,                       -- het item waar dit onder hangt; NULL = bovenste niveau
    ADD COLUMN IF NOT EXISTS `OpVolgorde`   TINYINT(1) NOT NULL DEFAULT 0,   -- (sub)fase: wat eronder hangt moet op volgorde
    ADD COLUMN IF NOT EXISTS `OudFaseID`    INT NULL;                        -- herkomst: ProductFases.FaseID (alleen voor dit script)

-- 2. Een fase kan ook overgeslagen zijn
ALTER TABLE `ProductOpbouwStatus` ADD COLUMN IF NOT EXISTS `Overgeslagen` TINYINT(1) NOT NULL DEFAULT 0;

-- 3. De hoofdfase bij een historieregel (verwijst naar een item op het bovenste niveau)
ALTER TABLE `ProductHistorie` ADD COLUMN IF NOT EXISTS `FaseStapID` INT NULL;

-- 4. Eindchecks waren altijd verplicht, ook zonder vinkje Verplicht; in de boom telt alleen dat vinkje nog
UPDATE `ProductOpbouwStappen` SET `Verplicht` = 1 WHERE `Soort` = 'controle' AND `Verplicht` = 0;

-- 5. De fases worden items op het bovenste niveau
INSERT INTO `ProductOpbouwStappen` (`ProductNameID`, `Volgorde`, `Soort`, `Naam`, `Omschrijving`, `Verplicht`, `AddedAt`, `OudFaseID`)
SELECT f.`ProductNameID`, f.`Volgorde`, 'opbouw', f.`Naam`, f.`Omschrijving`, 1, NOW(), f.`FaseID`
  FROM `ProductFases` f
 WHERE NOT EXISTS (SELECT 1 FROM `ProductOpbouwStappen` x WHERE x.`OudFaseID` = f.`FaseID`);

-- 6. De stappen hangen onder hun fase
UPDATE `ProductOpbouwStappen` s
  JOIN `ProductOpbouwStappen` g ON g.`OudFaseID` = s.`FaseID`
   SET s.`ParentStapID` = g.`OpbouwStapID`
 WHERE s.`FaseID` IS NOT NULL AND s.`ParentStapID` IS NULL AND s.`OudFaseID` IS NULL;

-- 7. De stand van de fases per exemplaar (klaar of overgeslagen) overnemen
INSERT INTO `ProductOpbouwStatus` (`ProductID`, `OpbouwStapID`, `Klaar`, `KlaarOp`, `KlaarDoor`, `Opmerking`, `Overgeslagen`)
SELECT x.`ProductID`, g.`OpbouwStapID`, 1, x.`Op`, x.`Door`, x.`Opmerking`, x.`Stand` = 'overgeslagen'
  FROM `ProductFaseStatus` x
  JOIN `ProductOpbouwStappen` g ON g.`OudFaseID` = x.`FaseID`
    ON DUPLICATE KEY UPDATE `OpbouwStapID` = VALUES(`OpbouwStapID`);     -- bestaat al: niets wijzigen

-- 8. De fase van historieregels overnemen
UPDATE `ProductHistorie` h
  JOIN `ProductOpbouwStappen` g ON g.`OudFaseID` = h.`FaseID`
   SET h.`FaseStapID` = g.`OpbouwStapID`
 WHERE h.`FaseID` IS NOT NULL AND h.`FaseStapID` IS NULL;
