-- ===========================================================================
--  Activity board: een board hoort bij een project, bij een klant (relatie) of
--  nergens bij (vrij board met alleen een naam). Eén board per project en één per
--  klant; dat bewaakt de app. Een vrij board kan later alsnog gekoppeld worden.
--
--  Na ActivityBoard_MariaDB.sql uitvoeren. Veilig om nog eens te draaien.
-- ===========================================================================

ALTER TABLE `ActivityBoards` MODIFY `ProjectID` INT NULL;                       -- Projects.Project-Id, leeg = geen project
ALTER TABLE `ActivityBoards` ADD COLUMN IF NOT EXISTS `RelatieID` INT NULL AFTER `ProjectID`;   -- Relations.id_supplier
ALTER TABLE `ActivityBoards` ADD INDEX IF NOT EXISTS `IX_ActivityBoards_Relatie` (`RelatieID`);
