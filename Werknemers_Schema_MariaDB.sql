/* ============================================================================
   WERKNEMERS: TIJDLIJN, RELATIES EN FILESTORE   (MariaDB / MySQL)
   ----------------------------------------------------------------------------
   Drie onderdelen:

   A. Gebeurtenissen — de loopbaan als tijdlijn.
      Leg vast WAT er gebeurde en WANNEER; de huidige status volgt daaruit.
      Iemand die vertrekt en terugkomt levert extra gebeurtenissen op, er raakt
      nooit historie verloren. Elke gebeurtenis kan documenten hebben.

   B. Relaties — partner, kinderen, ouders.
      Dit zijn personen met een naam en geboortedatum, geen gebeurtenissen.
      Aparte tabel, want je wilt ze kunnen opvragen voor de verjaardagenlijst.

   C. Filestore — bestaat al en kan dit al.
      De tabel FileStore koppelt via MasterID + StoreType aan een record.
      Meerdere bestanden per record = meerdere regels. We voegen alleen twee
      nieuwe StoreType-nummers toe (werknemer en werknemer-gebeurtenis) en een
      tabelletje dat vastlegt wat die nummers betekenen.

   VOORAF CONTROLEREN
   - [Werknemer-id] in Werknemers moet INT zijn (anders het FK-type aanpassen).
   - Werknemers moet InnoDB zijn, anders werken foreign keys niet:
       SELECT engine FROM information_schema.tables
        WHERE table_schema = DATABASE() AND table_name = 'Werknemers';
     Staat er MyISAM? Laat dan de CONSTRAINT-regels weg.
   - Draai dit eerst op de testdatabase.
   ============================================================================ */


/* ===========================================================================
   A1. Soorten gebeurtenissen
   =========================================================================== */
