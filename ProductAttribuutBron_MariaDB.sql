-- Attributen automatisch laten vullen vanuit Productie ▸ Test PCB.
--
-- 'Bron' op het standaardattribuut van een producttype zegt waar de waarde
-- vandaan komt, los van de (vrije) naam van het attribuut:
--   drive:1:serienummer   serienummer van de 1e EtherCAT-drive (slavepositie 0)
--   drive:2:serienummer   idem 2e drive, enzovoort
--   drive:1:type          naam/productcode van die drive (Everest, Capitan)
--   esp32:mac             MAC-adres van de ESP32 na het flashen
--   stm32:uid             96-bits UID van de STM32 na het flashen
--   firmware:versie       bestandsnaam van de geladen firmware
-- Leeg/NULL = handmatig in te vullen, zoals altijd.
--
-- De attributen van een exemplaar (ProductAttributes) verwijzen al met
-- ProductNameAttributeID naar het standaardattribuut; daar hoeft niets bij.

ALTER TABLE `ProductNameAttributes`
    ADD COLUMN IF NOT EXISTS `Bron` VARCHAR(40) NULL DEFAULT NULL
        COMMENT 'Automatische bron (drive:1:serienummer, esp32:mac, ...); leeg = handmatig'
        AFTER `VraagBijAanmaak`;
