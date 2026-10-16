-- ===========================================================================
--  Meldingen: berichten voor één werknemer, getoond bij de bel in de bovenbalk.
--  Eerste gebruik: de besteller hoort dat zijn bestelling is goedgekeurd (Bestellingen ▸ Volgende).
--  Wat op goedkeuring wacht staat niet in deze tabel; dat volgt uit de status van de bestellingen.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `Meldingen` (
    `MeldingID`    INT NOT NULL AUTO_INCREMENT,
    `WerknemerID`  INT NOT NULL,                     -- Werknemers.Werknemer-id: voor wie
    `Soort`        VARCHAR(30) NOT NULL,             -- bv. 'goedgekeurd'
    `OrderID`      INT NULL,                         -- Orders.Order-id als het over een bestelling gaat
    `Tekst`        VARCHAR(250) NOT NULL,
    `Aangemaakt`   DATETIME NOT NULL,
    `Gelezen`      DATETIME NULL,                    -- leeg = nog niet gezien
    PRIMARY KEY (`MeldingID`),
    KEY `IX_Meldingen_Werknemer` (`WerknemerID`, `Gelezen`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
