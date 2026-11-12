-- ===========================================================================
--  De productstatus TEST ("Wordt getest") vervalt (keuze 11-11-2026 bij het voorstel van Sjoerd Quist):
--  de fases van de opbouw laten zien waar een exemplaar is.
--
--  LET OP: dit script wijzigt gegevens. Op 12-11-2026 stonden er 18 exemplaren op TEST
--  (14 x MPR-1, 4 x MPR-CAB-CA2). Die gaan naar PROD ("In productie"), met een regel in hun historie.
--  De status zelf blijft in de tabel staan (oude verwijzingen blijven kloppen) maar krijgt Vervallen = 1 en
--  staat daarna niet meer in de keuzelijsten van de app.
--
--  Los van ProductOpbouwDeel3_MariaDB.sql; mag ook later. Veilig om nog eens te draaien.
-- ===========================================================================

-- Vooraf bekijken wat er verandert:
--   SELECT n.`ProductName`, p.`SerieNummer` FROM `Producten` p
--     JOIN `ProductStatus` s ON s.`ProductStatusID` = p.`ProductStatusID`
--     LEFT JOIN `ProductNames` n ON n.`ProductNameID` = p.`ProductNameID`
--    WHERE s.`StatusShort` = 'TEST' ORDER BY n.`ProductName`, p.`SerieNummer`;

ALTER TABLE `ProductStatus` ADD COLUMN IF NOT EXISTS `Vervallen` TINYINT(1) NOT NULL DEFAULT 0;

-- 1. In de historie van elk exemplaar dat nu op TEST staat (HistorieTypeID 1 = levensloop)
INSERT INTO `ProductHistorie` (`ProductID`, `HistorieTypeID`, `Event`, `ToegevoegdOp`)
SELECT p.`ProductenID`, 1, 'Status TEST vervallen: status → In productie (de fases van de opbouw laten zien waar het product is)', NOW()
  FROM `Producten` p
  JOIN `ProductStatus` s ON s.`ProductStatusID` = p.`ProductStatusID`
 WHERE s.`StatusShort` = 'TEST';

-- 2. Die exemplaren naar PROD
UPDATE `Producten` p
  JOIN `ProductStatus` t ON t.`ProductStatusID` = p.`ProductStatusID` AND t.`StatusShort` = 'TEST'
  JOIN `ProductStatus` d ON d.`StatusShort` = 'PROD'
   SET p.`ProductStatusID` = d.`ProductStatusID`;

-- 3. De status zelf vervalt
UPDATE `ProductStatus` SET `Vervallen` = 1 WHERE `StatusShort` = 'TEST';
