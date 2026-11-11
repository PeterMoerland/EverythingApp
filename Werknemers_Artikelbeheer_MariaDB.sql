-- ===========================================================================
--  Recht Artikelbeheer: artikelen en samenstellingen wijzigen (Artikelen, Artikelsamenstellingen).
--  Zonder dit recht (of Directie) zijn die schermen alleen te lezen (wens gebruiker 11-11-2026).
--  Daarvoor keek de code naar de kolommen Inkoop en Beheer, die niet bestaan, dus mocht alleen Directie;
--  en wie niet aan een werknemer gekoppeld was, mocht alles. Dat laatste is nu dicht.
--  Zetten in Werknemers > Rechten > Artikelbeheer.
--
--  Eenmalig uitvoeren op de MDE-database. Volgorde maakt niet uit: zonder de kolom mag alleen Directie.
-- ===========================================================================

ALTER TABLE `Werknemers`
    ADD COLUMN IF NOT EXISTS `Artikelbeheer` TINYINT(1) NOT NULL DEFAULT 0;

-- Wie het recht krijgt (vul de juiste werknemer-id's in):
-- UPDATE `Werknemers` SET `Artikelbeheer` = 1 WHERE `Werknemer-id` IN (0);
-- Wie het nu heeft:
-- SELECT `Werknemer-id`, `NaamVoluit` FROM `Werknemers` WHERE `Artikelbeheer` <> 0;
