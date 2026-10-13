-- Drives (Everest, Capitan, …) op de prints: typen, firmwareversies en de
-- parametersets per halffabricaat. Gebruikt door Productie ▸ Test PCB en het
-- beheerscherm Productie ▸ Drivetypen.
--
--   DriveTypen        één regel per type, herkend aan de EtherCAT-productcode
--   DriveFirmwares    per type meerdere versies, elk met status en bestanden
--                     (firmware .lfu/.sfu en dictionary .xdf, in de FileStore)
--   DriveParameters   parameterset (.xcf) voor halffabricaat × drive-positie ×
--                     firmwareversie — elke firmware heeft zijn eigen set
--   DrivePosities     wat er per halffabricaat op welke positie verwacht wordt
--
-- Bestanden staan in de FileStore met StoreType 320 (firmware), 321 (dictionary),
-- 322 (parameterset), 323 (ESI); MasterID = het DriveFirmwareID resp. DriveParameterID.
-- Deze vier moeten ook in FileStoreType staan (foreign key).

CREATE TABLE IF NOT EXISTS `DriveTypen` (
    `DriveTypeID`   INT          NOT NULL AUTO_INCREMENT,
    `ProductCode`   VARCHAR(12)  NOT NULL COMMENT 'EtherCAT-productcode als hex, bv. 0x03B11001',
    `Naam`          VARCHAR(60)  NOT NULL COMMENT 'bv. Everest EVS-NET-E',
    `Fabrikant`     VARCHAR(60)  NULL,
    `Opmerking`     VARCHAR(500) NULL,
    `AddedBy`       INT          NULL,
    `AddedAt`       DATETIME     NULL,
    PRIMARY KEY (`DriveTypeID`),
    UNIQUE KEY `uq_drivetype_code` (`ProductCode`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `DriveFirmwares` (
    `DriveFirmwareID`   INT          NOT NULL AUTO_INCREMENT,
    `DriveTypeID`       INT          NOT NULL,
    `Versie`            VARCHAR(30)  NOT NULL COMMENT 'zoals de drive hem meldt (0x26E4), bv. 2.10.0.000',
    `Status`            VARCHAR(12)  NOT NULL DEFAULT 'toegestaan' COMMENT 'actueel | toegestaan | geblokkeerd',
    `FirmwareFileID`    INT          NULL COMMENT 'FileStoreID van het .lfu/.sfu-bestand',
    `DictionaryFileID`  INT          NULL COMMENT 'FileStoreID van de .xdf-dictionary',
    `EsiFileID`         INT          NULL COMMENT 'FileStoreID van het ESI-bestand (EtherCAT Slave Information, .xml)',
    `Vrijgegeven`       DATE         NULL,
    `Opmerking`         VARCHAR(500) NULL,
    `StatusDoor`        INT          NULL,
    `StatusOp`          DATETIME     NULL,
    `AddedBy`           INT          NULL,
    `AddedAt`           DATETIME     NULL,
    PRIMARY KEY (`DriveFirmwareID`),
    UNIQUE KEY `uq_firmware_versie` (`DriveTypeID`, `Versie`),
    CONSTRAINT `fk_firmware_type` FOREIGN KEY (`DriveTypeID`) REFERENCES `DriveTypen` (`DriveTypeID`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `DrivePosities` (
    `DrivePositieID`  INT          NOT NULL AUTO_INCREMENT,
    `ProductNameID`   INT          NOT NULL COMMENT 'het halffabricaat',
    `Positie`         INT          NOT NULL COMMENT '1 = eerste drive op de EtherCAT-bus',
    `Naam`            VARCHAR(40)  NULL COMMENT 'bv. R-as, Capitan A',
    `DriveTypeID`     INT          NULL COMMENT 'verwacht type; leeg = niet controleren',
    PRIMARY KEY (`DrivePositieID`),
    UNIQUE KEY `uq_positie` (`ProductNameID`, `Positie`),
    CONSTRAINT `fk_positie_type` FOREIGN KEY (`DriveTypeID`) REFERENCES `DriveTypen` (`DriveTypeID`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `DriveParameters` (
    `DriveParameterID`  INT          NOT NULL AUTO_INCREMENT,
    `ProductNameID`     INT          NOT NULL COMMENT 'het halffabricaat',
    `Positie`           INT          NOT NULL COMMENT 'drive-positie, zie DrivePosities',
    `DriveFirmwareID`   INT          NOT NULL COMMENT 'de firmwareversie waar deze set bij hoort',
    `Omschrijving`      VARCHAR(80)  NULL COMMENT 'bv. R_drive A v0.0.2',
    `ParameterFileID`   INT          NULL COMMENT 'FileStoreID van het .xcf-bestand',
    `Status`            VARCHAR(12)  NOT NULL DEFAULT 'actueel' COMMENT 'actueel | geblokkeerd',
    `Opmerking`         VARCHAR(500) NULL,
    `AddedBy`           INT          NULL,
    `AddedAt`           DATETIME     NULL,
    PRIMARY KEY (`DriveParameterID`),
    KEY `ix_param_product` (`ProductNameID`, `Positie`),
    CONSTRAINT `fk_param_firmware` FOREIGN KEY (`DriveFirmwareID`) REFERENCES `DriveFirmwares` (`DriveFirmwareID`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Bestond de tabel al zonder ESI-kolom (eerdere versie van dit script):
ALTER TABLE `DriveFirmwares`
    ADD COLUMN IF NOT EXISTS `EsiFileID` INT NULL
        COMMENT 'FileStoreID van het ESI-bestand (EtherCAT Slave Information, .xml)'
        AFTER `DictionaryFileID`;

-- De typen die we al kennen (productcode = <Device ProductCode="…"> in het .xcf, als hex).
INSERT IGNORE INTO `DriveTypen` (`ProductCode`, `Naam`, `Fabrikant`, `AddedAt`) VALUES
    ('0x03B11001', 'Everest EVS-NET-E', 'Novanta / Ingenia', NOW()),
    ('0x03711001', 'Capitan CAP-NET-E', 'Novanta / Ingenia', NOW());
