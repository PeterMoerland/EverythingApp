-- ===========================================================================
--  Projectfoto's (wens gebruiker 12-11-2026): op de telefoon (MDE mobiel ▸ Foto ▸ Projectfoto's) een werkorder of
--  project kiezen, een onderwerp ingeven en foto's maken. Ze staan daarna bij het project in de grote app
--  (Projecten ▸ project ▸ tab Foto's), per onderwerp.
--
--  De foto zelf staat in de FileStore (StoreType 370, MasterID = ProjectFotoID); deze tabel houdt bij bij welk project
--  (en eventueel welke werkorder) hij hoort, het onderwerp, een notitie en een klein voorbeeld voor het overzicht.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `ProjectFotos` (
    `ProjectFotoID`  INT NOT NULL AUTO_INCREMENT,
    `ProjectID`      INT NOT NULL,              -- Projects.Project-Id
    `WerkbonID`      INT NULL,                  -- Workorders.Werkbon-id, als de foto bij een werkorder is gemaakt
    `Onderwerp`      VARCHAR(100) NOT NULL,     -- bv. 'Montage bij klant'; het overzicht groepeert hierop
    `Notitie`        VARCHAR(500) NULL,         -- per foto, optioneel
    `FileStoreID`    INT NULL,                  -- de foto zelf in de FileStore
    `Miniatuur`      MEDIUMBLOB NULL,           -- klein voorbeeld (jpeg, enkele tientallen kB)
    `GemaaktOp`      DATETIME NOT NULL,
    `GemaaktDoor`    INT NULL,                  -- Werknemers.Werknemer-id
    PRIMARY KEY (`ProjectFotoID`),
    KEY `IX_ProjectFotos_Project` (`ProjectID`, `GemaaktOp`),
    KEY `IX_ProjectFotos_Werkbon` (`WerkbonID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- De foto's zelf (FileStore.StoreType heeft een foreign key naar FileStoreType)
INSERT INTO `FileStoreType` (`FilestoreTypeID`, `FileStoreTypeDescription`)
SELECT 370, 'Projectfoto' FROM DUAL
 WHERE NOT EXISTS (SELECT 1 FROM `FileStoreType` WHERE `FilestoreTypeID` = 370);
