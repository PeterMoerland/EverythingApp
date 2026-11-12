-- ===========================================================================
--  Opbouw deel 5 (voorstel Sjoerd Quist, 12-11-2026): invullen in de fase, tabblad Info alleen-lezen,
--  corrigeren met een recht.
--
--  1. ProductNameAttributes.FaseStapID en ProductSamenstelling.FaseStapID: bij het producttype kies je per attribuut
--     en per regel van de stuklijst in welke hoofdfase van de opbouw het wordt ingevuld (Producten ▸ producttype ▸
--     tab Attributen / Samenstelling, kolom Fase). Bij een exemplaar vul je het dan in die fase in (tab Opbouw); op
--     het tabblad Info is het alleen te lezen.
--     Zolang er geen fase is gekozen verandert er niets: zonder fase blijft alles invulbaar zoals voorheen.
--  2. Werknemers.Productcorrectie: wie dit recht heeft (of Directie) mag op het tabblad Info corrigeren buiten de
--     fase om; elke correctie komt in de historie. Zetten in Werknemers ▸ Rechten ▸ Productcorrectie.
--
--  Eerst ProductOpbouwBoom_MariaDB.sql en ProductSamenstelling_MariaDB.sql (zijn al gedraaid). Veilig om nog eens te draaien.
-- ===========================================================================

ALTER TABLE `ProductNameAttributes` ADD COLUMN IF NOT EXISTS `FaseStapID` INT NULL;   -- hoofdfase van de opbouwboom; NULL = vrij in te vullen
ALTER TABLE `ProductSamenstelling`  ADD COLUMN IF NOT EXISTS `FaseStapID` INT NULL;   -- hoofdfase van de opbouwboom; NULL = vrij te koppelen

ALTER TABLE `Werknemers` ADD COLUMN IF NOT EXISTS `Productcorrectie` TINYINT(1) NOT NULL DEFAULT 0;

-- Wie het recht krijgt (vul de juiste werknemer-id's in):
-- UPDATE `Werknemers` SET `Productcorrectie` = 1 WHERE `Werknemer-id` IN (0);
-- Wie het nu heeft:
-- SELECT `Werknemer-id`, `NaamVoluit` FROM `Werknemers` WHERE `Productcorrectie` <> 0;
