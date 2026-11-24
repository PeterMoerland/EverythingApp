-- ===========================================================================
--  Activity board: afbeeldingen bij een tekstveld van een activiteit (punt van Arnold, 24-11-2026).
--  Het bestand zelf staat in de filestore (soort 344, MasterID = ActiviteitID); deze tabel zegt bij welk veld het
--  hoort en bewaart de titel en het commentaar.
--  FileStore.StoreType verwijst naar FileStoreType (FK_FileStore_StoreType); zonder die regel weigert de database het
--  bestand.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien. Zonder dit script werkt het board gewoon,
--  alleen zonder afbeeldingen.
-- ===========================================================================

INSERT IGNORE INTO `FileStoreType` (`FilestoreTypeID`, `FileStoreTypeDescription`) VALUES (344, 'Activity board afbeelding');

CREATE TABLE IF NOT EXISTS `ActivityBoardAfbeeldingen` (
  `AfbeeldingID` INT NOT NULL AUTO_INCREMENT,
  `ActiviteitID` INT NOT NULL,
  `Veld`         VARCHAR(30) NOT NULL,
  `FileStoreID`  INT NOT NULL,
  `Titel`        VARCHAR(200) NOT NULL DEFAULT '',
  `Commentaar`   TEXT NULL,
  `GemaaktDoor`  INT NULL,
  `GemaaktOp`    DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`AfbeeldingID`),
  KEY `IX_ActivityBoardAfbeeldingen_Activiteit` (`ActiviteitID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
