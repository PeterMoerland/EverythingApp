-- ===========================================================================
--  Recht MagFirmware: firmwareversies toevoegen en wijzigen (Productie ▸ Controllertypen ▸ tab Firmware:
--  versie, status, bestanden firmware/dictionary/ESI, vrijgegeven, opmerking, verwijderen).
--  Zonder dit recht: alleen bekijken en downloaden. Per werknemer zetten in SQL (besluit gebruiker 22-10-2026).
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

ALTER TABLE `Werknemers`
    ADD COLUMN IF NOT EXISTS `MagFirmware` TINYINT(1) NOT NULL DEFAULT 0;

-- Recht geven (voorbeeld; vul de juiste werknemer-id's in):
-- UPDATE `Werknemers` SET `MagFirmware` = 1 WHERE `Werknemer-id` IN (0);
-- Wie het nu heeft:
-- SELECT `Werknemer-id`, `NaamVoluit` FROM `Werknemers` WHERE `MagFirmware` <> 0;
