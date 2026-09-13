-- ============================================================
--  Aliassen van leveranciersnamen
--
--  Waarom: op een factuur staat niet altijd de naam waaronder een leverancier
--  bij ons in de boeken staat. Bedrijven veranderen van naam, gebruiken een
--  handelsnaam, of factureren via een moedermaatschappij. Voorbeeld: op de
--  factuur staat "GeT Components Group B.V." terwijl wij hem kennen als
--  "VA-Imaging (get)".
--
--  Deze tabel legt dat verband vast. Kies je bij het uitlezen van een factuur
--  de juiste leverancier, dan vraagt de app of hij het mag onthouden — en de
--  volgende factuur van dat bedrijf wordt meteen goed gekoppeld, zonder dat er
--  een model aan te pas komt.
--
--  Uitvoeren op de MDE-database. Bestaat de tabel al, dan doet dit script niets.
-- ============================================================

CREATE TABLE IF NOT EXISTS `LeverancierAlias` (
  `LeverancierAliasID` int(10) NOT NULL AUTO_INCREMENT,

  -- De naam zoals hij op de factuur stond; alleen om te laten zien.
  `Naam`               varchar(200) NOT NULL,

  -- Dezelfde naam, maar opgeschoond: kleine letters, zonder punten, spaties en
  -- rechtsvormen. Hierop wordt gezocht, zodat "GeT Components Group B.V." en
  -- "get components group bv" hetzelfde zijn.
  `Sleutel`            varchar(200) NOT NULL,

  -- De leverancier in Relations (id_supplier).
  `RelatieID`          int(10) NOT NULL,

  `ToegevoegdOp`       datetime DEFAULT NULL,
  `ToegevoegdDoor`     int(10) DEFAULT NULL,

  -- Hoe vaak deze alias van pas kwam; handig om te zien wat er echt wordt gebruikt.
  `Gebruikt`           int(10) NOT NULL DEFAULT 0,

  `last_updated`       timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP
                       ON UPDATE CURRENT_TIMESTAMP,

  PRIMARY KEY (`LeverancierAliasID`),

  -- Eén naam kan maar naar één leverancier wijzen; anders weet de app het niet.
  UNIQUE KEY `UQ_LeverancierAlias_Sleutel` (`Sleutel`),

  KEY `IX_LeverancierAlias_Relatie` (`RelatieID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Een verwijzing naar Relations kan alleen als de kolomtypes overeenkomen; bij
-- twijfel laat je dit weg, de app werkt ook zonder.
-- ALTER TABLE `LeverancierAlias`
--   ADD CONSTRAINT `FK_LeverancierAlias_Relations`
--   FOREIGN KEY (`RelatieID`) REFERENCES `Relations` (`id_supplier`);
