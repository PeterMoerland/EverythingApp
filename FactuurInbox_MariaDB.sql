-- ===========================================================================
--  FactuurInbox: facturen en bonnetjes die nog beoordeeld moeten worden.
--
--  Twee bronnen:
--    - 'mail': de app leest op de achtergrond de postbus invoice@ (sectie FactuurInbox in appsettings);
--              per PDF/foto-bijlage een regel, een mail zonder factuur ook (met een opmerking).
--    - 'bon' : een collega fotografeert een bonnetje op de telefoon (MDE mobiel ▸ Bonnetje).
--  Financiën ▸ Inkomende facturen ▸ Te beoordelen: openen = het factuurvenster, al ingevuld; pas bij
--  Opslaan wordt het een echte factuur (FacturenInkomend). Zo komt er niets ongecontroleerd in de Basecone-lijst.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `FactuurInbox` (
    `FactuurInboxID`  INT NOT NULL AUTO_INCREMENT,
    `Bron`            VARCHAR(10) NOT NULL,          -- 'mail' of 'bon'
    `Ontvangen`       DATETIME NOT NULL,             -- mail: datum van de mail; bon: moment van scannen
    `DoorWerknemer`   INT NULL,                      -- bon: wie hem scande (Werknemers.Werknemer-id)
    `MailId`          VARCHAR(64) NULL,              -- Gmail-bericht-id; met BijlageNaam samen uniek
    `BijlageNaam`     VARCHAR(255) NULL,             -- mail: de bijlage; leeg = mail zonder factuur
    `MailVan`         VARCHAR(255) NULL,
    `MailOnderwerp`   VARCHAR(500) NULL,
    `MailTekst`       TEXT NULL,                     -- het begin van de mail, voor een mail zonder factuur
    `Soort`           VARCHAR(40) NULL,              -- bon: Brandstof, Maaltijd, Parkeren, Overig
    `WerkbonID`       INT NULL,                      -- bon: werkorder die de collega koos (mag leeg)
    `Opmerking`       VARCHAR(1000) NULL,            -- bon: wat de collega erbij schreef
    `Melding`         VARCHAR(500) NULL,             -- van de app zelf, bv. 'Geen factuur gevonden'
    `Gelezen`         MEDIUMTEXT NULL,               -- wat de AI las (JSON), zodat het venster meteen gevuld is
    `Status`          TINYINT NOT NULL DEFAULT 0,    -- 0 = te beoordelen, 1 = verwerkt, 2 = genegeerd
    `FactuurID`       INT NULL,                      -- verwerkt: de factuur die eruit ontstond
    `AfgehandeldOp`   DATETIME NULL,
    `AfgehandeldDoor` INT NULL,
    PRIMARY KEY (`FactuurInboxID`),
    UNIQUE KEY `UX_FactuurInbox_Mail` (`MailId`, `BijlageNaam`),
    KEY `IX_FactuurInbox_Status` (`Status`, `Ontvangen`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `FactuurInboxBestand` (
    `FactuurInboxBestandID` INT NOT NULL AUTO_INCREMENT,
    `FactuurInboxID`        INT NOT NULL,
    `Naam`                  VARCHAR(255) NOT NULL,
    `Inhoud`                LONGBLOB NOT NULL,       -- tot 15 MB (max_allowed_packet is standaard 16 MB)
    PRIMARY KEY (`FactuurInboxBestandID`),
    KEY `IX_FactuurInboxBestand_Inbox` (`FactuurInboxID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
