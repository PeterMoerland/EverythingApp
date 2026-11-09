-- ===========================================================================
--  Verkooporders (VO): simpele orders van een klant opnemen, bevestigen, inkopen, leveren en factureren
--  (wens gebruiker 9-11-2026; Bestellingen > Verkooporders).
--  Stap 1: de order met regels en de orderbevestiging. Bestellen bij de leverancier, pakbon en factuur volgen;
--  hun koppelingen per regel komen in VerkoopOrderKoppelingen (alvast aangemaakt, zodat dit script eenmalig is).
--  De bestaande tabellen (Orders, Pakbonnen, Invoices, Projects, Workorders) worden niet gewijzigd.
--
--  Eenmalig uitvoeren op de MDE-database, VOOR het publiceren. Veilig om nog eens te draaien.
-- ===========================================================================

-- Recht: wie verkooporders mag invoeren (Werknemers > Status > Verkooporders). Directie mag het altijd.
ALTER TABLE `Werknemers`
    ADD COLUMN IF NOT EXISTS `Verkoop` TINYINT(1) NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS `VerkoopOrders` (
    `VerkoopOrderID`  INT NOT NULL AUTO_INCREMENT,
    `Ordernummer`     VARCHAR(20) NOT NULL,        -- VO-2026-001, per jaar opnieuw vanaf 001
    `KlantID`         INT NOT NULL,                -- Relations.id_supplier
    `ContactID`       INT NULL,                    -- Contacts.ID_contact (contactpersoon van de klant)
    `BvID`            INT NOT NULL,                -- Relations.id_supplier van de eigen BV (Werkmaatschappij)
    `WerkorderID`     INT NULL,                    -- Workorders.Werkbon-id: project "Verkoop <jaar>" van de klant, werkorder -01
    `PoKlant`         VARCHAR(100) NULL,           -- ordernummer / PO-referentie van de klant
    `Omschrijving`    VARCHAR(300) NULL,
    `Leverdatum`      DATE NULL,                   -- verwachte leverdatum (op de orderbevestiging)
    `Engels`          TINYINT(1) NOT NULL DEFAULT 0,
    `BtwVerlegd`      TINYINT(1) NOT NULL DEFAULT 0,
    `Status`          VARCHAR(12) NOT NULL DEFAULT 'open',   -- open | afgerond | geannuleerd
    `Opmerking`       VARCHAR(2000) NULL,          -- intern, niet op de bevestiging
    `AangemaaktOp`    DATETIME NOT NULL,
    `AangemaaktDoor`  INT NULL,
    `GewijzigdOp`     DATETIME NULL,
    `BevestigdOp`     DATETIME NULL,               -- orderbevestiging verstuurd
    `BevestigdDoor`   INT NULL,
    `BevestigdAan`    VARCHAR(300) NULL,
    PRIMARY KEY (`VerkoopOrderID`),
    UNIQUE KEY `UX_VerkoopOrders_Ordernummer` (`Ordernummer`),
    KEY `IX_VerkoopOrders_Klant` (`KlantID`, `AangemaaktOp`),
    KEY `IX_VerkoopOrders_Status` (`Status`, `AangemaaktOp`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `VerkoopOrderRegels` (
    `RegelID`         INT NOT NULL AUTO_INCREMENT,
    `VerkoopOrderID`  INT NOT NULL,
    `Volgorde`        INT NOT NULL DEFAULT 0,
    `ArtikelID`       INT NULL,                    -- Artikelen.ArtikelID; NULL = vrije regel (bv. verzendkosten)
    `Code`            VARCHAR(100) NULL,
    `Omschrijving`    VARCHAR(400) NULL,
    `Aantal`          DECIMAL(12,3) NOT NULL DEFAULT 1,
    `Inkoopprijs`     DECIMAL(12,4) NULL,          -- per stuk, voorstel uit de kostprijs van het artikel
    `Verkoopprijs`    DECIMAL(12,4) NULL,          -- per stuk, voorstel = inkoop + opslag (General_settings.ToeslagInkoopVerkoop)
    `LeverancierID`   INT NULL,                    -- Relations.id_supplier, voor het bestellen (stap 2)
    PRIMARY KEY (`RegelID`),
    KEY `IX_VerkoopOrderRegels_Order` (`VerkoopOrderID`, `Volgorde`),
    CONSTRAINT `FK_VerkoopOrderRegels_Order` FOREIGN KEY (`VerkoopOrderID`)
        REFERENCES `VerkoopOrders` (`VerkoopOrderID`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Per regel: wat er besteld, geleverd (pakbon) en gefactureerd is, met het aantal (deelleveringen en deelfacturen).
CREATE TABLE IF NOT EXISTS `VerkoopOrderKoppelingen` (
    `KoppelingID`     INT NOT NULL AUTO_INCREMENT,
    `RegelID`         INT NOT NULL,
    `Soort`           VARCHAR(10) NOT NULL,        -- inkoop (Orderitems.Orderitem-id) | pakbon (PakbonItem.PakbonItemID) | factuur (InvoiceHours.InvoiceHoursID)
    `DoelID`          INT NOT NULL,
    `Aantal`          DECIMAL(12,3) NOT NULL DEFAULT 0,
    `Op`              DATETIME NOT NULL,
    `Door`            INT NULL,
    PRIMARY KEY (`KoppelingID`),
    KEY `IX_VerkoopOrderKoppelingen_Regel` (`RegelID`, `Soort`),
    CONSTRAINT `FK_VerkoopOrderKoppelingen_Regel` FOREIGN KEY (`RegelID`)
        REFERENCES `VerkoopOrderRegels` (`RegelID`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Filestore-soort voor de verstuurde orderbevestiging (MasterID = VerkoopOrderID)
INSERT IGNORE INTO `FileStoreType` (`FilestoreTypeID`, `FileStoreTypeDescription`) VALUES (350, 'Orderbevestiging');

-- Wie het recht krijgt (vul de juiste werknemer-id's in), bijvoorbeeld Michiel:
-- UPDATE `Werknemers` SET `Verkoop` = 1 WHERE `Werknemer-id` IN (0);
-- Wie het nu heeft:
-- SELECT `Werknemer-id`, `NaamVoluit` FROM `Werknemers` WHERE `Verkoop` <> 0;
