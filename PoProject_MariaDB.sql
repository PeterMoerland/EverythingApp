-- ===========================================================================
--  PO klant: per PO het project (Financiën ▸ PO klant ▸ PO ▸ Project, verplicht).
--  Bij opslaan zet de app een budgetregel in BudgetProject (POklant = de PO) met het PO-bedrag; bij wissen van de
--  PO gaat die regel weer weg (Projecten ▸ Budget).
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

ALTER TABLE `PO_Inkomend`
    ADD COLUMN IF NOT EXISTS `Project-id` INT NULL;                -- Projects.Project-Id

CREATE INDEX IF NOT EXISTS `IX_PO_Inkomend_Project` ON `PO_Inkomend` (`Project-id`);
