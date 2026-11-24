-- ===========================================================================
--  Activity board: statuslijsten (idee van Arnold, 24-11-2026).
--  Een statuslijst is een tabel op een board: kolommen (bv. machine 1 t/m 8), rijen (IPC, Ixon, UPS ...) onder
--  kopjes (Inregelen, Updaten), en per cel een status (todo, bezig, testen, klaar). Per cel wie hem het laatst
--  wijzigde en wanneer; per rij een opmerking.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien. Zonder dit script werkt het board gewoon,
--  alleen zonder het tabblad Statuslijsten.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `ActivityBoardStatuslijsten` (
  `LijstID`     INT NOT NULL AUTO_INCREMENT,
  `BoardID`     INT NOT NULL,
  `Naam`        VARCHAR(200) NOT NULL,
  `GemaaktDoor` INT NULL,
  `GemaaktOp`   DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`LijstID`),
  KEY `IX_ActivityBoardStatuslijsten_Board` (`BoardID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `ActivityBoardStatusKolommen` (
  `KolomID` INT NOT NULL AUTO_INCREMENT,
  `LijstID` INT NOT NULL,
  `Naam`    VARCHAR(100) NOT NULL,
  `Positie` INT NOT NULL DEFAULT 0,
  PRIMARY KEY (`KolomID`),
  KEY `IX_ActivityBoardStatusKolommen_Lijst` (`LijstID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `ActivityBoardStatusRijen` (
  `RijID`      INT NOT NULL AUTO_INCREMENT,
  `LijstID`    INT NOT NULL,
  `IsKop`      TINYINT(1) NOT NULL DEFAULT 0,
  `Naam`       VARCHAR(200) NOT NULL,
  `Opmerking`  TEXT NULL,
  `Positie`    INT NOT NULL DEFAULT 0,
  PRIMARY KEY (`RijID`),
  KEY `IX_ActivityBoardStatusRijen_Lijst` (`LijstID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `ActivityBoardStatusCellen` (
  `RijID`         INT NOT NULL,
  `KolomID`       INT NOT NULL,
  `Status`        VARCHAR(10) NOT NULL,
  `GewijzigdDoor` INT NULL,
  `GewijzigdOp`   DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`RijID`, `KolomID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
