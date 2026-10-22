-- ===========================================================================
--  Uitgaande facturen: per factuur een project (Financiën ▸ Uitgaande facturen ▸ factuur ▸ Project).
--  Intern veld, komt niet op de factuur; ook na versturen aan te passen. Vervangt de koppeling aan een werkorder
--  (FactuurWerkorder_MariaDB.sql, kolom Workorder-id: NIET IN GEBRUIK, op 22-10-2026 was er geen factuur gekoppeld).
--  (SelectieProject is iets anders: de selectie voor het ophalen van uren.)
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

ALTER TABLE `Invoices`
    ADD COLUMN IF NOT EXISTS `Project-id` INT NULL;                -- Projects.Project-Id

CREATE INDEX IF NOT EXISTS `IX_Invoices_Project` ON `Invoices` (`Project-id`);
