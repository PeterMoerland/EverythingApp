-- ===========================================================================
--  Vragen aan AI privé, met delen: een gesprek is standaard alleen zichtbaar voor de vraagsteller; met het vinkje
--  "Delen met de directie" (Openbaar = 1 op de eerste vraag van het gesprek) zien de directieleden het ook.
--  Besluit gebruiker 22-10-2026. Zonder deze kolom ziet iedereen alleen zijn eigen vragen en kan er niets gedeeld worden.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

ALTER TABLE `AiVragen` ADD COLUMN IF NOT EXISTS `Openbaar` TINYINT(1) NOT NULL DEFAULT 0;
