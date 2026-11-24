-- ===========================================================================
--  Verbeterverzoeken: prioriteit per verzoek (wens gebruiker 24-11-2026).
--  1 = hoog, 2 = gemiddeld, 3 = laag, NULL = geen. Directie zet hem op de pagina Verbeterverzoeken; de lijst kan erop
--  filteren.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien. Zonder dit script werkt de pagina gewoon,
--  alleen zonder prioriteit. De knop Idee in Inventor schrijft in dezelfde tabel en heeft hier geen last van.
-- ===========================================================================

ALTER TABLE `Verbeterverzoeken` ADD COLUMN IF NOT EXISTS `Prioriteit` TINYINT NULL;
