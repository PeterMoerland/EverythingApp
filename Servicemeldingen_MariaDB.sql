-- Servicemeldingen (verzoek van Michiel, 11-11-2026): storingen en verzoeken die via Service ▸ "wie werkte eraan?"
-- worden doorgezet. Eén regel per melding: wie belde, wanneer, terugbelnummer, omschrijving, naar wie hij gemaild is en
-- of hij is afgehandeld. Zonder deze tabel werkt mailen gewoon; alleen opslaan en de lijst ontbreken dan.
-- Mag vaker gedraaid worden.

CREATE TABLE IF NOT EXISTS `Servicemeldingen` (
    `ServicemeldingID` INT NOT NULL AUTO_INCREMENT,
    `ProjectID`        INT NULL,                       -- Projects.Project-Id
    `KlantID`          INT NULL,                       -- Relations.id_supplier
    `Beller`           VARCHAR(150) NOT NULL DEFAULT '',
    `Telefoon`         VARCHAR(50)  NOT NULL DEFAULT '',
    `GebeldOp`         DATETIME NULL,
    `Omschrijving`     TEXT NULL,
    `Aan`              VARCHAR(500) NOT NULL DEFAULT '',  -- namen van wie de mail kreeg
    `Gemaild`          TINYINT(1) NOT NULL DEFAULT 0,
    `AangemaaktOp`     DATETIME NOT NULL,
    `AangemaaktDoor`   INT NULL,                       -- Werknemers.Werknemer-id
    `Afgehandeld`      TINYINT(1) NOT NULL DEFAULT 0,
    `AfgehandeldOp`    DATETIME NULL,
    `AfgehandeldDoor`  INT NULL,
    PRIMARY KEY (`ServicemeldingID`),
    KEY `IX_Servicemeldingen_Project` (`ProjectID`),
    KEY `IX_Servicemeldingen_Open` (`Afgehandeld`, `AangemaaktOp`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
