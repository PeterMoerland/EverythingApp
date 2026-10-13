-- Opbouwvolgorde van een product: de stappen per producttype (Mechanische opbouw,
-- Elektrische opbouw, Parameters inschieten, Testen …) en per exemplaar welke
-- stappen afgevinkt zijn. Elke wijziging van een vinkje komt ook in ProductHistorie.
--
-- Documenten met hands-on uitleg per stap staan in de FileStore met StoreType 330
-- (MasterID = OpbouwStapID). Voeg 330 toe aan FileStoreType (foreign key!):
--   INSERT INTO `FileStoreType` (`FilestoreTypeID`, `Omschrijving`) VALUES (330, 'Opbouwstap');

CREATE TABLE IF NOT EXISTS `ProductOpbouwStappen` (
    `OpbouwStapID`  INT          NOT NULL AUTO_INCREMENT,
    `ProductNameID` INT          NOT NULL,
    `Volgorde`      INT          NOT NULL DEFAULT 0,
    `Naam`          VARCHAR(80)  NOT NULL,
    `Omschrijving`  VARCHAR(500) NULL,
    `Verplicht`     TINYINT      NOT NULL DEFAULT 1,
    `AddedBy`       INT          NULL,
    `AddedAt`       DATETIME     NULL,
    PRIMARY KEY (`OpbouwStapID`),
    KEY `ix_opbouw_product` (`ProductNameID`, `Volgorde`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Soort stap: 'opbouw' (de bouwvolgorde) of 'controle' (de handmatige eindcontrole).
-- Zijn alle verplichte opbouwstappen en alle controles afgevinkt, dan kan het
-- exemplaar op status 'Gereed' (knop Product gereed; statusnaam in appsettings
-- Productie:GereedStatus, standaard 'Gereed').
ALTER TABLE `ProductOpbouwStappen`
    ADD COLUMN IF NOT EXISTS `Soort` VARCHAR(12) NOT NULL DEFAULT 'opbouw' COMMENT 'opbouw | controle' AFTER `Volgorde`;

CREATE TABLE IF NOT EXISTS `ProductOpbouwStatus` (
    `ProductID`     INT      NOT NULL COMMENT 'het exemplaar (Producten.ProductenID)',
    `OpbouwStapID`  INT      NOT NULL,
    `Klaar`         TINYINT  NOT NULL DEFAULT 0,
    `KlaarOp`       DATETIME NULL,
    `KlaarDoor`     INT      NULL,
    `Opmerking`     VARCHAR(300) NULL,
    PRIMARY KEY (`ProductID`, `OpbouwStapID`),
    CONSTRAINT `fk_opbouwstatus_stap` FOREIGN KEY (`OpbouwStapID`) REFERENCES `ProductOpbouwStappen` (`OpbouwStapID`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
