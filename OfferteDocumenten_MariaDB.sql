-- Offertedocument (20-11-2026): een vrij op te maken offerte uit blokken (tekst, kop, plaatje, prijstabel uit de
-- calculatie, voorwaarden, ondertekening). Per offerte een document. De prijstabellen bewaren alleen een verwijzing
-- naar (een deel van) de calculatie; bij vastleggen komt de opmaak met de cijfers van dat moment in Vast.
--
-- Zelf draaien op de database van de app. Meerdere keren draaien kan geen kwaad.

CREATE TABLE IF NOT EXISTS `OfferteDocumenten` (
  `DocumentID`     INT          NOT NULL AUTO_INCREMENT,
  `OfferteID`      INT          NOT NULL,
  `Status`         TINYINT      NOT NULL DEFAULT 0,      -- 0 = concept (gekoppeld aan de calculatie), 1 = vastgelegd
  `Versie`         INT          NOT NULL DEFAULT 1,      -- telt op bij elke opslag (tegen elkaar overschrijven)
  `Data`           LONGTEXT     NOT NULL,                -- de blokken als JSON
  `Vast`           LONGTEXT     NULL,                    -- bij vastleggen: de opmaak met de cijfers van dat moment (JSON)
  `VastTotaal`     DECIMAL(14,2) NULL,                   -- bij vastleggen: het totaal van dat moment = het offertebedrag
  `AangemaaktOp`   DATETIME     NOT NULL,
  `AangemaaktDoor` INT          NULL,
  `GewijzigdOp`    DATETIME     NULL,
  `GewijzigdDoor`  INT          NULL,
  `VastOp`         DATETIME     NULL,
  `VastDoor`       INT          NULL,
  PRIMARY KEY (`DocumentID`),
  UNIQUE KEY `UX_OfferteDocumenten_Offerte` (`OfferteID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Later toegevoegd (20-11-2026): het totaal bij vastleggen. Voor wie het script al eerder draaide.
ALTER TABLE `OfferteDocumenten` ADD COLUMN IF NOT EXISTS `VastTotaal` DECIMAL(14,2) NULL AFTER `Vast`;
