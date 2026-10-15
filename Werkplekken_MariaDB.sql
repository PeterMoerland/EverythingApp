-- ===========================================================================
--  Werkplekken: pc's waar meerdere mensen aan werken (robots, magazijn/inboeken).
--  Een werkplek is geen Google-account (kost geen licentie): Beheer maakt een
--  koppelcode, op de pc vul je die in op /werkplek/koppelen en de pc blijft
--  ingelogd als die werkplek tot hij centraal wordt ontkoppeld.
--  Rechten en de naam in de historie komen van een niet-persoon in Werknemers.
--  Mail lezen en Agenda staan op een werkplek uit; versturen kan vanaf een alias.
--  Wie er aan de pc werkt: naam kiezen + pincode; dan komt het werk op die naam (rechten blijven van de werkplek).
--  Scherm: Beheer ▸ Werkplekken (recht Beheer of Directie).
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `Werkplekken` (
    `WerkplekID`     INT NOT NULL AUTO_INCREMENT,
    `Naam`           VARCHAR(80) NOT NULL,
    `WerknemerID`    INT NOT NULL,                              -- Werknemers.Werknemer-id (Persoon = nee): rechten en historie
    `Productie`      TINYINT(1) NOT NULL DEFAULT 1,             -- menu Productie tonen (als er ook een werkplek-agent is)
    `Startpagina`    VARCHAR(200) NULL,                         -- adres binnen de app, bv. voorraad/dragers
    `MailAfzender`   VARCHAR(120) NULL,                         -- alias waarvandaan de werkplek mailt, bv. magazijn@…
    `MailAccount`    VARCHAR(120) NULL,                         -- het account waar die alias ("verzenden als") op staat
    `Actief`         TINYINT(1) NOT NULL DEFAULT 1,
    `KoppelCode`     VARCHAR(12) NULL,                          -- eenmalige code om een pc te koppelen
    `KoppelCodeTot`  DATETIME NULL,
    `AangemaaktOp`   DATETIME NULL,
    `last_updated`   TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (`WerkplekID`),
    UNIQUE KEY `UX_Werkplekken_Naam` (`Naam`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Gekoppelde pc's (browsers). Het inlogcookie van zo'n pc noemt het apparaat; bij elk verzoek controleert de app of
-- het nog bestaat. Ontkoppelen = de regel weghalen.
CREATE TABLE IF NOT EXISTS `WerkplekApparaten` (
    `ApparaatID`     INT NOT NULL AUTO_INCREMENT,
    `WerkplekID`     INT NOT NULL,
    `Omschrijving`   VARCHAR(250) NULL,                         -- browser en adres bij het koppelen
    `GekoppeldOp`    DATETIME NOT NULL,
    `LaatstGezien`   DATETIME NULL,
    `MedewerkerID`   INT NULL,                                  -- wie er nu aan werkt ("Wie ben jij?" + pincode); leeg = niemand
    `MedewerkerSinds` DATETIME NULL,
    `LaatsteActiviteit` DATETIME NULL,                          -- na 30 minuten zonder activiteit weer niemand
    PRIMARY KEY (`ApparaatID`),
    KEY `IX_WerkplekApparaten_Werkplek` (`WerkplekID`),
    CONSTRAINT `FK_WerkplekApparaten_Werkplek` FOREIGN KEY (`WerkplekID`)
        REFERENCES `Werkplekken` (`WerkplekID`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Voor wie een eerdere versie van dit script draaide: de kolommen van "wie werkt er" alsnog toevoegen.
ALTER TABLE `WerkplekApparaten`
    ADD COLUMN IF NOT EXISTS `MedewerkerID`      INT NULL,
    ADD COLUMN IF NOT EXISTS `MedewerkerSinds`   DATETIME NULL,
    ADD COLUMN IF NOT EXISTS `LaatsteActiviteit` DATETIME NULL;

-- Wie zich op een werkplek mag aanmelden ("Wie ben jij?"): vinkje Werkplek gebruiker bij Werknemers ▸ Status.
ALTER TABLE `Werknemers`
    ADD COLUMN IF NOT EXISTS `Werkplekgebruiker` TINYINT(1) NOT NULL DEFAULT 0;

-- Pincode per werknemer (4 cijfers) om op een werkplek aan te geven wie er werkt. Alleen een hash (PBKDF2-SHA256 met
-- zout), nooit de code zelf. Instellen: Werknemers ▸ Mijn pincode (op je eigen pc); wissen: Beheer ▸ Werkplekken.
CREATE TABLE IF NOT EXISTS `WerknemerPincodes` (
    `WerknemerID`    INT NOT NULL,                              -- Werknemers.Werknemer-id
    `Hash`           VARCHAR(200) NOT NULL,
    `Gewijzigd`      DATETIME NOT NULL,
    PRIMARY KEY (`WerknemerID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
