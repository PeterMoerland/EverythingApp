-- ===========================================================================
--  Activity board (verzoek Edwin, 19-11-2026): per activiteit invoeren hoeveel tijd nodig is om hem uit te voeren.
--  Bewaard in uren (1,5 = anderhalf uur); NULL = niet ingevuld. De app rekent 1:30, 45m en 2d (dag = 8 uur) om.
--
--  Na ActivityBoard_MariaDB.sql uitvoeren. Veilig om nog eens te draaien. Zonder dit script werkt het board gewoon
--  door; het veld "Benodigde tijd" zegt dan dat dit script nog uitgevoerd moet worden.
-- ===========================================================================

ALTER TABLE `ActivityBoardActiviteiten` ADD COLUMN IF NOT EXISTS `BenodigdeUren` DECIMAL(7,2) NULL AFTER `Vervaldatum`;
