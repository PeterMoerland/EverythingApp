-- Sjablonen voor het offertedocument (20-11-2026, stap 3): een document dat je als begin voor een volgende offerte
-- bewaart, voor een BV of voor alle BV's. Een prijstabel in een sjabloon toont altijd de hele calculatie (een
-- verwijzing naar een deel geldt alleen binnen de calculatie waar hij uit komt).
--
-- Zelf draaien op de database van de app, na OfferteDocumenten_MariaDB.sql. Meerdere keren draaien kan geen kwaad.

CREATE TABLE IF NOT EXISTS `OfferteSjablonen` (
  `SjabloonID`     INT          NOT NULL AUTO_INCREMENT,
  `Naam`           VARCHAR(100) NOT NULL,
  `BvID`           INT          NULL,                    -- de BV (Projects.Werkmaatschappij); NULL = voor alle BV's
  `Data`           LONGTEXT     NOT NULL,                -- de blokken als JSON
  `AangemaaktOp`   DATETIME     NOT NULL,
  `AangemaaktDoor` INT          NULL,
  `GewijzigdOp`    DATETIME     NULL,
  `GewijzigdDoor`  INT          NULL,
  PRIMARY KEY (`SjabloonID`),
  KEY `IX_OfferteSjablonen_Bv` (`BvID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
