-- ===========================================================================
--  AiVragen: vragen aan het taalmodel (Amazon Bedrock) over de financiën, met het antwoord,
--  de gebruikte gegevens en de kosten. Tegelijk de wachtrij: 'Mag wachten' wordt in de daluren
--  uitgevoerd, tegen het lagere Flex-tarief.
--  Financiën ▸ Analyse ▸ Vragen aan AI (/financien/analyse), invoer ook op de pagina Financiën.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `AiVragen` (
    `AiVraagID`    INT NOT NULL AUTO_INCREMENT,
    `WerknemerID`  INT NOT NULL,                     -- Werknemers.Werknemer-id: wie de vraag stelde
    `Vraag`        TEXT NOT NULL,
    `Prioriteit`   VARCHAR(10) NOT NULL,             -- 'direct' of 'wachten'
    `Status`       VARCHAR(12) NOT NULL,             -- 'wacht', 'bezig', 'klaar', 'fout', 'geannuleerd'
    `Aangemaakt`   DATETIME NOT NULL,
    `Gestart`      DATETIME NULL,
    `Klaar`        DATETIME NULL,
    `Antwoord`     MEDIUMTEXT NULL,                  -- tekst van het model (markdown)
    `Gegevens`     MEDIUMTEXT NULL,                  -- JSON: welke gegevens het model opvroeg, met de tabellen
    `Model`        VARCHAR(150) NULL,
    `Laag`         VARCHAR(20) NULL,                 -- default / flex / priority, zoals Bedrock hem uitvoerde
    `TokensIn`     INT NULL,
    `TokensUit`    INT NULL,
    `Kosten`       DECIMAL(10,4) NULL,               -- schatting in euro's (prijzen uit appsettings)
    `Fout`         VARCHAR(1000) NULL,
    PRIMARY KEY (`AiVraagID`),
    KEY `IX_AiVragen_Werknemer` (`WerknemerID`, `Aangemaakt`),
    KEY `IX_AiVragen_Status` (`Status`, `Prioriteit`, `Aangemaakt`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
