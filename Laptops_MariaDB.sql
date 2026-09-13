-- ============================================================
--  Laptopregister: welke laptop wordt door wie gebruikt
--
--  Elke laptop meldt zichzelf bij het starten van de app (via de
--  snelkoppeling MDE-Starten.bat, die eerst laptop-registreren.ps1 draait
--  en dan de browser opent). De combinatie computernaam + gebruikersnaam
--  is uniek: bestaat hij al, dan wordt alleen 'laatste keer' en de
--  hardware-informatie bijgewerkt — er komt geen dubbele regel bij.
--
--  De ServiceTag is bij Dell gewoon het BIOS-serienummer; daarmee is de
--  laptop rechtstreeks bij Dell-support op te zoeken.
-- ============================================================

CREATE TABLE IF NOT EXISTS `LaptopGebruik` (
  `LaptopGebruikID` int(10) NOT NULL AUTO_INCREMENT,

  `Computernaam`    varchar(64) NOT NULL,
  -- De Windows-gebruikersnaam op de laptop.
  `Gebruikersnaam`  varchar(64) NOT NULL,

  -- Bij Dell: de service tag (= BIOS-serienummer). Bij andere merken staat
  -- hier het serienummer dat de fabrikant meegaf.
  `ServiceTag`      varchar(32) NOT NULL DEFAULT '',

  `Merk`            varchar(64) NOT NULL DEFAULT '',
  `Model`           varchar(64) NOT NULL DEFAULT '',
  `Processor`       varchar(128) NOT NULL DEFAULT '',
  `GeheugenGB`      int(10) DEFAULT NULL,

  -- Handmatig te koppelen aan Werknemers (Werknemer-id); de Windows-naam
  -- zegt niet altijd wie het echt is.
  `WerknemerID`     int(10) DEFAULT NULL,

  `EersteKeer`      datetime DEFAULT NULL,
  `LaatsteKeer`     datetime DEFAULT NULL,

  -- Hoe vaak deze combinatie zich heeft gemeld; zegt iets over wie de
  -- laptop écht dagelijks gebruikt.
  `Meldingen`       int(10) NOT NULL DEFAULT 1,

  `last_updated`    timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP
                    ON UPDATE CURRENT_TIMESTAMP,

  PRIMARY KEY (`LaptopGebruikID`),

  -- Dezelfde laptop + dezelfde gebruiker = dezelfde regel, altijd.
  UNIQUE KEY `UQ_LaptopGebruik` (`Computernaam`, `Gebruikersnaam`),

  KEY `IX_LaptopGebruik_ServiceTag` (`ServiceTag`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
