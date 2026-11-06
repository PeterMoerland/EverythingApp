-- ===========================================================================
--  Recht Fusion: de schermen onder Tools > Fusion (Converteer naar CAM, Eurocircuits, JLCPCB).
--  Zonder dit recht staan ze niet in het menu en zijn ze ook via het adres dicht (wens gebruiker 6-11-2026).
--  Op een werkplek telt het recht van de werkplek (de werknemer met Persoon = nee), niet dat van wie er aangemeld is.
--  Zetten in Werknemers > Rechten > Fusion.
--
--  Eenmalig uitvoeren op de MDE-database, VOOR het publiceren (anders ziet niemand Fusion tot het vinkje staat).
-- ===========================================================================

ALTER TABLE `Werknemers`
    ADD COLUMN IF NOT EXISTS `Fusion` TINYINT(1) NOT NULL DEFAULT 0;

-- Wie het recht krijgt (vul de juiste werknemer-id's in), bijvoorbeeld de elektronica-ontwerpers en de Fusion-werkplek:
-- UPDATE `Werknemers` SET `Fusion` = 1 WHERE `Werknemer-id` IN (0);
-- Of: zoals het nu is, iedereen in dienst (daarna per persoon uitzetten):
-- UPDATE `Werknemers` SET `Fusion` = 1 WHERE COALESCE(`Uit dienst`, 0) = 0;
-- Wie het nu heeft:
-- SELECT `Werknemer-id`, `NaamVoluit` FROM `Werknemers` WHERE `Fusion` <> 0;