CREATE TABLE IF NOT EXISTS `Gebeurtenis_Soort` (
  `GebeurtenisSoortId` INT NOT NULL AUTO_INCREMENT,
  `Naam`               VARCHAR(60) NOT NULL,
  `Groep`              VARCHAR(30) NOT NULL DEFAULT 'Overig',
  `WijzigtDienst`      TINYINT(1)  NOT NULL DEFAULT 0,  -- 1 = in dienst, -1 = uit dienst, 0 = neutraal
  `VraagtDocument`     TINYINT(1)  NOT NULL DEFAULT 0,  -- hint: hier hoort een document bij
  `StandaardPrive`     TINYINT(1)  NOT NULL DEFAULT 0,  -- vinkje 'privé' staat standaard aan
  `Jubileum`           TINYINT(1)  NOT NULL DEFAULT 0,  -- telt mee voor de jubileumlijst
  `Actief`             TINYINT(1)  NOT NULL DEFAULT 1,
  PRIMARY KEY (`GebeurtenisSoortId`),
  UNIQUE KEY `uq_soort_naam` (`Naam`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

INSERT INTO `Gebeurtenis_Soort`
  (`Naam`, `Groep`, `WijzigtDienst`, `VraagtDocument`, `StandaardPrive`, `Jubileum`) VALUES
  ('In dienst',               'Dienstverband',  1, 1, 0, 1),
  ('Uit dienst',              'Dienstverband', -1, 1, 0, 0),
  ('Contract verlengd',       'Contract',       0, 1, 0, 0),
  ('Contract onbepaalde tijd','Contract',       0, 1, 0, 0),
  ('Functiewijziging',        'Contract',       0, 1, 0, 0),
  ('Wijziging contracturen',  'Contract',       0, 1, 0, 0),
  ('Salariswijziging',        'Contract',       0, 1, 1, 0),
  ('Beoordelingsgesprek',     'Gesprek',        0, 1, 1, 0),
  ('Functioneringsgesprek',   'Gesprek',        0, 1, 1, 0),
  ('Afspraak',                'Gesprek',        0, 1, 1, 0),
  ('Waarschuwing',            'Gesprek',        0, 1, 1, 0),
  ('Diploma / certificaat',   'Opleiding',      0, 1, 0, 0),
  ('Cursus gevolgd',          'Opleiding',      0, 1, 0, 0),
  ('Gehuwd',                  'Persoonlijk',    0, 0, 1, 1),
  ('Geregistreerd partner',   'Persoonlijk',    0, 0, 1, 1),
  ('Overig',                  'Overig',         0, 0, 0, 0)
ON DUPLICATE KEY UPDATE `Groep` = VALUES(`Groep`);


/* ===========================================================================
   A2. De gebeurtenissen zelf — tijdlijn per werknemer
   =========================================================================== */
CREATE TABLE IF NOT EXISTS `Werknemer_Gebeurtenis` (
  `GebeurtenisId`      INT NOT NULL AUTO_INCREMENT,
  `WerknemerId`        INT NOT NULL,
  `GebeurtenisSoortId` INT NOT NULL,

  -- Wanneer de gebeurtenis GELDT (niet wanneer hij is ingevoerd)
  `Moment`             DATETIME      NOT NULL,

  `Toelichting`        VARCHAR(500)  NOT NULL DEFAULT '',

  -- Alleen invullen wat van toepassing is; zo blijft de historie compleet
  `FunctieId`          INT           NULL,   -- werknemers_functie.id_functie
  `Contracturen`       DECIMAL(5,2)  NULL,
  `WerkzaamBij`        INT           NULL,   -- Relations.id_supplier (BV)
  `Bedrag`             DECIMAL(12,2) NULL,   -- bijv. salaris
  `TotDatum`           DATE          NULL,   -- einddatum tijdelijk contract

  -- Privé: alleen zichtbaar voor HRM/Directie
  `Prive`              TINYINT(1)    NOT NULL DEFAULT 0,

  `IngevoerdOp`        DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `IngevoerdDoor`      INT           NULL,   -- Werknemer-id
  `GewijzigdOp`        TIMESTAMP     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,

  PRIMARY KEY (`GebeurtenisId`),
  KEY `ix_geb_werknemer_moment` (`WerknemerId`, `Moment`),
  KEY `ix_geb_soort` (`GebeurtenisSoortId`),
  CONSTRAINT `fk_geb_werknemer` FOREIGN KEY (`WerknemerId`)
      REFERENCES `Werknemers` (`Werknemer-id`),
  CONSTRAINT `fk_geb_soort` FOREIGN KEY (`GebeurtenisSoortId`)
      REFERENCES `Gebeurtenis_Soort` (`GebeurtenisSoortId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;


/* ===========================================================================
   B. Relaties: partner, kinderen, ouders
      (vervangt het lijstje op het Privé-tabblad in Access)
   =========================================================================== */
CREATE TABLE IF NOT EXISTS `Relatie_Soort` (
  `RelatieSoortId` INT NOT NULL AUTO_INCREMENT,
  `Naam`           VARCHAR(40) NOT NULL,
  PRIMARY KEY (`RelatieSoortId`),
  UNIQUE KEY `uq_relatiesoort` (`Naam`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

INSERT INTO `Relatie_Soort` (`Naam`) VALUES
  ('Partner'), ('Zoon'), ('Dochter'), ('Stiefzoon'), ('Stiefdochter'),
  ('Vader'), ('Moeder'), ('Contactpersoon noodgeval'), ('Overig')
ON DUPLICATE KEY UPDATE `Naam` = VALUES(`Naam`);

CREATE TABLE IF NOT EXISTS `Werknemer_Relatie` (
  `RelatieId`      INT NOT NULL AUTO_INCREMENT,
  `WerknemerId`    INT NOT NULL,
  `RelatieSoortId` INT NOT NULL,
  `Naam`           VARCHAR(120) NOT NULL,
  `Geboortedatum`  DATE         NULL,
  `Telefoon`       VARCHAR(40)  NOT NULL DEFAULT '',
  `Opmerking`      VARCHAR(255) NOT NULL DEFAULT '',
  `Noodcontact`    TINYINT(1)   NOT NULL DEFAULT 0,
  `Actief`         TINYINT(1)   NOT NULL DEFAULT 1,
  `GewijzigdOp`    TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`RelatieId`),
  KEY `ix_rel_werknemer` (`WerknemerId`),
  CONSTRAINT `fk_rel_werknemer` FOREIGN KEY (`WerknemerId`)
      REFERENCES `Werknemers` (`Werknemer-id`),
  CONSTRAINT `fk_rel_soort` FOREIGN KEY (`RelatieSoortId`)
      REFERENCES `Relatie_Soort` (`RelatieSoortId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;


/* ===========================================================================
   C. FILESTORE — bestaat al, geen nieuwe tabel nodig.

   De tabel `FileStore` gebruikt het patroon:
       MasterID   = het record waar het bestand bij hoort
       StoreType  = om wat voor soort record het gaat (2 = artikelen, ...)
   Meerdere bestanden aan één record zijn dus simpelweg meerdere regels met
   dezelfde MasterID en StoreType. Voor de werknemersmodule hoeven we alleen
   nieuwe StoreType-nummers af te spreken.

   LET OP: FileStore.StoreType heeft een foreign key naar de BESTAANDE tabel
   `filestoretype` (kolom FilestoreTypeID). Nieuwe soorten moeten daar eerst in,
   anders weigert de database het bestand.

   Kijk eerst wat er staat en welke nummers vrij zijn:
       SHOW COLUMNS FROM `filestoretype`;
       SELECT * FROM `filestoretype` ORDER BY `FilestoreTypeID`;

   Voeg daarna de twee nieuwe soorten toe (kolomnaam van de omschrijving
   aanpassen als die bij jullie anders heet):
       INSERT INTO `filestoretype` (`FilestoreTypeID`, `Omschrijving`) VALUES
         (300, 'Werknemer'),
         (301, 'Werknemer gebeurtenis');

   Zijn 300/301 al bezet? Kies dan andere nummers en pas ze aan in
   FilestoreService/Werknemers.razor (nu vastgelegd als 301).
   =========================================================================== */


/* ===========================================================================
   D1. View: huidige dienststatus per werknemer
   =========================================================================== */
CREATE OR REPLACE VIEW `vw_Werknemer_Status` AS
SELECT
    g.`WerknemerId`,
    g.`Moment`                                        AS `SindsMoment`,
    s.`Naam`                                          AS `LaatsteGebeurtenis`,
    CASE WHEN s.`WijzigtDienst` = 1 THEN 1 ELSE 0 END AS `InDienst`
FROM `Werknemer_Gebeurtenis` g
JOIN `Gebeurtenis_Soort` s ON s.`GebeurtenisSoortId` = g.`GebeurtenisSoortId`
WHERE s.`WijzigtDienst` <> 0
  AND g.`Moment` <= NOW()
  AND g.`GebeurtenisId` = (
        SELECT g2.`GebeurtenisId`
        FROM `Werknemer_Gebeurtenis` g2
        JOIN `Gebeurtenis_Soort` s2 ON s2.`GebeurtenisSoortId` = g2.`GebeurtenisSoortId`
        WHERE g2.`WerknemerId` = g.`WerknemerId`
          AND s2.`WijzigtDienst` <> 0
          AND g2.`Moment` <= NOW()
        ORDER BY g2.`Moment` DESC, g2.`GebeurtenisId` DESC
        LIMIT 1);


/* ===========================================================================
   D2. View: dienstverbanden als periodes (uit de gebeurtenissen afgeleid)
        Iemand die terugkomt levert meerdere regels op.
        Vereist MariaDB 10.2+ / MySQL 8+ (windowfuncties).
   =========================================================================== */
CREATE OR REPLACE VIEW `vw_Werknemer_Dienstverband` AS
SELECT `WerknemerId`, `Van`, `Tot`
FROM (
    SELECT
        g.`WerknemerId`,
        g.`Moment` AS `Van`,
        LEAD(g.`Moment`) OVER (PARTITION BY g.`WerknemerId`
                               ORDER BY g.`Moment`, g.`GebeurtenisId`) AS `Tot`,
        s.`WijzigtDienst`
    FROM `Werknemer_Gebeurtenis` g
    JOIN `Gebeurtenis_Soort` s ON s.`GebeurtenisSoortId` = g.`GebeurtenisSoortId`
    WHERE s.`WijzigtDienst` <> 0
) x
WHERE `WijzigtDienst` = 1;


/* ===========================================================================
   D3. View: tijdlijn voor het scherm (met aantal documenten)
   =========================================================================== */
CREATE OR REPLACE VIEW `vw_Werknemer_Tijdlijn` AS
SELECT
    g.`GebeurtenisId`,
    g.`WerknemerId`,
    g.`Moment`,
    s.`Naam`  AS `Soort`,
    s.`Groep`,
    g.`Toelichting`,
    g.`FunctieId`,
    g.`Contracturen`,
    g.`TotDatum`,
    g.`Prive`,
    (SELECT COUNT(*) FROM `FileStore` f
      WHERE f.`StoreType` = 301 AND f.`MasterID` = g.`GebeurtenisId`) AS `AantalDocumenten`
FROM `Werknemer_Gebeurtenis` g
JOIN `Gebeurtenis_Soort` s ON s.`GebeurtenisSoortId` = g.`GebeurtenisSoortId`;


/* ===========================================================================
   D4. View: verjaardagen (werknemers én hun relaties)
        Dagen tot de volgende verjaardag, zodat je makkelijk kunt sorteren.
   =========================================================================== */
CREATE OR REPLACE VIEW `vw_Verjaardagen` AS
SELECT 'Werknemer' AS `Wie`, w.`Werknemer-id` AS `WerknemerId`,
       w.`NaamVoluit` AS `Naam`, '' AS `Relatie`, w.`Geboortedatum`,
       TIMESTAMPDIFF(YEAR, w.`Geboortedatum`, CURDATE()) AS `Leeftijd`,
       DATEDIFF(
         DATE_ADD(w.`Geboortedatum`,
                  INTERVAL TIMESTAMPDIFF(YEAR, w.`Geboortedatum`, CURDATE())
                           + IF(DATE_FORMAT(w.`Geboortedatum`, '%m%d') < DATE_FORMAT(CURDATE(), '%m%d'), 1, 0) YEAR),
         CURDATE()) AS `DagenTot`
FROM `Werknemers` w
WHERE w.`Geboortedatum` IS NOT NULL
  AND (w.`Uit dienst` = 0 OR w.`Uit dienst` IS NULL)
UNION ALL
SELECT 'Relatie', r.`WerknemerId`, r.`Naam`, rs.`Naam`, r.`Geboortedatum`,
       TIMESTAMPDIFF(YEAR, r.`Geboortedatum`, CURDATE()),
       DATEDIFF(
         DATE_ADD(r.`Geboortedatum`,
                  INTERVAL TIMESTAMPDIFF(YEAR, r.`Geboortedatum`, CURDATE())
                           + IF(DATE_FORMAT(r.`Geboortedatum`, '%m%d') < DATE_FORMAT(CURDATE(), '%m%d'), 1, 0) YEAR),
         CURDATE())
FROM `Werknemer_Relatie` r
JOIN `Relatie_Soort` rs ON rs.`RelatieSoortId` = r.`RelatieSoortId`
WHERE r.`Geboortedatum` IS NOT NULL AND r.`Actief` <> 0;


/* ===========================================================================
   D5. View: jubilea (dienstjaren en huwelijksjaren)
   =========================================================================== */
CREATE OR REPLACE VIEW `vw_Jubilea` AS
SELECT
    g.`WerknemerId`,
    s.`Naam`                                              AS `Soort`,
    g.`Moment`                                            AS `Sinds`,
    TIMESTAMPDIFF(YEAR, g.`Moment`, CURDATE())            AS `Jaren`,
    DATE_ADD(DATE(g.`Moment`),
             INTERVAL TIMESTAMPDIFF(YEAR, g.`Moment`, CURDATE())
                      + IF(DATE_FORMAT(g.`Moment`, '%m%d') < DATE_FORMAT(CURDATE(), '%m%d'), 1, 0) YEAR)
                                                          AS `VolgendeDatum`
FROM `Werknemer_Gebeurtenis` g
JOIN `Gebeurtenis_Soort` s ON s.`GebeurtenisSoortId` = g.`GebeurtenisSoortId`
WHERE s.`Jubileum` <> 0;


/* ============================================================================
   OVERZETTEN VAN DE HUIDIGE GEGEVENS — controleer eerst de kolomnamen,
   en draai elk blok één keer.
   ----------------------------------------------------------------------------
   -- in dienst
   INSERT INTO `Werknemer_Gebeurtenis` (`WerknemerId`,`GebeurtenisSoortId`,`Moment`,`Toelichting`)
   SELECT w.`Werknemer-id`,
          (SELECT `GebeurtenisSoortId` FROM `Gebeurtenis_Soort` WHERE `Naam`='In dienst'),
          w.`Datum in-dienst`, 'Overgenomen uit Access'
   FROM `Werknemers` w WHERE w.`Datum in-dienst` IS NOT NULL;

   -- trouwdatum
   INSERT INTO `Werknemer_Gebeurtenis` (`WerknemerId`,`GebeurtenisSoortId`,`Moment`,`Prive`,`Toelichting`)
   SELECT w.`Werknemer-id`,
          (SELECT `GebeurtenisSoortId` FROM `Gebeurtenis_Soort` WHERE `Naam`='Gehuwd'),
          w.`Trouwdatum`, 1, 'Overgenomen uit Access'
   FROM `Werknemers` w WHERE w.`Trouwdatum` IS NOT NULL;
   ============================================================================ */


/* ============================================================================
   FILESTORE — AANSLUITEN OP DE BESTAANDE OPZET

   Documenten bij een gebeurtenis ophalen:
       SELECT * FROM `FileStore`
        WHERE `StoreType` = 301 AND `MasterID` = <GebeurtenisId>
        ORDER BY `DatumToegevoegd` DESC;

   Mapindeling zoals de VBA die nu maakt (per 1000 bestanden):
       map = LPAD(FLOOR(FileStoreID / 1000) + 1, 6, '0')
       dus FileStoreID 12345 -> map '000013'
   De app krijgt daar één functie voor, zodat de indeling op één plek vastligt
   en later te wijzigen is (bijv. twee niveaus, Vault-stijl) zonder bestaande
   bestanden te verplaatsen.

   Hoofdmap in appsettings.json, niet in de code:
       "Filestore": { "Pad": "\\\\server\\Filestore" }

   Aandachtspunten voor de overstap van Z: naar afgeschermd:
   - Een Windows-service kan niet bij een letterschijf (Z:) — die bestaat alleen
     per ingelogde gebruiker. Gebruik het volledige pad \\server\share.
   - Geef alleen het serviceaccount van de app rechten op die map en haal de
     share weg waar gebruikers nu bij kunnen. De app levert bestanden uit via
     een eigen adres en controleert daarbij de rechten (HRM/Directie voor
     vertrouwelijke StoreTypes).
   - Access blijft ondertussen werken zolang Access-gebruikers Z: nog hebben;
     je kunt dus per stap afschermen.
   ============================================================================ */
