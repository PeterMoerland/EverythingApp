-- ===========================================================================
--  Activity board (wensen gebruiker 24-10-2026):
--  1. Een board ook bij een werkorder: naast project, klant en vrij. Eén board per werkorder; dat bewaakt de app.
--     Een werkorderboard heeft geen ProjectID (dat blijft voor het projectboard).
--  2. Bijlagen (plaatjes en bestanden) bij een onderwerp: filestore-soort 342, MasterID = ActivityBoardProblemen.ProbleemID.
--
--  Na ActivityBoard_MariaDB.sql en ActivityBoard_Soorten_MariaDB.sql uitvoeren. Veilig om nog eens te draaien.
-- ===========================================================================

ALTER TABLE `ActivityBoards` ADD COLUMN IF NOT EXISTS `WerkorderID` INT NULL AFTER `RelatieID`;   -- Workorders.Werkbon-id
ALTER TABLE `ActivityBoards` ADD INDEX IF NOT EXISTS `IX_ActivityBoards_Werkorder` (`WerkorderID`);

-- Filestore-soort voor bijlagen bij een onderwerp; FileStore.StoreType verwijst naar FileStoreType (anders weigert de database)
INSERT IGNORE INTO `FileStoreType` (`FilestoreTypeID`, `FileStoreTypeDescription`) VALUES (342, 'Activity board onderwerp bijlage');
