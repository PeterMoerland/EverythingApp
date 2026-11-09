-- ===========================================================================
--  Vaste lasten per BV (wens gebruiker 9-11-2026): Liquiditeit toont het overzicht per gekozen BV (of een BV met
--  dochters, uit Financiën > BV-structuur); een vaste last telt dan alleen mee bij zijn eigen BV.
--  Bestaande vaste lasten komen bij MDE Automation (5254); pas ze aan in Financiën > Vaste lasten.
--
--  Eenmalig uitvoeren op de MDE-database, VOOR het publiceren. Veilig om nog eens te draaien.
-- ===========================================================================

ALTER TABLE `VasteLasten`
    ADD COLUMN IF NOT EXISTS `BvID` INT NULL;      -- Relations.id_supplier van de eigen BV die betaalt

UPDATE `VasteLasten` SET `BvID` = 5254 WHERE `BvID` IS NULL;
