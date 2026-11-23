-- ===========================================================================
--  Recht Netwerk: het scherm Beheer > Netwerk (IP-adressen), de lijst met vaste IP-adressen per groep uit Access
--  (tabellen IPgroep en NetworkTopologie; die bestaan al en veranderen niet).
--  Zonder dit recht staat het scherm niet in het menu en is het ook via het adres dicht (wens gebruiker 23-11-2026).
--  Directie mag het altijd. Wachtwoorden in de lijst zijn alleen voor Directie te zien, ook met dit recht.
--  Op een werkplek (gedeelde pc) is het scherm altijd dicht.
--  Zetten in Werknemers > Rechten > Netwerk (IP-adressen).
--
--  Eenmalig uitvoeren op de MDE-database. Meerdere keren draaien kan geen kwaad.
-- ===========================================================================

ALTER TABLE `Werknemers`
    ADD COLUMN IF NOT EXISTS `Netwerk` TINYINT(1) NOT NULL DEFAULT 0;

-- Wie het recht krijgt (vul de juiste werknemer-id's in):
-- UPDATE `Werknemers` SET `Netwerk` = 1 WHERE `Werknemer-id` IN (0);
-- Wie het nu heeft:
-- SELECT `Werknemer-id`, `NaamVoluit` FROM `Werknemers` WHERE `Netwerk` <> 0;
