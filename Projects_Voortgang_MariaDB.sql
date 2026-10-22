-- ===========================================================================
--  Voortgang per project (aanname, voor het onderhanden werk): percentage gereed, wanneer en door wie gezet, en hoe
--  (handmatig, of "Bepaal zelf" = gemaakte kosten uit de nacalculatie / totaal projectbudget). Alleen de huidige stand.
--  Projecten ▸ project ▸ tab Budget (Directie en Budgetverantwoordelijke). Wens gebruiker 22-10-2026.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

ALTER TABLE `Projects` ADD COLUMN IF NOT EXISTS `Voortgang`     DECIMAL(4,1) NULL;      -- 0,0 - 100,0 %
ALTER TABLE `Projects` ADD COLUMN IF NOT EXISTS `VoortgangOp`   DATETIME NULL;
ALTER TABLE `Projects` ADD COLUMN IF NOT EXISTS `VoortgangDoor` INT NULL;               -- Werknemers.Werknemer-id
ALTER TABLE `Projects` ADD COLUMN IF NOT EXISTS `VoortgangBron` VARCHAR(200) NULL;      -- 'handmatig' of de berekening
