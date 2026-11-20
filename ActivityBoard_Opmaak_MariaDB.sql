-- Activity board: opmaak (Markdown) in de zes tekstvelden van een activiteit (vraag Gerard, 20-11-2026).
-- Per activiteit een vlag. Besluit: bestaande kaartjes blijven ongemoeid, dus bestaande rijen krijgen 0 (platte tekst,
-- precies zoals nu). Nieuwe kaartjes krijgen 1 (de app zet dat zelf); bij een bestaand kaartje kun je de opmaak in de
-- pop-up aanzetten.
--
-- Zelf draaien op de database van de app. Meerdere keren draaien kan geen kwaad. Zonder dit script blijft alle tekst plat.

ALTER TABLE `ActivityBoardActiviteiten`
    ADD COLUMN IF NOT EXISTS `Opmaak` TINYINT(1) NOT NULL DEFAULT 0 AFTER `Opmerkingen`;
