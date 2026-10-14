-- Testrecepten per controllertype (Productie ▸ Controllertypen ▸ Testrecepten; Test PCB voert ze uit via
-- de werkplek-agent, taak mde.test). Een recept is een lijst stappen (JSON) uit vaste bouwstenen:
--   identiteit, lees, schrijf, wacht, beweging, doorgeefluik  — zie MdeAgent/mde_agent/taken/mde.py.
-- Een nieuwe print = ESI uploaden bij het type en hier de recepten invullen; de agent hoeft niet aangepast.

CREATE TABLE IF NOT EXISTS `DriveRecepten` (
    `DriveReceptID`  INT          NOT NULL AUTO_INCREMENT,
    `DriveTypeID`    INT          NOT NULL,
    `Volgorde`       INT          NOT NULL DEFAULT 1,
    `Naam`           VARCHAR(60)  NOT NULL COMMENT 'bv. Identiteit, Motoren aanwezig, Joint 1 heen en terug',
    `Omschrijving`   VARCHAR(500) NULL,
    `Stappen`        LONGTEXT     NOT NULL COMMENT 'JSON-lijst van stappen (bouwstenen)',
    `Verplicht`      TINYINT(1)   NOT NULL DEFAULT 1 COMMENT '1 = hoort bij de eindcontrole',
    `AddedBy`        INT          NULL,
    `AddedAt`        DATETIME     NULL,
    `last_updated`   TIMESTAMP    NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (`DriveReceptID`),
    KEY `ix_recept_type` (`DriveTypeID`, `Volgorde`),
    CONSTRAINT `fk_recept_type` FOREIGN KEY (`DriveTypeID`) REFERENCES `DriveTypen` (`DriveTypeID`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Eerste recept voor de M-Pede-WJ1 (Swivel): identiteit en de standaardobjecten. De bewegings- en
-- motorstappen komen erbij zodra de bits van 0x7000/0x6000 en het 0x8000-protocol vastliggen.
INSERT INTO `DriveRecepten` (`DriveTypeID`, `Volgorde`, `Naam`, `Omschrijving`, `Stappen`, `Verplicht`, `AddedAt`)
SELECT t.`DriveTypeID`, 1, 'Identiteit', 'Fabrikant, productcode, revisie en firmwareversie volgens het ESI',
       '[{"stap":"identiteit","vendor":"0x000010E3","productcode":"0x00055955","revisie":"0x00000001"},
 {"stap":"lees","naam":"Druk (bar x100)","index":"0x6020","sub":0,"type":"UINT","min":0,"max":4500},
 {"stap":"lees","naam":"Joint 1 stand","index":"0x6010","sub":1,"type":"INT","min":-512,"max":512},
 {"stap":"lees","naam":"Joint 2 stand","index":"0x6010","sub":2,"type":"INT","min":-512,"max":512}]', 1, NOW()
  FROM `DriveTypen` t
 WHERE t.`ProductCode` = '0x00055955'
   AND NOT EXISTS (SELECT 1 FROM `DriveRecepten` r WHERE r.`DriveTypeID` = t.`DriveTypeID`);

-- Kleppen en druksensor: vacuüm aan → druk omlaag, blaas aan → druk omhoog, beide uit. De drukgrenzen
-- (bar x100) zijn een beginwaarde; stel ze bij op wat de testrig met perslucht echt laat zien.
-- De uit-stappen hebben "stoppen": false, zodat de kleppen ook na een mislukte meting dichtgaan.
INSERT INTO `DriveRecepten` (`DriveTypeID`, `Volgorde`, `Naam`, `Omschrijving`, `Stappen`, `Verplicht`, `AddedAt`)
SELECT t.`DriveTypeID`, 2, 'Vacuüm en blaas', 'Kleppen schakelen en de druksensor controleren (perslucht op de testrig nodig)',
       '[{"stap":"monitor","objecten":[{"index":"0x6020","sub":0,"type":"UINT","naam":"Druk (bar x100)"}]},
 {"stap":"lees","naam":"Druk in rust","index":"0x6020","sub":0,"type":"UINT","min":0,"max":4500},
 {"stap":"schrijf","naam":"Vacuüm aan","index":"0x7020","sub":1,"type":"BOOL","waarde":1},
 {"stap":"wacht","seconden":1.5},
 {"stap":"lees","naam":"Druk met vacuüm","index":"0x6020","sub":0,"type":"UINT","max":50},
 {"stap":"schrijf","naam":"Vacuüm uit","index":"0x7020","sub":1,"type":"BOOL","waarde":0,"stoppen":false},
 {"stap":"schrijf","naam":"Blaas aan","index":"0x7020","sub":2,"type":"BOOL","waarde":1},
 {"stap":"wacht","seconden":1.5},
 {"stap":"lees","naam":"Druk met blaas","index":"0x6020","sub":0,"type":"UINT","min":200},
 {"stap":"schrijf","naam":"Blaas uit","index":"0x7020","sub":2,"type":"BOOL","waarde":0,"stoppen":false},
 {"stap":"wacht","seconden":1},
 {"stap":"lees","naam":"Druk weer in rust","index":"0x6020","sub":0,"type":"UINT","min":0,"max":4500}]', 1, NOW()
  FROM `DriveTypen` t
 WHERE t.`ProductCode` = '0x00055955'
   AND NOT EXISTS (SELECT 1 FROM `DriveRecepten` r WHERE r.`DriveTypeID` = t.`DriveTypeID` AND r.`Naam` = 'Vacuüm en blaas');
