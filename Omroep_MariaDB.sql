-- ============================================================================
--  Omroep op de Sonos-speakers: meldingen op vaste tijden (Beheer > Omroep). Wens gebruiker 13-11-2026.
--  De app spreekt de tekst uit met de Windows-stem op de server en speelt hem af op de speakers
--  (Services/OmroepService.cs, OmroepWaker). Direct omroepen (PA) werkt ook zonder deze tabel.
--  Zelf draaien; het script is veilig om nog eens te draaien.
-- ============================================================================

CREATE TABLE IF NOT EXISTS `OmroepMeldingen` (
    `OmroepID`        INT NOT NULL AUTO_INCREMENT,
    `Tekst`           VARCHAR(500) NOT NULL DEFAULT '',   -- wat er gezegd wordt
    `Dagen`           VARCHAR(7)   NOT NULL DEFAULT '',   -- cijfers 1 (maandag) t/m 7 (zondag): '5' = vrijdag, '12345' = werkdagen
    `Tijd`            TIME         NOT NULL DEFAULT '09:00:00',
    `Speakers`        VARCHAR(600) NULL,                  -- uuid's van de speakers met een komma ertussen; leeg = alle
    `Volume`          TINYINT UNSIGNED NULL,              -- 1-100; leeg = het standaardvolume (appsettings Omroep:Volume, 25)
    `Toon`            TINYINT(1)   NOT NULL DEFAULT 1,    -- eerst een ding-dong
    `Actief`          TINYINT(1)   NOT NULL DEFAULT 1,
    `LaatstGedaan`    DATETIME     NULL,                  -- wanneer hij voor het laatst klonk (tegen dubbel omroepen)
    `LaatsteUitkomst` VARCHAR(300) NULL,                  -- op welke speakers het lukte
    `GewijzigdOp`     DATETIME     NULL,
    `GewijzigdDoor`   INT          NULL,                  -- Werknemer-id
    PRIMARY KEY (`OmroepID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- De eerste melding: vrijdag om 9:00 (de broodjeslijst sluit om 9:30). Alleen als de tabel nog leeg is.
INSERT INTO `OmroepMeldingen` (`Tekst`, `Dagen`, `Tijd`, `Toon`, `Actief`, `GewijzigdOp`)
SELECT 'Goedemorgen. Hebben jullie allemaal een broodje besteld? De lijst sluit om half tien.', '5', '09:00:00', 1, 1, NOW()
  FROM DUAL
 WHERE NOT EXISTS (SELECT 1 FROM `OmroepMeldingen`);
