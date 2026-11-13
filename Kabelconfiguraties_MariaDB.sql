-- ===========================================================================
--  Kabelconfiguraties: kabels op maat voor de kabelleverancier LDZY
--  (Tools ▸ Kabelconfigurator, wens gebruiker 13-11-2026).
--  De hele spec staat als JSON in Spec (Models/Kabel.cs); Naam en LeveranciersNummer
--  (W2464-08-G020007, het schema van LDZY) staan er los naast om te zoeken en te tonen.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
--  Zonder deze tabel werkt het scherm wel (samenstellen, tekenen, afdrukken), alleen opslaan niet.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `Kabelconfiguraties` (
    `KabelID`             INT NOT NULL AUTO_INCREMENT,
    `Naam`                VARCHAR(200) NOT NULL,
    `Leverancier`         VARCHAR(100) NULL,            -- 'LDZY'
    `LeveranciersNummer`  VARCHAR(60) NULL,             -- nummer volgens het schema van de leverancier
    `Spec`                LONGTEXT NOT NULL,            -- de kabel als JSON (KabelSpec)
    `ArtikelID`           INT NULL,                     -- Artikelen.ArtikelID zodra er een MDE-artikel van is gemaakt (nog niet gebruikt)
    `GemaaktOp`           DATETIME NOT NULL,
    `GemaaktDoor`         INT NULL,                     -- Werknemers.Werknemer-id
    `GewijzigdOp`         DATETIME NULL,
    `GewijzigdDoor`       INT NULL,
    PRIMARY KEY (`KabelID`),
    KEY `IX_Kabelconfiguraties_Naam` (`Naam`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
