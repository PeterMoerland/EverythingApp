-- ===========================================================================
--  AiVragen: vragen aan het taalmodel (Amazon Bedrock) over projecten, uren, bestellingen, voorraad, producten
--  en (met recht Financial/Directie) de financiën, met het antwoord,
--  de gebruikte gegevens en de kosten. Tegelijk de wachtrij: 'Mag wachten' wordt in de daluren
--  uitgevoerd, tegen het lagere Flex-tarief.
--  Vragen aan AI: /ai (✨ in de bovenbalk) en Financiën ▸ Analyse; invoer ook op de pagina Financiën.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `AiVragen` (
    `AiVraagID`    INT NOT NULL AUTO_INCREMENT,
    `WerknemerID`  INT NOT NULL,                     -- Werknemers.Werknemer-id: wie de vraag stelde
    `Vraag`        TEXT NOT NULL,
    `Prioriteit`   VARCHAR(10) NOT NULL,             -- 'direct' of 'wachten'
    `Financieel`   TINYINT(1) NOT NULL DEFAULT 0,    -- vraagsteller had Financial/Directie (en zat niet op een werkplek)
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

-- Voor wie de tabel al had aangemaakt (vóór 19-10-2026 middag):
ALTER TABLE `AiVragen` ADD COLUMN IF NOT EXISTS `Financieel` TINYINT(1) NOT NULL DEFAULT 0 AFTER `Prioriteit`;

-- Gesprekken (doorvragen): elke vervolgvraag wijst naar de eerste vraag van het gesprek; Berichten = het hele gesprek
-- zoals het naar het model ging (met de opgevraagde gegevens), zodat een volgende vraag erop kan voortbouwen.
ALTER TABLE `AiVragen` ADD COLUMN IF NOT EXISTS `GesprekID` INT NULL AFTER `Financieel`;
ALTER TABLE `AiVragen` ADD COLUMN IF NOT EXISTS `Berichten` MEDIUMTEXT NULL AFTER `Gegevens`;
ALTER TABLE `AiVragen` ADD INDEX IF NOT EXISTS `IX_AiVragen_Gesprek` (`GesprekID`);
