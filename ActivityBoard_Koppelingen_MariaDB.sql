-- Activity board: koppelingen tussen activiteiten (vraag Gerard, 20-11-2026), ook tussen twee boards.
-- Eén rij per koppeling, van de ene activiteit (VanID) naar de andere (NaarID):
--   afgesplitst  = Van is afgesplitst van Naar   (bij Naar staat "opgesplitst in")
--   hangtaf      = Van hangt af van Naar         (bij Naar staat "blokkeert")
--   zieook       = zie ook                       (aan beide kanten hetzelfde)
-- Wordt een van de twee activiteiten verwijderd, dan gaat de koppeling mee weg.
--
-- Zelf draaien op de database van de app. Meerdere keren draaien kan geen kwaad. Zonder dit script zijn er geen
-- koppelingen te zien en zegt de API dat ze er nog niet zijn.

CREATE TABLE IF NOT EXISTS `ActivityBoardKoppelingen` (
    `KoppelingID`    INT NOT NULL AUTO_INCREMENT,
    `VanID`          INT NOT NULL,
    `NaarID`         INT NOT NULL,
    `Soort`          VARCHAR(20) NOT NULL,                -- afgesplitst, hangtaf of zieook
    `AangemaaktOp`   DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `AangemaaktDoor` INT NULL,
    PRIMARY KEY (`KoppelingID`),
    UNIQUE KEY `UX_ABKoppelingen` (`VanID`, `NaarID`, `Soort`),
    KEY `IX_ABKoppelingen_Naar` (`NaarID`),
    CONSTRAINT `FK_ABKoppelingen_Van` FOREIGN KEY (`VanID`)
        REFERENCES `ActivityBoardActiviteiten` (`ActiviteitID`) ON DELETE CASCADE,
    CONSTRAINT `FK_ABKoppelingen_Naar` FOREIGN KEY (`NaarID`)
        REFERENCES `ActivityBoardActiviteiten` (`ActiviteitID`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
