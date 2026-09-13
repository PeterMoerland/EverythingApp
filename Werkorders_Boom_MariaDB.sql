-- ============================================================
--  Werkorders als boom
--
--  Eén kolom erbij: ParentWerkbonID. Leeg = direct onder het project (zoals
--  alles nu is), gevuld = kind van een andere werkorder van hetzelfde
--  project. Zo wordt een productielijn: project > lijn > machines > modules,
--  en een kleine machine gewoon: project > machine > 2 modules.
--
--  Bestaande data blijft ongemoeid (alles leeg = platte lijst) en Access
--  negeert de kolom; beide programma's draaien gewoon door.
--
--  De app bewaakt zelf dat ouder en kind bij hetzelfde project horen en dat
--  er geen kringen ontstaan (een werkorder kan geen kind van zijn eigen
--  nakomeling worden).
-- ============================================================

-- Let op: dit script is voor eenmalig gebruik. Draai je het per ongeluk een
-- tweede keer, dan krijg je een onschuldige melding "Duplicate column name" —
-- dat betekent alleen dat het al gebeurd is.
ALTER TABLE `Workorders`
  ADD COLUMN `ParentWerkbonID` int(10) DEFAULT NULL;

-- Snel de kinderen van een werkorder vinden.
ALTER TABLE `Workorders`
  ADD INDEX `IX_Workorders_Parent` (`ParentWerkbonID`);

-- De verwijzing kan hard als de kolomtypes kloppen; bij twijfel weglaten,
-- de app werkt ook zonder.
-- ALTER TABLE `Workorders`
--   ADD CONSTRAINT `FK_Workorders_Parent`
--   FOREIGN KEY (`ParentWerkbonID`) REFERENCES `Workorders` (`Werkbon-id`);
