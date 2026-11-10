-- ===========================================================================
--  BV op inkooporders (wens gebruiker 10-11-2026): MDE Automation en M-Pede Robotics kopen elk zelf in.
--  Orders.BvID = de eigen BV die bestelt (Relations.id_supplier). Een nieuwe bestelling krijgt de BV van het project van
--  de werkorder (Projects.Werkmaatschappij); in het formulier kan hij gewijzigd worden (met een waarschuwing als hij
--  afwijkt van het project). De mail aan de leverancier staat op naam van die BV, met het factuuradres als het niet
--  MDE Automation is.
--  Bestaande bestellingen: de BV van het project van hun werkorder; zonder werkorder of project MDE Automation (5254).
--
--  Eenmalig uitvoeren op de MDE-database, VOOR het publiceren. Veilig om nog eens te draaien.
-- ===========================================================================

ALTER TABLE `Orders`
    ADD COLUMN IF NOT EXISTS `BvID` INT NULL;

UPDATE `Orders` o
  LEFT JOIN `Workorders` w ON w.`Werkbon-id` = o.`Workorder-id`
  LEFT JOIN `Projects` p ON p.`Project-Id` = w.`Project-id`
   SET o.`BvID` = COALESCE(NULLIF(p.`Werkmaatschappij`, 0), 5254)
 WHERE o.`BvID` IS NULL;
