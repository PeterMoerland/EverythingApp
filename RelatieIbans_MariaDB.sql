-- ===========================================================================
--  Bekende rekeningen van een relatie (wens gebruiker 11-11-2026).
--
--  Relations.IBAN heeft één rekening; een relatie betaalt soms van meer rekeningen (Weyers Holding betaalt de facturen
--  van Weyers GmbH, een klant met twee banken). Bij elke koppeling van een banktransactie aan een factuur (met de hand in
--  Banktransacties, of automatisch bij het verwerken van betalingen) komt de tegenrekening hier bij de relatie van de
--  factuur, als die er nog niet staat en niet al Relations.IBAN is. Banktransacties gebruikt ze bij de voorstellen
--  ("zelfde rekening") en toont de relatie achter de tegenrekening; Relaties toont ze in het blok Bekende rekeningen.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien. Het tweede deel vult de tabel uit de
--  bestaande koppelingen (11-11-2026 geteld: 669 combinaties relatie + rekening, waarvan 513 al Relations.IBAN zijn).
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `RelatieIbans` (
    `RelatieIbanID`  INT NOT NULL AUTO_INCREMENT,
    `RelatieID`      INT NOT NULL,                       -- Relations.id_supplier
    `IBAN`           VARCHAR(40) NOT NULL,               -- zonder spaties, hoofdletters
    `Bron`           VARCHAR(40) NOT NULL DEFAULT '',    -- 'handmatig koppelen', 'betalingscheck', 'bestaande koppeling'
    `AangemaaktOp`   DATETIME NOT NULL,
    `AangemaaktDoor` INT NULL,
    PRIMARY KEY (`RelatieIbanID`),
    UNIQUE KEY `uq_relatieiban` (`RelatieID`, `IBAN`),
    KEY `IX_RelatieIbans_IBAN` (`IBAN`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- uit de bestaande koppelingen (inkomende en uitgaande facturen)
INSERT IGNORE INTO `RelatieIbans` (`RelatieID`, `IBAN`, `Bron`, `AangemaaktOp`)
SELECT p.`rel`, p.`iban`, 'bestaande koppeling', NOW()
  FROM (
        SELECT DISTINCT f.`Bedrijf` AS `rel`, UPPER(REPLACE(t.`Tegenrekening`, ' ', '')) AS `iban`
          FROM `banktransacties_facturen_crossref` x
          JOIN `banktransacties` t ON t.`BankTransacties_ID` = x.`Banktransacties-id`
          JOIN `FacturenInkomend` f ON f.`FacturenInkomend-ID` = x.`Factureninkomend-id`
         WHERE COALESCE(t.`Tegenrekening`, '') <> '' AND COALESCE(f.`Bedrijf`, 0) <> 0
        UNION
        SELECT DISTINCT i.`Customer-id`, UPPER(REPLACE(t.`Tegenrekening`, ' ', ''))
          FROM `banktransacties_facturen_crossref` x
          JOIN `banktransacties` t ON t.`BankTransacties_ID` = x.`Banktransacties-id`
          JOIN `Invoices` i ON i.`Invoice-id` = x.`Facturenuitgaand-id`
         WHERE COALESCE(t.`Tegenrekening`, '') <> '' AND COALESCE(i.`Customer-id`, 0) <> 0
       ) p
  LEFT JOIN `Relations` r ON r.`id_supplier` = p.`rel`
 WHERE p.`iban` REGEXP '^[A-Z]{2}[0-9]{2}[A-Z0-9]{8,30}$'
   AND UPPER(REPLACE(COALESCE(r.`IBAN`, ''), ' ', '')) <> p.`iban`;
