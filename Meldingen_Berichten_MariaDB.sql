-- ===========================================================================
--  Meldingen als berichtenbox (bel in de bovenbalk, altijd te openen):
--  - collega's sturen elkaar berichten (Soort 'bericht'): van wie staat in VanWerknemerID;
--  - de tekst mag langer zijn (was 250);
--  - gelezen berichten blijven bewaard tot de eigenaar ze zelf wist.
--
--  Eenmalig uitvoeren op de MDE-database (na Meldingen_MariaDB.sql). Veilig om nog eens te draaien.
-- ===========================================================================

ALTER TABLE `Meldingen`
    ADD COLUMN IF NOT EXISTS `VanWerknemerID` INT NULL;           -- Werknemers.Werknemer-id: afzender van een bericht

ALTER TABLE `Meldingen`
    MODIFY `Tekst` VARCHAR(1000) NOT NULL;

CREATE INDEX IF NOT EXISTS `IX_Meldingen_Werknemer_Aangemaakt` ON `Meldingen` (`WerknemerID`, `Aangemaakt`);
