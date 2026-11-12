-- ===========================================================================
--  Orderbevestigingen uit de inkooppostbus (wens gebruiker 12-11-2026; vervangt het VB-programmaatje van vroeger).
--
--  De service leest op de achtergrond inkoop@mde-automation.nl (appsettings, sectie InkoopBevestigingen), zoekt in
--  elk binnengekomen bericht een PO-nummer (PO-12345) en zet de PDF-bijlagen als document bij die bestelling
--  (FileStore soort 4, zoals wat je bij Bestellingen ▸ Documenten met de hand toevoegt).
--
--  Deze tabel houdt per verwerkt bericht bij wat ermee gedaan is. Daarmee wordt een bericht nooit twee keer
--  verwerkt, en staat in Bestellingen ▸ Bevestigingen wat niet gekoppeld kon worden (geen PO-nummer gevonden, of een
--  PO-nummer dat niet bestaat), om met de hand toe te wijzen of te negeren.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `InkoopBevestigingen` (
    `BevestigingID` INT NOT NULL AUTO_INCREMENT,
    `MailId`        VARCHAR(64) NOT NULL,          -- het bericht-id van Gmail
    `Ontvangen`     DATETIME NULL,
    `Van`           VARCHAR(200) NULL,
    `Onderwerp`     VARCHAR(300) NULL,
    `Uitkomst`      VARCHAR(20) NOT NULL,          -- gekoppeld, geenpo, onbekend, geenbijlage, genegeerd
    `Orders`        VARCHAR(100) NULL,             -- de gevonden PO-nummers, met komma's
    `OrderID`       INT NULL,                      -- de bestelling waar de bestanden bij staan (bij meer dan een: de eerste)
    `Bestanden`     INT NOT NULL DEFAULT 0,        -- hoeveel PDF's er bij de bestelling zijn gezet
    `Melding`       VARCHAR(500) NULL,
    `VerwerktOp`    DATETIME NOT NULL,
    `VerwerktDoor`  INT NULL,                      -- Werknemers.Werknemer-id bij met de hand koppelen of negeren; NULL = de service
    PRIMARY KEY (`BevestigingID`),
    UNIQUE KEY `UX_InkoopBevestigingen_Mail` (`MailId`),
    KEY `IX_InkoopBevestigingen_Uitkomst` (`Uitkomst`, `Ontvangen`),
    KEY `IX_InkoopBevestigingen_Order` (`OrderID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
