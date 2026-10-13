-- Producten: vrij tekstveld 'Project' per exemplaar (verzoek collega, 13-10-2026).
-- Het scherm Producten toont het veld pas als deze kolom bestaat; daarna de service
-- herstarten (de kolommen van Producten worden bij de start één keer gelezen).
-- Mag vaker gedraaid worden.

ALTER TABLE `Producten`
    ADD COLUMN IF NOT EXISTS `Project` VARCHAR(100) NULL AFTER `KlantID`;
