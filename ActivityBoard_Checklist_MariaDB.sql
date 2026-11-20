-- Activity board: afvinklijst per activiteit (vraag Gerard, 20-11-2026; overzetten van de Notion-database "Robot Software").
-- Een activiteit krijgt een lijst met punten op vaste volgorde; een regel is een kopje (IsKop = 1) of een punt. De punten
-- onder een kopje zijn de regels tot het volgende kopje. Bij een punt staat wie het afvinkte en wanneer.
--
-- Zelf draaien op de database van de app. Meerdere keren draaien kan geen kwaad. Zonder dit script werkt het board zoals
-- het was: de afvinklijst is dan niet te zien en de API zegt dat hij er nog niet is.

CREATE TABLE IF NOT EXISTS `ActivityBoardChecklist` (
    `PuntID`         INT NOT NULL AUTO_INCREMENT,
    `ActiviteitID`   INT NOT NULL,
    `IsKop`          TINYINT(1) NOT NULL DEFAULT 0,      -- 1 = kopje, 0 = punt om af te vinken
    `Tekst`          VARCHAR(500) NOT NULL,
    `Klaar`          TINYINT(1) NOT NULL DEFAULT 0,
    `KlaarOp`        DATETIME NULL,
    `KlaarDoor`      INT NULL,                            -- Werknemers.Werknemer-id van wie het afvinkte
    `Positie`        INT NOT NULL DEFAULT 0,              -- volgorde binnen de activiteit (0, 1, 2 …)
    `AangemaaktOp`   DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `AangemaaktDoor` INT NULL,
    PRIMARY KEY (`PuntID`),
    KEY `IX_ABChecklist_Activiteit` (`ActiviteitID`, `Positie`),
    CONSTRAINT `FK_ABChecklist_Activiteit` FOREIGN KEY (`ActiviteitID`)
        REFERENCES `ActivityBoardActiviteiten` (`ActiviteitID`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
