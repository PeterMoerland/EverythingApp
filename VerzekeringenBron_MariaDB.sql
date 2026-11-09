-- ===========================================================================
--  Verzekering = de enige bron voor de premie (besluit gebruiker 9-11-2026).
--  De premie, termijn en betaaldag staan in Verzekeringen; de app maakt of werkt daarmee zelf de vaste last bij
--  (categorie Verzekeringen), die Vaste lasten en Liquiditeit gewoon blijven lezen. In Vaste lasten zijn die regels
--  niet los aan te passen. Opgezegd of beëindigd = de vaste last krijgt een einddatum (de dag voor de vervaldatum).
--  Na het draaien neemt Financiën > Verzekeringen bij het openen de premie, termijn en betaaldag over van de al
--  gekoppelde vaste lasten.
--
--  Eenmalig uitvoeren op de MDE-database (na Verzekeringen_MariaDB.sql), VOOR het publiceren. Veilig om nog eens te draaien.
-- ===========================================================================

ALTER TABLE `Verzekeringen`
    ADD COLUMN IF NOT EXISTS `PremieDag`   INT NULL,     -- weekdag (1-7) bij wekelijks, anders dag van de maand
    ADD COLUMN IF NOT EXISTS `PremieMaand` INT NULL;     -- maand (1-12) bij jaarlijks, 1-3 bij kwartaal

ALTER TABLE `VasteLasten`
    ADD COLUMN IF NOT EXISTS `Einddatum` DATE NULL;      -- na deze dag geen betalingen meer (leeg = loopt door)
