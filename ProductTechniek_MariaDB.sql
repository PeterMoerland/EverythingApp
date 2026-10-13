-- Productie ▸ Test PCB: wat zit er op een halffabricaat, en is een historieregel
-- geslaagd of mislukt.
--
-- 1. ProductTechnieken: per halffabricaat welke techniek erop zit. Test PCB
--    toont in stap 4 alleen de bijbehorende knoppen.
--      drives  EtherCAT-drives (Ingenia/Novanta Everest, Capitan)
--      esp32   ESP32 (programmeren via USB-UART)
--      stm32   STM32 (programmeren via ST-LINK)
--
-- 2. ProductHistorie.Uitkomst: 1 = geslaagd, 0 = mislukt, NULL = neutraal (gewone
--    gebeurtenis). De app kleurt de historie ermee. Bestaande regels blijven NULL.

CREATE TABLE IF NOT EXISTS `ProductTechnieken` (
    `ProductTechniekID` INT         NOT NULL AUTO_INCREMENT,
    `ProductNameID`     INT         NOT NULL,
    `Techniek`          VARCHAR(20) NOT NULL COMMENT 'drives | esp32 | stm32',
    `Opmerking`         VARCHAR(200) NULL,
    PRIMARY KEY (`ProductTechniekID`),
    UNIQUE KEY `uq_product_techniek` (`ProductNameID`, `Techniek`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

ALTER TABLE `ProductHistorie`
    ADD COLUMN IF NOT EXISTS `Uitkomst` TINYINT NULL DEFAULT NULL
        COMMENT '1 = geslaagd, 0 = mislukt, NULL = neutraal';

-- De RZ-print heeft drives; alvast invullen (past aan naar jullie situatie).
INSERT IGNORE INTO `ProductTechnieken` (`ProductNameID`, `Techniek`)
SELECT `ProductNameID`, 'drives' FROM `ProductNames` WHERE `ProductName` = 'MPR-PCBA-RZ';
