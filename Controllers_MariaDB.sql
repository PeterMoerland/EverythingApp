-- Controllertypen breder dan EtherCAT-drives: ook Feetech-servo's (en later CANopen).
--
-- DriveTypen.Protocol zegt hoe de agent met het type praat:
--   ethercat  Ingenia/Novanta drives (pysoem, ingeniamotion) — productcode = EtherCAT-productcode
--   feetech   Feetech STS/SMS-servo's over de USB-TTL-bus — productcode = modelnaam (STS3215)
--   canopen   later
--
-- Parametersets (DriveParameters) blijven per product × positie × firmwareversie;
-- bij Feetech is het bestand een JSON (zie MdeAgent/voorbeelden/feetech-sts3215.json).

ALTER TABLE `DriveTypen`
    ADD COLUMN IF NOT EXISTS `Protocol` VARCHAR(12) NOT NULL DEFAULT 'ethercat'
        COMMENT 'ethercat | feetech | canopen' AFTER `ProductCode`;

INSERT IGNORE INTO `DriveTypen` (`ProductCode`, `Protocol`, `Naam`, `Fabrikant`, `AddedAt`)
VALUES ('STS3215', 'feetech', 'Feetech STS3215', 'Feetech', NOW());

-- Chips: ook een 'controllertype', zodat firmware (.hex/.bin) per type met status
-- (actueel/toegestaan/geblokkeerd) in de tabel staat en in Half-fabricaten ▸ stap 4
-- gekozen kan worden. Productcode = esptool-chip resp. pyocd-target ('auto' / leeg = herkennen).
--   esp32   ESP32-familie via USB-UART (esptool); firmware .hex (adressen erin) of samengevoegde .bin
--   stm32   STM32 via ST-LINK (STM32CubeProgrammer of pyocd); firmware .hex/.elf/.bin
INSERT IGNORE INTO `DriveTypen` (`ProductCode`, `Protocol`, `Naam`, `Fabrikant`, `AddedAt`)
VALUES ('auto',  'esp32', 'ESP32 (alle varianten)', 'Espressif', NOW()),
       ('stm32', 'stm32', 'STM32 (alle families)',  'ST',        NOW());

-- ZeroErr eRob-actuatoren: CiA402 over EtherCAT (protocol 'coe'). Productcode uit het
-- ESI-bestand 'ZeroErr Driver_V3.2.0.xml'; alle eRob-modellen melden dezelfde code.
-- Parameterset = JSON met SDO's (sjabloon: MdeAgent\voorbeelden\erob110.json); geen firmware/dictionary.
INSERT IGNORE INTO `DriveTypen` (`ProductCode`, `Protocol`, `Naam`, `Fabrikant`, `AddedAt`)
VALUES ('0x00029252', 'coe', 'ZeroErr eRob (110/90/80/70)', 'ZeroErr', NOW());

-- Techniek 'feetech' op een motorproduct: zet hier de juiste ProductName in.
-- INSERT IGNORE INTO `ProductTechnieken` (`ProductNameID`, `Techniek`)
-- SELECT `ProductNameID`, 'feetech' FROM `ProductNames` WHERE `ProductName` = 'MOTOR-STS3215';
