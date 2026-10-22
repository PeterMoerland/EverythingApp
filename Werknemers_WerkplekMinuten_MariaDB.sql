-- ===========================================================================
--  Werkplek (gedeelde pc): na hoeveel minuten zonder activiteit een medewerker weer afgemeld wordt, per werknemer.
--  Leeg (NULL) = de standaard van 30 minuten. Hoogstens 720. In te vullen op het werknemersscherm (tab Status,
--  "Werkplek: minuten tot afmelden") of hieronder in SQL. Wens gebruiker 22-10-2026.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

ALTER TABLE `Werknemers`
    ADD COLUMN IF NOT EXISTS `WerkplekMinuten` INT NULL;

-- Voorbeeld: 60 minuten voor een werknemer
-- UPDATE `Werknemers` SET `WerkplekMinuten` = 60 WHERE `Werknemer-id` = 0;
