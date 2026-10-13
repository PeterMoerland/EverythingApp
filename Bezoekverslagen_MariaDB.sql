-- ===========================================================================
--  Bezoekverslagen: een verslag van elk contactmoment met een relatie
--  (bij de klant, een leverancier bij MDE, online, beurs). Met wie er bij waren
--  (collega's en contactpersonen), actiepunten, en foto's/bijlagen in de
--  filestore (soort 330). Scherm: Relaties ▸ Bezoekverslagen.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `Bezoekverslagen` (
    `BezoekID`       INT NOT NULL AUTO_INCREMENT,
    `Datum`          DATETIME NOT NULL,
    `Titel`          VARCHAR(150) NOT NULL,
    `Soort`          VARCHAR(30) NOT NULL DEFAULT 'Bij relatie',  -- Bij relatie / Bij MDE / Online / Telefonisch / Beurs of evenement
    `RelatieID`      INT NULL,                                    -- Relations.id_supplier
    `ProjectID`      INT NULL,                                    -- Projects.Project-Id
    `Locatie`        VARCHAR(150) NULL,
    `Verslag`        LONGTEXT NULL,
    `Vervolg`        DATE NULL,                                   -- volgend contact
    `Vertrouwelijk`  TINYINT(1) NOT NULL DEFAULT 0,               -- alleen deelnemers, maker en Directie
    `AangemaaktDoor` INT NULL,                                    -- Werknemers.Werknemer-id
    `AangemaaktOp`   DATETIME NULL,
    `GewijzigdDoor`  INT NULL,
    `last_updated`   TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (`BezoekID`),
    KEY `IX_Bezoek_Relatie` (`RelatieID`),
    KEY `IX_Bezoek_Project` (`ProjectID`),
    KEY `IX_Bezoek_Datum` (`Datum`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Wie er bij was: een collega (WerknemerID), een contactpersoon van de relatie (ContactID)
-- of iemand anders (alleen Naam).
CREATE TABLE IF NOT EXISTS `BezoekDeelnemers` (
    `ID`           INT NOT NULL AUTO_INCREMENT,
    `BezoekID`     INT NOT NULL,
    `WerknemerID`  INT NULL,
    `ContactID`    INT NULL,                                      -- Contacts.ID_contact
    `Naam`         VARCHAR(100) NULL,
    `last_updated` TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (`ID`),
    KEY `IX_BezoekDeelnemer_Werknemer` (`WerknemerID`),
    CONSTRAINT `FK_BezoekDeelnemers_Bezoek` FOREIGN KEY (`BezoekID`)
        REFERENCES `Bezoekverslagen` (`BezoekID`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `BezoekActies` (
    `ID`           INT NOT NULL AUTO_INCREMENT,
    `BezoekID`     INT NOT NULL,
    `Omschrijving` VARCHAR(250) NOT NULL,
    `WerknemerID`  INT NULL,                                      -- wie pakt het op
    `Deadline`     DATE NULL,
    `Gereed`       TINYINT(1) NOT NULL DEFAULT 0,
    `GereedOp`     DATETIME NULL,
    `last_updated` TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (`ID`),
    KEY `IX_BezoekActie_Werknemer` (`WerknemerID`, `Gereed`),
    CONSTRAINT `FK_BezoekActies_Bezoek` FOREIGN KEY (`BezoekID`)
        REFERENCES `Bezoekverslagen` (`BezoekID`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Filestore-soort voor foto's en bijlagen (MasterID = BezoekID)
INSERT IGNORE INTO `FileStoreType` (`FilestoreTypeID`, `FileStoreTypeDescription`) VALUES (330, 'Bezoekverslag');
