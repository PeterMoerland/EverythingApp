-- Stuklijst per producttype: welke halffabricaten (ProductNames) er in een product gaan
-- en hoeveel. Eigen zijtabel; de Access-tabellen ProductNames/Producten blijven ongemoeid.
-- De werkelijke koppeling per exemplaar blijft Producten.ParentProductenID (zoals in Access);
-- deze tabel zegt alleen wat er verwacht wordt, zodat de app bij het samenstellen
-- uitsluitend de juiste halffabricaten aanbiedt en bijhoudt wat er nog ontbreekt.
--
-- Beheer: Producten ▸ producttype ▸ tab Samenstelling.
-- Gebruik: Producten ▸ exemplaar ▸ Samenstelling en Productie ▸ Producten ▸ stap Samenstelling.

CREATE TABLE IF NOT EXISTS `ProductSamenstelling` (
    `SamenstellingID`  INT          NOT NULL AUTO_INCREMENT,
    `ProductNameID`    INT          NOT NULL COMMENT 'het product (ProductNames)',
    `OnderdeelNameID`  INT          NOT NULL COMMENT 'het halffabricaat dat erin gaat (ProductNames)',
    `Aantal`           INT          NOT NULL DEFAULT 1,
    `Volgorde`         INT          NOT NULL DEFAULT 0,
    `Opmerking`        VARCHAR(200) NULL,
    `AddedBy`          INT          NULL,
    `AddedAt`          DATETIME     NULL,
    PRIMARY KEY (`SamenstellingID`),
    UNIQUE KEY `uq_samenstelling` (`ProductNameID`, `OnderdeelNameID`),
    KEY `ix_samenstelling_onderdeel` (`OnderdeelNameID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
