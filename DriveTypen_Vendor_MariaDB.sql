-- Controllertypen herkennen op fabrikantcode + productcode (EtherCAT identity 0x1018:1 en :2).
--
-- Tot nu toe was de productcode alleen de sleutel. Met eigen MDE-slaves (vendor 0x000010E3, MDE is
-- EtherCAT-partner) naast Ingenia/Novanta en ZeroErr kan dezelfde productcode bij twee fabrikanten
-- voorkomen; daarom wordt de combinatie uniek. Een leeg VendorID betekent "niet gecontroleerd":
-- zo'n type wordt nog op productcode alleen herkend, en Test PCB laat zien welke fabrikantcode de
-- slave meldt, zodat je hem bij Controllertypen kunt invullen.

ALTER TABLE `DriveTypen`
    ADD COLUMN IF NOT EXISTS `VendorID` VARCHAR(12) NOT NULL DEFAULT ''
        COMMENT 'EtherCAT Vendor ID als hex, bv. 0x000010E3; leeg = niet gecontroleerd' AFTER `ProductCode`;

ALTER TABLE `DriveTypen` DROP INDEX IF EXISTS `uq_drivetype_code`;
ALTER TABLE `DriveTypen` ADD UNIQUE KEY IF NOT EXISTS `uq_drivetype_vendor_code` (`VendorID`, `ProductCode`);

-- Bekende fabrikanten. Ingenia/Novanta bewust leeg gelaten: vul in wat de testrig bij de eerste
-- uitleesbeurt meldt (Test PCB toont "fabrikantcode 0x... nog niet vastgelegd").
UPDATE `DriveTypen` SET `VendorID` = '0x000010E3' WHERE `VendorID` = '' AND `ProductCode` = '0x00055955';   -- MDE M-Pede-WJ1
UPDATE `DriveTypen` SET `VendorID` = '0x5A65726F' WHERE `VendorID` = '' AND `ProductCode` = '0x00029252';   -- ZeroErr eRob
