/* ============================================================================
   NDA-module — tabellen voor MariaDB (MDE-database)

   Vervangt nda_register.json uit de losse Python-app. De documenten zelf gaan
   naar de bestaande filestore, dus hier staan alleen verwijzingen (FileStoreID).

   Uitvoeren in HeidiSQL of MySQL Workbench op de MDE-database.
   Alles is aanvullend: de Access-frontend blijft ongemoeid werken.
   ============================================================================ */

/* ---------------------------------------------------------------------------
   1. De NDA zelf
   Adres, rechtsvorm en KvK worden als momentopname bewaard: als een relatie
   later verhuist, moet het document nog steeds kloppen met wat er destijds
   is ondertekend.
   --------------------------------------------------------------------------- */
CREATE TABLE IF NOT EXISTS `NDA` (
  `NDA_id`              INT(10)      NOT NULL AUTO_INCREMENT,
  `Relation_id`         INT(10)          NULL COMMENT 'Relations.id_supplier',
  `Contactperson_id`    INT(10)          NULL COMMENT 'Contacts.ID_contact — ondertekenaar wederpartij',
  `Project_id`          INT(10)          NULL COMMENT 'Projects.Project-Id, indien van toepassing',

  /* momentopname van de wederpartij zoals in het document */
  `RelatieNaam`         VARCHAR(150)     NULL,
  `Rechtsvorm`          VARCHAR(50)      NULL,
  `Adres`               VARCHAR(255)     NULL,
  `KvK`                 VARCHAR(30)      NULL,
  `ToepasselijkRecht`   VARCHAR(50)      NULL DEFAULT 'Nederlands',

  `Projectomschrijving` VARCHAR(255)     NULL,
  `Projectnummer`       VARCHAR(30)      NULL,

  `DatumDocument`       DATE             NULL COMMENT 'datum zoals in het document',
  `DatumAangemaakt`     DATETIME         NULL,
  `AangemaaktDoor`      INT(10)          NULL COMMENT 'Werknemers.Werknemer-id',
  `DatumVerzonden`      DATETIME         NULL,
  `DatumOndertekend`    DATETIME         NULL,

  /* Einddatum: NULL = niet ingesteld, 1 in Onbepaald = geen einddatum */
  `Einddatum`           DATE             NULL,
  `Onbepaald`           TINYINT(1)   NOT NULL DEFAULT 0,

  `Status`              VARCHAR(20)  NOT NULL DEFAULT 'Concept'
                        COMMENT 'Concept | Verzonden | Ondertekend | Vervallen',
  `Soort`               VARCHAR(20)  NOT NULL DEFAULT 'eigen'
                        COMMENT 'eigen = door ons opgesteld, geimporteerd = van de wederpartij',

  `Notities`            LONGTEXT         NULL,
  `Zoektekst`           LONGTEXT         NULL COMMENT 'uitgelezen tekst van geïmporteerde documenten',

  `last_updated`        TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,

  PRIMARY KEY (`NDA_id`),
  KEY `IX_NDA_Relatie` (`Relation_id`),
  KEY `IX_NDA_Status`  (`Status`),
  KEY `IX_NDA_Eind`    (`Einddatum`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

/* Vrij zoeken in de tekst van geïmporteerde NDA's.
   Werkt vanaf MariaDB 10.0; geeft het een foutmelding, dan sla je dit over. */
ALTER TABLE `NDA` ADD FULLTEXT INDEX `FT_NDA_Zoektekst` (`Zoektekst`);

/* ---------------------------------------------------------------------------
   2. De versies. Elke versie kan uit twee bestanden bestaan (Word en PDF),
   dus dit zijn losse regels met een Soort.
   --------------------------------------------------------------------------- */
CREATE TABLE IF NOT EXISTS `NDA_Bestand` (
  `NDA_Bestand_id`  INT(10)      NOT NULL AUTO_INCREMENT,
  `NDA_id`          INT(10)      NOT NULL,
  `Versie`          VARCHAR(20)  NOT NULL COMMENT 'v1, v2, … of "getekend"',
  `Soort`           VARCHAR(20)  NOT NULL COMMENT 'word | pdf | getekend',
  `FileStoreID`     INT(10)          NULL COMMENT 'FileStore.FileStoreID',
  `Datum`           DATETIME         NULL,
  `ToegevoegdDoor`  INT(10)          NULL,
  `Notitie`         VARCHAR(255)     NULL,
  `last_updated`    TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`NDA_Bestand_id`),
  KEY `IX_NDA_Bestand_NDA` (`NDA_id`),
  CONSTRAINT `FK_NDA_Bestand_NDA` FOREIGN KEY (`NDA_id`)
      REFERENCES `NDA` (`NDA_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

/* ---------------------------------------------------------------------------
   3. Instellingen die in de Python-app in config.json stonden.
   Zo kun je ze straks in Beheer wijzigen zonder een bestand op de server aan
   te passen.
   --------------------------------------------------------------------------- */
CREATE TABLE IF NOT EXISTS `NDA_Instelling` (
  `Sleutel`      VARCHAR(50)   NOT NULL,
  `Waarde`       VARCHAR(255)      NULL,
  `Omschrijving` VARCHAR(255)      NULL,
  `last_updated` TIMESTAMP     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`Sleutel`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

INSERT INTO `NDA_Instelling` (`Sleutel`, `Waarde`, `Omschrijving`) VALUES
  ('retourverplichting',    '10 (tien)',                  'Aantal dagen retourverplichting (artikel 3)'),
  ('looptijd',              'vijf (5)',                   'Looptijd in jaren (artikel 4)'),
  ('nawerkingstermijn',     'vijf (5)',                   'Nawerkingstermijn in jaren (artikel 4)'),
  ('mde_ondertekenaar',     'E.W.J. Denissen',            'Ondertekenaar namens MDE'),
  ('mde_functie',           'Managing Director',          'Functie van de ondertekenaar'),
  ('email_naam',            'Freek Moerland',             'Afzender in de conceptmail'),
  ('email_telefoon',        '+31 (0)166 60 14 77',        'Telefoonnummer in de conceptmail'),
  ('email_adres',           'f.moerland@mde-automation.nl', 'E-mailadres in de conceptmail')
ON DUPLICATE KEY UPDATE `Omschrijving` = VALUES(`Omschrijving`);

/* ---------------------------------------------------------------------------
   4. Filestore: nieuw StoreType voor NDA-documenten.
   FileStore.StoreType heeft een foreign key naar `filestoretype`, dus dit
   nummer moet daar eerst in staan. Controleer of 310 en 311 nog vrij zijn:
       SELECT * FROM `filestoretype` ORDER BY `FilestoreTypeID`;

       310 = NDA       (MasterID = NDA.NDA_id)
       311 = Relatie   (MasterID = Relations.id_supplier) — losse documenten die
                        je op het tabblad Documenten bij een relatie sleept.
   --------------------------------------------------------------------------- */
INSERT INTO `filestoretype` (`FilestoreTypeID`, `Omschrijving`) VALUES
  (310, 'NDA'),
  (311, 'Relatie')
ON DUPLICATE KEY UPDATE `Omschrijving` = VALUES(`Omschrijving`);

/* ---------------------------------------------------------------------------
   5. Recht om NDA's op te stellen en te wijzigen.
   Zelfde opzet als de bestaande rechtenkolommen (HRM, Directie, Beheer):
   een vinkje per werknemer. Bekijken mag ieder die bij Relaties komt.

   Geeft dit "Duplicate column name", dan bestaat de kolom al — prima.
   --------------------------------------------------------------------------- */
ALTER TABLE `Werknemers` ADD COLUMN `NDA` TINYINT(1) NULL DEFAULT 0
  COMMENT 'Mag NDA''s opstellen, versturen en ondertekend markeren';

/* Zet het recht meteen voor wie het nodig heeft (pas de namen aan):
   UPDATE `Werknemers` SET `NDA` = 1 WHERE `mail` IN
     ('f.moerland@mde-automation.nl', 'p.moerland@mde-automation.nl');
   */

/* ---------------------------------------------------------------------------
   6. Handig overzicht: wat loopt er af?
   --------------------------------------------------------------------------- */
CREATE OR REPLACE VIEW `NDA_Overzicht` AS
SELECT n.`NDA_id`,
       n.`Relation_id`,
       COALESCE(r.`name`, n.`RelatieNaam`)                    AS `Relatie`,
       n.`Projectomschrijving`,
       n.`Projectnummer`,
       n.`Status`,
       n.`Soort`,
       n.`DatumDocument`,
       n.`DatumOndertekend`,
       n.`Einddatum`,
       n.`Onbepaald`,
       CASE
         WHEN n.`Onbepaald` = 1        THEN NULL
         WHEN n.`Einddatum` IS NULL    THEN NULL
         ELSE DATEDIFF(n.`Einddatum`, CURDATE())
       END                                                    AS `DagenTotEinde`
  FROM `NDA` n
  LEFT JOIN `Relations` r ON r.`id_supplier` = n.`Relation_id`;
