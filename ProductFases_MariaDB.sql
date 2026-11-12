-- Opbouwfases per producttype (voorstel Sjoerd Quist, keuzes 11-11-2026; gebouwd 12-11-2026).
--
-- Een producttype krijgt fases (voor de MPR-1: acht). Elke opbouwstap en eindcheck hoort bij een fase. Per exemplaar
-- wordt bijgehouden welke fase klaar of overgeslagen is; de eerste fase die dat niet is, is de actieve fase. Alleen in
-- de actieve fase kunnen stappen worden afgevinkt. Elke historieregel krijgt de fase die op dat moment actief was.
-- Zonder dit script werkt alles zoals voorheen (tabbladen Opbouw en Eindcheck). Mag vaker gedraaid worden.

-- 1. De fases van een producttype
CREATE TABLE IF NOT EXISTS `ProductFases` (
    `FaseID`        INT NOT NULL AUTO_INCREMENT,
    `ProductNameID` INT NOT NULL,                       -- ProductNames.ProductNameID
    `Volgorde`      INT NOT NULL,
    `Naam`          VARCHAR(60) NOT NULL,
    `Omschrijving`  VARCHAR(250) NOT NULL DEFAULT '',
    PRIMARY KEY (`FaseID`),
    KEY `IX_ProductFases_Type` (`ProductNameID`, `Volgorde`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 2. Een stap (opbouw of eindcheck) hoort bij een fase
ALTER TABLE `ProductOpbouwStappen` ADD COLUMN IF NOT EXISTS `FaseID` INT NULL;

-- 3. Per exemplaar: welke fase is klaar of overgeslagen
CREATE TABLE IF NOT EXISTS `ProductFaseStatus` (
    `ProductID` INT NOT NULL,                           -- Producten.ProductenID
    `FaseID`    INT NOT NULL,
    `Stand`     VARCHAR(12) NOT NULL DEFAULT 'klaar',   -- klaar / overgeslagen
    `Op`        DATETIME NOT NULL,
    `Door`      INT NULL,                               -- Werknemers.Werknemer-id; NULL = beginstand uit dit script
    `Opmerking` VARCHAR(250) NOT NULL DEFAULT '',
    PRIMARY KEY (`ProductID`, `FaseID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 4. De fase bij een historieregel
ALTER TABLE `ProductHistorie` ADD COLUMN IF NOT EXISTS `FaseID` INT NULL;

-- 5. De acht fases van de MPR-1 (alleen als het type nog geen fases heeft)
INSERT INTO `ProductFases` (`ProductNameID`, `Volgorde`, `Naam`, `Omschrijving`)
SELECT n.`ProductNameID`, f.`Volgorde`, f.`Naam`, f.`Omschrijving`
  FROM `ProductNames` n
  JOIN (      SELECT 1 AS `Volgorde`, 'Aanmaken' AS `Naam`, 'Serienummer, bouwjaar, werkorder' AS `Omschrijving`
    UNION ALL SELECT 2, 'Mechanisch opbouwen', 'Serienummers motoren, buislengte'
    UNION ALL SELECT 3, 'Elektrisch bekabelen', 'Serienummers printplaten'
    UNION ALL SELECT 4, 'Kalibratie', 'Programmeren en testen, testbank beneden'
    UNION ALL SELECT 5, 'Test gereed maken', 'Afmonteren, op een paar delen na'
    UNION ALL SELECT 6, 'Eindtest / duurtest', 'Node-adressen, half uur boven'
    UNION ALL SELECT 7, 'Afmontage', 'Laatste onderdelen erop'
    UNION ALL SELECT 8, 'Eindcheck', 'Handmatige controle') f
 WHERE n.`ProductName` = 'MPR-1'
   AND NOT EXISTS (SELECT 1 FROM `ProductFases` x WHERE x.`ProductNameID` = n.`ProductNameID`);

-- 6. De bestaande stappen van de MPR-1 over de fases verdelen (afgesproken 12-11-2026); alleen stappen zonder fase.
--    Eindchecks (Soort = 'controle') gaan naar fase 8.
UPDATE `ProductOpbouwStappen` s
  JOIN `ProductNames` n ON n.`ProductNameID` = s.`ProductNameID` AND n.`ProductName` = 'MPR-1'
  JOIN `ProductFases` f ON f.`ProductNameID` = s.`ProductNameID`
   AND f.`Volgorde` = CASE
         WHEN s.`Soort` = 'controle'              THEN 8
         WHEN s.`Naam` = 'Mechanisch opbouwen'    THEN 2
         WHEN s.`Naam` = 'Elektrisch aansluiten'  THEN 3
         WHEN s.`Naam` = 'Firmware en Parameter'  THEN 4
         WHEN s.`Naam` = 'Kalibratie robot'       THEN 4
         WHEN s.`Naam` = 'Testdraaien robot'      THEN 6
         WHEN s.`Naam` = 'Mechanisch afmonteren'  THEN 7
         ELSE 0 END
   SET s.`FaseID` = f.`FaseID`
 WHERE s.`FaseID` IS NULL;

-- 7. Beginstand voor bestaande exemplaren die nog geen enkele fase hebben. Status OK (7) = alle fases klaar. Anders,
--    van boven naar beneden tot de eerste fase die niet af is: een fase met stappen is af als alle verplichte stappen
--    (en alle eindchecks) zijn afgevinkt; een fase zonder stappen is af als er in een latere fase al iets is afgevinkt.
INSERT INTO `ProductFaseStatus` (`ProductID`, `FaseID`, `Stand`, `Op`, `Door`, `Opmerking`)
SELECT p.`ProductenID`, f.`FaseID`, 'klaar', NOW(), NULL, 'Beginstand bij invoering van de fases'
  FROM `Producten` p
  JOIN `ProductFases` f ON f.`ProductNameID` = p.`ProductNameID`
 WHERE NOT EXISTS (SELECT 1 FROM `ProductFaseStatus` x WHERE x.`ProductID` = p.`ProductenID`)
   AND (p.`ProductStatusID` = 7 OR NOT EXISTS (
        SELECT 1 FROM `ProductFases` g
         WHERE g.`ProductNameID` = f.`ProductNameID` AND g.`Volgorde` <= f.`Volgorde`
           AND NOT (
                (EXISTS (SELECT 1 FROM `ProductOpbouwStappen` s WHERE s.`FaseID` = g.`FaseID`)
                 AND NOT EXISTS (SELECT 1 FROM `ProductOpbouwStappen` s
                                   LEFT JOIN `ProductOpbouwStatus` st ON st.`OpbouwStapID` = s.`OpbouwStapID` AND st.`ProductID` = p.`ProductenID`
                                  WHERE s.`FaseID` = g.`FaseID` AND (s.`Verplicht` <> 0 OR s.`Soort` = 'controle')
                                    AND COALESCE(st.`Klaar`, 0) = 0))
             OR (NOT EXISTS (SELECT 1 FROM `ProductOpbouwStappen` s WHERE s.`FaseID` = g.`FaseID`)
                 AND EXISTS (SELECT 1 FROM `ProductOpbouwStappen` s
                               JOIN `ProductFases` h ON h.`FaseID` = s.`FaseID`
                               JOIN `ProductOpbouwStatus` st ON st.`OpbouwStapID` = s.`OpbouwStapID` AND st.`ProductID` = p.`ProductenID` AND st.`Klaar` <> 0
                              WHERE h.`ProductNameID` = g.`ProductNameID` AND h.`Volgorde` > g.`Volgorde`))
           )));
