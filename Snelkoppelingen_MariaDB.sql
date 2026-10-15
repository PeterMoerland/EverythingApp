-- ===========================================================================
--  Snelbalk: per gebruiker maximaal 10 snelkoppelingen bovenin elke pagina, met een
--  icoon (Lucide-naam, wwwroot/iconen) of een eigen plaatje (PNG van 64x64, door de
--  browser verkleind). Gebruiker = 'w:<Werknemer-id>' of, zonder koppeling, 'e:<e-mail>'.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `Snelkoppelingen` (
    `ID`             INT NOT NULL AUTO_INCREMENT,
    `Gebruiker`      VARCHAR(120) NOT NULL,
    `Volgorde`       INT NOT NULL DEFAULT 0,
    `Naam`           VARCHAR(40) NOT NULL,
    `Url`            VARCHAR(500) NOT NULL,                     -- adres binnen de app, bv. /activityboard/12
    `Icoon`          VARCHAR(60) NULL,                          -- Lucide-naam; leeg = automatisch (icoon van de module)
    `Plaatje`        MEDIUMBLOB NULL,                           -- eigen plaatje (PNG); gaat voor het icoon
    `last_updated`   TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (`ID`),
    KEY `IX_Snelkoppelingen_Gebruiker` (`Gebruiker`, `Volgorde`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
