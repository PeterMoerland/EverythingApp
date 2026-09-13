/* ============================================================================
   UITBREIDING: arbeidsvoorwaarden op de gebeurtenissen   (MariaDB / MySQL)
   ----------------------------------------------------------------------------
   Salaris en vrije dagen horen bij een GEBEURTENIS, niet bij de werknemer:
   ze veranderen in de tijd en je wilt weten wat er wanneer gold. Bij elke
   wijziging leg je een nieuwe gebeurtenis vast; de vorige blijft staan.

   Draai dit na Werknemers_Schema_MariaDB.sql.
   ============================================================================ */

ALTER TABLE `Werknemer_Gebeurtenis`
  ADD COLUMN `BrutoSalaris`   DECIMAL(12,2) NULL COMMENT 'bruto per maand bij voltijd'   AFTER `Bedrag`,
  ADD COLUMN `DertiendeMaand` TINYINT(1)    NULL COMMENT '1 = recht op 13e maand'        AFTER `BrutoSalaris`,
  ADD COLUMN `VrijeDagen`     DECIMAL(5,1)  NULL COMMENT 'vakantiedagen per jaar'        AFTER `DertiendeMaand`,
  ADD COLUMN `AtvDagen`       DECIMAL(5,1)  NULL COMMENT 'ATV-dagen per jaar'            AFTER `VrijeDagen`;

/* Soorten die deze velden gebruiken staan standaard op privé —
   salaris hoort niet voor iedereen zichtbaar te zijn. */
UPDATE `Gebeurtenis_Soort`
   SET `StandaardPrive` = 1
 WHERE `Naam` IN ('Salariswijziging', 'In dienst', 'Contract verlengd',
                  'Contract onbepaalde tijd', 'Wijziging contracturen');

/* Nieuwe soort voor een wijziging van alleen de voorwaarden */
INSERT INTO `Gebeurtenis_Soort`
  (`Naam`, `Groep`, `WijzigtDienst`, `VraagtDocument`, `StandaardPrive`, `Jubileum`)
VALUES ('Wijziging arbeidsvoorwaarden', 'Contract', 0, 1, 1, 0)
ON DUPLICATE KEY UPDATE `Groep` = VALUES(`Groep`);

/* ---------------------------------------------------------------------------
   View: wat geldt er NU per werknemer?
   Neemt van elk veld de laatste gebeurtenis waarin het is ingevuld, zodat een
   gebeurtenis waarin alleen het salaris wijzigt de vrije dagen laat staan.
   --------------------------------------------------------------------------- */
CREATE OR REPLACE VIEW `vw_Werknemer_Voorwaarden` AS
SELECT
    w.`Werknemer-id` AS `WerknemerId`,
    (SELECT g.`BrutoSalaris`   FROM `Werknemer_Gebeurtenis` g
      WHERE g.`WerknemerId` = w.`Werknemer-id` AND g.`BrutoSalaris` IS NOT NULL
        AND g.`Moment` <= NOW() ORDER BY g.`Moment` DESC, g.`GebeurtenisId` DESC LIMIT 1) AS `BrutoSalaris`,
    (SELECT g.`DertiendeMaand` FROM `Werknemer_Gebeurtenis` g
      WHERE g.`WerknemerId` = w.`Werknemer-id` AND g.`DertiendeMaand` IS NOT NULL
        AND g.`Moment` <= NOW() ORDER BY g.`Moment` DESC, g.`GebeurtenisId` DESC LIMIT 1) AS `DertiendeMaand`,
    (SELECT g.`VrijeDagen`     FROM `Werknemer_Gebeurtenis` g
      WHERE g.`WerknemerId` = w.`Werknemer-id` AND g.`VrijeDagen` IS NOT NULL
        AND g.`Moment` <= NOW() ORDER BY g.`Moment` DESC, g.`GebeurtenisId` DESC LIMIT 1) AS `VrijeDagen`,
    (SELECT g.`AtvDagen`       FROM `Werknemer_Gebeurtenis` g
      WHERE g.`WerknemerId` = w.`Werknemer-id` AND g.`AtvDagen` IS NOT NULL
        AND g.`Moment` <= NOW() ORDER BY g.`Moment` DESC, g.`GebeurtenisId` DESC LIMIT 1) AS `AtvDagen`,
    (SELECT g.`Contracturen`   FROM `Werknemer_Gebeurtenis` g
      WHERE g.`WerknemerId` = w.`Werknemer-id` AND g.`Contracturen` IS NOT NULL
        AND g.`Moment` <= NOW() ORDER BY g.`Moment` DESC, g.`GebeurtenisId` DESC LIMIT 1) AS `Contracturen`,
    (SELECT g.`FunctieId`      FROM `Werknemer_Gebeurtenis` g
      WHERE g.`WerknemerId` = w.`Werknemer-id` AND g.`FunctieId` IS NOT NULL
        AND g.`Moment` <= NOW() ORDER BY g.`Moment` DESC, g.`GebeurtenisId` DESC LIMIT 1) AS `FunctieId`
FROM `Werknemers` w;
