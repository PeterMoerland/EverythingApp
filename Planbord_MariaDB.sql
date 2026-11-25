-- ===========================================================================
--  Planning (menu Planning, wens gebruiker 25-11-2026): groepen met daaronder activiteiten op een tijdbalk, zoals
--  Tom's Planner. Stap 1: de export van Tom's Planner eenmalig overzetten en bekijken.
--
--  1. PlanbordRijen: een groep of een activiteit. OuderID = de rij erboven (leeg = bovenaan); een rij met rijen
--     eronder is een groep. RelatieID, ProjectID en WerkorderID zijn voor later: een groep aan een klant, project of
--     werkorder koppelen, zoals een Activity board. Nummer = het rijnummer uit Tom's Planner.
--  2. PlanbordItems: de balken (Start tot Einde) en de symbolen (alleen Start, Symbool = 1) van een rij. Legenda =
--     de soort (Done, To Do, Periode, Out of office, Vertraging, Milestone …); Kleur = de kleur van de balk of het
--     nummer van het symbool, zoals in de export.
--
--  De oude tabellen Planning, PlanningSoorten en PlanningAfhankelijkheden (Planning_Schema_MariaDB.sql) blijven
--  staan; ze zijn nooit gevuld en worden niet meer gebruikt.
--
--  3. Recht Planner (stap 2, bewerken): wie het heeft (en Directie) mag de planning wijzigen. Kijken mag iedereen.
--     Zetten in Werknemers > Rechten > Planner (planning bewerken).
--
--  Uitvoeren op de MDE-database. Meerdere keren draaien kan geen kwaad. Zonder dit script staat de pagina
--  er wel, met de melding dat de tabellen ontbreken.
--  LET OP: is het script al eerder gedraaid (25-11-2026, voor stap 1), draai het dan nog een keer voor het recht
--  Planner. De planning zelf blijft staan.
-- ===========================================================================

ALTER TABLE `Werknemers`
    ADD COLUMN IF NOT EXISTS `Planner` TINYINT(1) NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS `PlanbordRijen` (
    `RijID`       INT NOT NULL AUTO_INCREMENT,
    `OuderID`     INT NULL,
    `Volgorde`    INT NOT NULL DEFAULT 0,
    `Naam`        VARCHAR(200) NOT NULL,
    `Resource`    VARCHAR(100) NULL,
    `Nummer`      VARCHAR(40) NULL,
    `RelatieID`   INT NULL,
    `ProjectID`   INT NULL,
    `WerkorderID` INT NULL,
    PRIMARY KEY (`RijID`),
    KEY `IX_PlanbordRijen_Ouder` (`OuderID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `PlanbordItems` (
    `ItemID`    INT NOT NULL AUTO_INCREMENT,
    `RijID`     INT NOT NULL,
    `Symbool`   TINYINT(1) NOT NULL DEFAULT 0,
    `Start`     DATETIME NOT NULL,
    `Einde`     DATETIME NULL,
    `Legenda`   VARCHAR(60) NULL,
    `Kleur`     VARCHAR(20) NULL,
    `Label`     VARCHAR(100) NULL,
    `Opmerking` VARCHAR(500) NULL,
    PRIMARY KEY (`ItemID`),
    KEY `IX_PlanbordItems_Rij` (`RijID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
