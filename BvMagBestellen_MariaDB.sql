-- ===========================================================================
--  Welke eigen BV's mogen bestellen (wens gebruiker 10-11-2026): Relaties > tab BV > "Mag bestellen (inkoop)".
--  Een bestelling op een werkorder van een project van een BV die niet mag bestellen wordt geweigerd (nieuwe bestelling,
--  werkorder wijzigen, bestellen vanuit een verkooporder).
--  Bij het aanmaken van de kolom krijgen MDE Automation en M-Pede Robotics het vinkje; daarna beheer je het in Relaties.
--
--  Eenmalig uitvoeren op de MDE-database, VOOR het publiceren. Veilig om nog eens te draaien (zet geen vinkjes terug).
-- ===========================================================================

SET @nieuw = (SELECT COUNT(*) = 0 FROM information_schema.COLUMNS
               WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Relations' AND COLUMN_NAME = 'MagBestellen');

ALTER TABLE `Relations`
    ADD COLUMN IF NOT EXISTS `MagBestellen` TINYINT(1) NOT NULL DEFAULT 0;

-- alleen de eerste keer (net aangemaakt): MDE Automation en M-Pede Robotics mogen bestellen
UPDATE `Relations` SET `MagBestellen` = 1 WHERE @nieuw = 1 AND (`id_supplier` = 5254 OR `Afkorting` = 'MPR');
