-- ===========================================================================
--  Broodjes bestellen bij het Kaeswienkeltje (Tholen): 🥖 in de bovenbalk op donderdag en vrijdag, pagina /broodjes.
--  Assortiment komt van hun webshop (WooCommerce Store API) en staat hier, met eigen vinkjes; de vrijdaglijst sluit
--  vrijdag 11:00; daarnaast losse lijsten voor bijvoorbeeld een klantbezoek. MDE betaalt. Wens gebruiker 24-10-2026.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `BroodjesAssortiment` (
    `ArtikelID`     INT NOT NULL AUTO_INCREMENT,
    `ExternID`      INT NULL,                         -- product-id in de webshop (leeg = zelf toegevoegd)
    `Naam`          VARCHAR(150) NOT NULL,
    `Omschrijving`  VARCHAR(400) NOT NULL DEFAULT '',
    `Categorie`     VARCHAR(300) NOT NULL DEFAULT '',  -- categorieën van de webshop, met ' / ' ertussen
    `Groep`         VARCHAR(60) NOT NULL DEFAULT '',   -- waaronder het in de keuzelijst staat (Warme broodjes, Drinken …)
    `Prijs`         DECIMAL(8,2) NULL,
    `Afbeelding`    VARCHAR(400) NULL,
    `Leverbaar`     TINYINT(1) NOT NULL DEFAULT 1,     -- op de site en op voorraad bij de laatste keer bijwerken
    `Verborgen`     TINYINT(1) NOT NULL DEFAULT 0,     -- eigen keuze: niet in de keuzelijst (blijft bij bijwerken staan)
    `Bijgewerkt`    DATETIME NULL,
    PRIMARY KEY (`ArtikelID`),
    UNIQUE KEY `UX_BroodjesAssortiment_Extern` (`ExternID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `BroodjesLijsten` (
    `LijstID`       INT NOT NULL AUTO_INCREMENT,
    `Datum`         DATE NOT NULL,
    `Soort`         VARCHAR(12) NOT NULL DEFAULT 'vrijdag',   -- vrijdag | los (klantbezoek e.d.)
    `Omschrijving`  VARCHAR(150) NOT NULL DEFAULT '',
    `SluitOp`       DATETIME NULL,                    -- vrijdag 11:00; daarna telefonisch toevoegen
    `AangemaaktOp`  DATETIME NOT NULL,
    `AangemaaktDoor` INT NULL,
    `GemaildOp`     DATETIME NULL,
    `GemaildDoor`   INT NULL,
    PRIMARY KEY (`LijstID`),
    KEY `IX_BroodjesLijsten_Datum` (`Datum`, `Soort`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `BroodjesBestellingen` (
    `BestellingID`  INT NOT NULL AUTO_INCREMENT,
    `LijstID`       INT NOT NULL,
    `WerknemerID`   INT NULL,                         -- leeg = vrije naam (gast bij een klantbezoek)
    `Naam`          VARCHAR(100) NOT NULL,
    `ArtikelID`     INT NULL,
    `Product`       VARCHAR(150) NOT NULL,            -- zoals het was bij het bestellen
    `Prijs`         DECIMAL(8,2) NULL,
    `Aantal`        INT NOT NULL DEFAULT 1,
    `Opmerking`     VARCHAR(200) NOT NULL DEFAULT '',
    `AangemaaktOp`  DATETIME NOT NULL,
    `AangemaaktDoor` INT NULL,
    PRIMARY KEY (`BestellingID`),
    KEY `IX_BroodjesBestellingen_Lijst` (`LijstID`),
    KEY `IX_BroodjesBestellingen_Werknemer` (`WerknemerID`, `LijstID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
