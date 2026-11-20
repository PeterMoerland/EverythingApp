-- ===========================================================================
--  Solar-prijzen bijwerken (Artikelen ▸ Solar-prijzen, en de wekelijkse taak).
--
--  SolarArtikelBasis : per regel van ArtikelSupplier of de Solar-prijs per stuk
--                      (0) of per verpakking/VE (1) geldt. Alleen gevuld voor
--                      regels waar iemand dat zelf heeft vastgelegd; anders
--                      kiest de app wat het dichtst bij onze prijs ligt.
--  SolarPrijsLog     : elke prijs die is overgenomen, met de oude en de nieuwe
--                      waarde, door wie (leeg = de wekelijkse taak) en wanneer.
--  SolarPrijsRuns    : wanneer de wekelijkse taak draaide en wat hij deed.
--
--  Zolang deze tabellen ontbreken kan er niets worden overgenomen en draait de
--  wekelijkse taak niet; het overzicht zelf (alleen lezen) werkt wel.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `SolarArtikelBasis` (
    `ArtikelSupplierID` INT NOT NULL,                  -- ArtikelSupplier.ArtikelSupplierID
    `PerVE`             TINYINT(1) NOT NULL,           -- 1 = Solar-prijs geldt per VE, 0 = per stuk
    `GezetDoor`         INT NULL,                      -- Werknemers.Werknemer-id
    `last_updated`      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (`ArtikelSupplierID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `SolarPrijsLog` (
    `LogID`             INT NOT NULL AUTO_INCREMENT,
    `ArtikelSupplierID` INT NOT NULL,
    `ArtikelID`         INT NOT NULL,
    `Op`                DATETIME NOT NULL,
    `Door`              INT NULL,                      -- Werknemers.Werknemer-id; leeg = de wekelijkse taak
    `Bron`              VARCHAR(10) NOT NULL,          -- 'auto' of 'hand'
    `OudPrijsPerVE`     DECIMAL(18,4) NULL,
    `OudKorting`        DECIMAL(12,6) NULL,
    `NieuwPrijsPerVE`   DECIMAL(18,4) NOT NULL,
    `NieuwKorting`      DECIMAL(12,6) NOT NULL,
    `SolarBruto`        DECIMAL(18,6) NULL,            -- per Solar-eenheid
    `SolarNetto`        DECIMAL(18,6) NULL,
    `PerVE`             TINYINT(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (`LogID`),
    KEY `IX_SolarPrijsLog_Op` (`Op`),
    KEY `IX_SolarPrijsLog_Artikel` (`ArtikelID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `SolarPrijsRuns` (
    `RunID`       INT NOT NULL AUTO_INCREMENT,
    `Op`          DATETIME NOT NULL,
    `Bijgewerkt`  INT NOT NULL DEFAULT 0,
    `NaTeKijken`  INT NOT NULL DEFAULT 0,
    `ZonderPrijs` INT NOT NULL DEFAULT 0,
    `Fout`        VARCHAR(500) NULL,
    PRIMARY KEY (`RunID`),
    KEY `IX_SolarPrijsRuns_Op` (`Op`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
