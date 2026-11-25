-- ===========================================================================
--  Tools > Licenties > M-Pede Robotics: de regels die uit licenses_issued.csv zijn overgenomen hebben als robot
--  alleen een nummer (5, 4, 3, 2, 1, 52). Dit zet ze in dezelfde vorm als nieuwe regels: "MPR-1 · 5"
--  (wens gebruiker 25-11-2026).
--
--  Eenmalig uitvoeren op de MDE-database. Nog een keer draaien doet niets meer: alleen regels waar de robot
--  enkel uit cijfers bestaat worden aangepast.
-- ===========================================================================

-- 1. De naam: "5" wordt "MPR-1 · 5". Het puntje ertussen staat als code in de opdracht, zodat het ook goed gaat als
--    het programma waarmee je dit uitvoert anders met tekens omgaat.
UPDATE `LicentiesMPede`
   SET `Robot` = CONCAT('MPR-1 ', CONVERT(0xC2B7 USING utf8mb4), ' ', `Robot`)
 WHERE `Robot` REGEXP '^[0-9]+$';

-- 2. (Mag je overslaan.) De regel koppelen aan de robot in Producten, zodat de robot op het scherm een link naar
--    het exemplaar wordt. Alleen voor regels zonder koppeling waarvan de naam precies bij een MPR-1 hoort.
UPDATE `LicentiesMPede` l
  JOIN `ProductNames` n ON n.`ProductName` = 'MPR-1'
  JOIN `Producten` p ON p.`ProductNameID` = n.`ProductNameID`
                    AND COALESCE(p.`isDeleted`, 0) = 0
                    AND l.`Robot` = CONCAT('MPR-1 ', CONVERT(0xC2B7 USING utf8mb4), ' ', TRIM(p.`SerieNummer`))
   SET l.`ProductID` = p.`ProductenID`
 WHERE l.`ProductID` IS NULL;

-- Nakijken:
-- SELECT `LicentieID`, `Datum`, `Robot`, `ProductID`, `Vervallen` FROM `LicentiesMPede` ORDER BY `LicentieID`;
