-- ===========================================================================
--  Bel > Berichten > Verzonden (wens gebruiker 27-11-2026): de berichten die je zelf aan collega's stuurde.
--
--  De lijst Verzonden werkt ook zonder dit script. Met deze kolom blijft een bericht bij de afzender staan als de
--  ontvanger het uit zijn eigen box wist: wissen zet dan alleen GewistOp, de regel zelf blijft bestaan. Meldingen van
--  de app (goedgekeurd, geleverd ...) worden bij wissen nog steeds echt verwijderd.
--
--  Uitvoeren op de MDE-database. Meerdere keren draaien kan geen kwaad.
-- ===========================================================================

ALTER TABLE `Meldingen`
    ADD COLUMN IF NOT EXISTS `GewistOp` DATETIME NULL;
