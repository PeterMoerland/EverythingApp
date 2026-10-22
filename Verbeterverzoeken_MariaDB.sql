-- ===========================================================================
--  Verbeterverzoeken: ideeën en wensen van collega's voor de app.
--  Het lampje (💡) in de bovenbalk: tekst intypen, de pagina waar je was gaat vanzelf mee.
--  Iedereen ziet de lijst (Beheer ▸ Verbeterverzoeken); Directie past tekst, status en reactie aan.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `Verbeterverzoeken` (
    `VerzoekID`       INT NOT NULL AUTO_INCREMENT,
    `Tekst`           VARCHAR(2000) NOT NULL,
    `Pagina`          VARCHAR(300) NULL,           -- adres in de app waar het verzoek is ingediend
    `IngediendOp`     DATETIME NOT NULL,
    `IngediendDoor`   INT NULL,                    -- Werknemers.Werknemer-id
    `Werkplek`        VARCHAR(100) NULL,           -- naam van de gedeelde werkplek, als het daar is ingediend
    `Status`          VARCHAR(12) NOT NULL DEFAULT 'nieuw',   -- nieuw | gepland | bezig | gedaan | afgewezen
    `Reactie`         VARCHAR(2000) NULL,          -- toelichting van de directie
    `GewijzigdOp`     DATETIME NULL,
    `GewijzigdDoor`   INT NULL,
    PRIMARY KEY (`VerzoekID`),
    KEY `IX_Verbeterverzoeken_Status` (`Status`, `IngediendOp`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
