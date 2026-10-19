-- ===========================================================================
--  Uitgaande facturen: per factuur een werkorder (Financiën ▸ Uitgaande facturen ▸ factuur ▸ Werkorder).
--  Intern veld, komt niet op de factuur; ook na versturen aan te passen.
--  (SelectieWerkorderString is iets anders: de selectie van werkorders voor het ophalen van uren.)
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

ALTER TABLE `Invoices`
    ADD COLUMN IF NOT EXISTS `Workorder-id` INT NULL;              -- Workorders.Werkbon-id

CREATE INDEX IF NOT EXISTS `IX_Invoices_Workorder` ON `Invoices` (`Workorder-id`);
