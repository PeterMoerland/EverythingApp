-- ===========================================================================
--  Activity board: een projectteam lost problemen op via activiteiten.
--  Naar de specificatie "Design specificatie MDE Activity Board" (Freek Moerland,
--  14-10-2026) en prototype v0.5; centrale opslag in plaats van de browser.
--
--  Project -> Board is een-op-veel (spec §10: later meerdere boards per project);
--  in release 1 krijgt elk project precies één board. Een board kan ook bij een klant
--  horen (één per klant) of bij niets (vrij board); zie ActivityBoard_Soorten_MariaDB.sql.
--  Problemen en types horen bij een board. Een activiteit koppelt aan 0..n
--  problemen, types en personen (Werknemers), via id's.
--  Scherm: Projecten ▸ Activity board.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `ActivityBoards` (
    `BoardID`        INT NOT NULL AUTO_INCREMENT,
    `ProjectID`      INT NULL,                                    -- Projects.Project-Id (leeg = klant- of vrij board)
    `RelatieID`      INT NULL,                                    -- Relations.id_supplier (klantboard)
    `Naam`           VARCHAR(200) NULL,                           -- leeg = naam van het project
    `Volgorde`       INT NOT NULL DEFAULT 0,                      -- voor later: meerdere boards per project
    `AangemaaktDoor` INT NULL,                                    -- Werknemers.Werknemer-id
    `AangemaaktOp`   DATETIME NULL,
    `last_updated`   TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (`BoardID`),
    KEY `IX_ActivityBoards_Project` (`ProjectID`),
    KEY `IX_ActivityBoards_Relatie` (`RelatieID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Positie 1..n = prioriteit (1 = hoogst). Naam uniek binnen het board, hoofdletters tellen niet
-- (utf8mb4_general_ci). Kleur = een naam uit het palet van het prototype (blue, red, ...).
CREATE TABLE IF NOT EXISTS `ActivityBoardProblemen` (
    `ProbleemID`     INT NOT NULL AUTO_INCREMENT,
    `BoardID`        INT NOT NULL,
    `Naam`           VARCHAR(200) NOT NULL,
    `Kleur`          VARCHAR(12) NOT NULL DEFAULT 'default',
    `Positie`        INT NOT NULL DEFAULT 0,
    `last_updated`   TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (`ProbleemID`),
    UNIQUE KEY `UX_ActivityBoardProblemen_Naam` (`BoardID`, `Naam`),
    CONSTRAINT `FK_ActivityBoardProblemen_Board` FOREIGN KEY (`BoardID`)
        REFERENCES `ActivityBoards` (`BoardID`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `ActivityBoardTypes` (
    `TypeID`         INT NOT NULL AUTO_INCREMENT,
    `BoardID`        INT NOT NULL,
    `Naam`           VARCHAR(100) NOT NULL,
    `Kleur`          VARCHAR(12) NOT NULL DEFAULT 'default',
    `Positie`        INT NOT NULL DEFAULT 0,                      -- volgorde van aanmaken
    `last_updated`   TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (`TypeID`),
    UNIQUE KEY `UX_ActivityBoardTypes_Naam` (`BoardID`, `Naam`),
    CONSTRAINT `FK_ActivityBoardTypes_Board` FOREIGN KEY (`BoardID`)
        REFERENCES `ActivityBoards` (`BoardID`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Status: backlog, next, bezig, testen, beoordeling, opgelost, nietopgelost (groepen To do / Bezig / Klaar).
-- Prioriteit: 1 = Hoog, 2 = Gemiddeld, 3 = Laag, NULL = geen. Volgorde = plek binnen het vak (status x baan).
CREATE TABLE IF NOT EXISTS `ActivityBoardActiviteiten` (
    `ActiviteitID`    INT NOT NULL AUTO_INCREMENT,
    `BoardID`         INT NOT NULL,
    `Titel`           VARCHAR(300) NOT NULL,
    `Status`          VARCHAR(20) NOT NULL DEFAULT 'backlog',
    `Prioriteit`      TINYINT NULL,
    `Vervaldatum`     DATE NULL,
    `GemaaktOp`       DATETIME NOT NULL,
    `GeslotenOp`      DATETIME NULL,                              -- gezet bij een status in de groep Klaar
    `Volgorde`        INT NOT NULL DEFAULT 0,
    `Samenvatting`    TEXT NULL,
    `Details`         TEXT NULL,
    `Impact`          TEXT NULL,
    `GenomenStappen`  TEXT NULL,
    `VolgendeStappen` TEXT NULL,
    `Opmerkingen`     TEXT NULL,
    `GemaaktDoor`     INT NULL,                                   -- Werknemers.Werknemer-id
    `GewijzigdDoor`   INT NULL,
    `last_updated`    TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (`ActiviteitID`),
    KEY `IX_ActivityBoardActiviteiten_Board` (`BoardID`, `Status`),
    CONSTRAINT `FK_ActivityBoardActiviteiten_Board` FOREIGN KEY (`BoardID`)
        REFERENCES `ActivityBoards` (`BoardID`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Koppelingen. Verwijderen van een probleem of type haalt alleen de koppeling weg; de activiteit blijft.
CREATE TABLE IF NOT EXISTS `ActivityBoardActProblemen` (
    `ActiviteitID`   INT NOT NULL,
    `ProbleemID`     INT NOT NULL,
    PRIMARY KEY (`ActiviteitID`, `ProbleemID`),
    KEY `IX_ActivityBoardActProblemen_Probleem` (`ProbleemID`),
    CONSTRAINT `FK_ActivityBoardActProblemen_Act` FOREIGN KEY (`ActiviteitID`)
        REFERENCES `ActivityBoardActiviteiten` (`ActiviteitID`) ON DELETE CASCADE,
    CONSTRAINT `FK_ActivityBoardActProblemen_Prob` FOREIGN KEY (`ProbleemID`)
        REFERENCES `ActivityBoardProblemen` (`ProbleemID`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `ActivityBoardActTypes` (
    `ActiviteitID`   INT NOT NULL,
    `TypeID`         INT NOT NULL,
    PRIMARY KEY (`ActiviteitID`, `TypeID`),
    KEY `IX_ActivityBoardActTypes_Type` (`TypeID`),
    CONSTRAINT `FK_ActivityBoardActTypes_Act` FOREIGN KEY (`ActiviteitID`)
        REFERENCES `ActivityBoardActiviteiten` (`ActiviteitID`) ON DELETE CASCADE,
    CONSTRAINT `FK_ActivityBoardActTypes_Type` FOREIGN KEY (`TypeID`)
        REFERENCES `ActivityBoardTypes` (`TypeID`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Toegewezen aan: Werknemers.Werknemer-id (geen FK: Werknemers is een oude tabel; uit dienst blijft de naam zichtbaar).
CREATE TABLE IF NOT EXISTS `ActivityBoardActPersonen` (
    `ActiviteitID`   INT NOT NULL,
    `WerknemerID`    INT NOT NULL,
    PRIMARY KEY (`ActiviteitID`, `WerknemerID`),
    KEY `IX_ActivityBoardActPersonen_Werknemer` (`WerknemerID`),
    CONSTRAINT `FK_ActivityBoardActPersonen_Act` FOREIGN KEY (`ActiviteitID`)
        REFERENCES `ActivityBoardActiviteiten` (`ActiviteitID`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
