-- ===========================================================================
--  Betalingen per uitgaande factuur (Financiën ▸ Uitgaande facturen). Een factuur kan in delen betaald worden
--  (bv. 90% nu, 10% inhouding door de klant); elke ontvangst of afboeking is een regel. Wens collega 4-11-2026.
--
--  Soort:  bank       = ontvangst op de bank (BanktransactieID gevuld; ook in banktransacties_facturen_crossref)
--          handmatig  = ontvangen zonder banktransactie (contant, verrekend, niet te koppelen)
--          afboeking  = niet ontvangen maar afgeboekt (korting, inhouding die niet komt); maakt de factuur rond
--
--  Invoices.Betaald blijft de vlag waar alles op filtert: de app zet hem zodra de regels samen het bedrag halen.
--  Een betaalde factuur zonder regels hier (alles van vóór deze tabel) telt als volledig betaald.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `FactuurBetalingen` (
    `FactuurBetalingID` INT NOT NULL AUTO_INCREMENT,
    `FactuurID`         INT NOT NULL,                  -- Invoices.Invoice-id
    `Datum`             DATE NOT NULL,                 -- datum van ontvangst of afboeking
    `Bedrag`            DECIMAL(12,2) NOT NULL,        -- incl. btw; het deel voor déze factuur
    `Soort`             VARCHAR(20) NOT NULL,          -- bank / handmatig / afboeking
    `BanktransactieID`  INT NULL,                      -- banktransacties.BankTransacties_ID (alleen bij bank)
    `Opmerking`         VARCHAR(255) NULL,
    `AangemaaktOp`      DATETIME NOT NULL,
    `AangemaaktDoor`    INT NULL,                      -- Werknemers.Werknemer-id
    PRIMARY KEY (`FactuurBetalingID`),
    KEY `IX_FactuurBetalingen_Factuur` (`FactuurID`),
    KEY `IX_FactuurBetalingen_Bank` (`BanktransactieID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
