-- ===========================================================================
--  Uren boeken op de delen van een werkorder (wens gebruiker 10-11-2026), na CalculatieWerkorders_MariaDB.sql.
--
--  Uren.WerkorderDeelID = het deel (WerkorderDelen.DeelID) waarop een urenregel geboekt is; leeg = op de werkorder
--  zelf (alle bestaande regels, en werkorders zonder delen). Heeft een werkorder delen, dan vraagt Ureningave het
--  deel; de calculatie (tabblad Werkorders) en de nacalculatie zetten per deel begroot tegen geboekt.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien. Daarna de app herstarten (publiceren is
--  genoeg): de app leest de kolommen van Uren één keer bij het starten.
-- ===========================================================================

ALTER TABLE `Uren` ADD COLUMN IF NOT EXISTS `WerkorderDeelID` INT NULL;
CREATE INDEX IF NOT EXISTS `IX_Uren_WerkorderDeel` ON `Uren` (`WerkorderDeelID`);
