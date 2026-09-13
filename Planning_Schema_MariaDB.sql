-- ============================================================
--  Planning (Gantt)
--
--  Eén algemene tabel voor alle balken op de planning. Wat een balk is,
--  bepaalt de soort (opzoektabel met kleur); waar hij bij hoort, bepalen
--  twee losse verwijzingen die elk leeg mogen zijn:
--
--    WerknemerID  WerkbonID   betekent
--    -----------  ---------   ------------------------------------------
--    12           leeg        iets van één werknemer (vakantie, cursus)
--    leeg         leeg        geldt voor iedereen (bedrijfssluiting)
--    leeg         4567        werkorder gepland, nog niemand toegewezen
--    12           4567        Jan werkt die dagen aan die werkorder
--
--  Bewust géén "soort + algemeen verwijsveld": dan kan de database geen
--  relaties bewaken, en de regel "werknemer X op werkorder Y" past er
--  niet in. Elke verwijzing zijn eigen kolom; een nieuwe soort verwijzing
--  (machine?) is later gewoon een kolom erbij.
--
--  Dezelfde tabel levert twee weergaven: rijen per werknemer (personeels-
--  planning) en rijen per werkorder (werkplanning) — alleen anders groeperen.
--
--  Uitvoeren op de MDE-database. Bestaat een tabel al, dan doet het script
--  daarvoor niets.
-- ============================================================

-- ---- de soorten, met kleur: nieuwe soorten zijn een regel erbij, geen code ----
CREATE TABLE IF NOT EXISTS `PlanningSoorten` (
  `Soort`      int(10) NOT NULL,
  `Naam`       varchar(50) NOT NULL,

  -- Kleur van de balk in de Gantt, als #rrggbb.
  `Kleur`      varchar(9) NOT NULL DEFAULT '#4a7fb5',

  -- Volgorde in keuzelijsten.
  `Volgorde`   int(10) NOT NULL DEFAULT 0,

  -- Soorten die niet meer gebruikt worden verberg je; weggooien hoeft nooit.
  `Actief`     tinyint(1) NOT NULL DEFAULT 1,

  `last_updated` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP
                 ON UPDATE CURRENT_TIMESTAMP,

  PRIMARY KEY (`Soort`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

INSERT IGNORE INTO `PlanningSoorten` (`Soort`, `Naam`, `Kleur`, `Volgorde`) VALUES
  (1, 'Verlof',           '#e6a23c', 1),
  (2, 'Ziek',             '#c0392b', 2),
  (3, 'Bedrijfssluiting', '#95a5a6', 3),
  (4, 'Werk',             '#4a7fb5', 4),
  (5, 'Cursus / beurs',   '#8e6bb5', 5);

-- ---- de balken zelf ----
CREATE TABLE IF NOT EXISTS `Planning` (
  `PlanningID`   int(10) NOT NULL AUTO_INCREMENT,

  `Soort`        int(10) NOT NULL,

  -- Leeg = geldt voor iedereen (bedrijfssluiting, feestdag).
  `WerknemerID`  int(10) DEFAULT NULL,

  -- Leeg = niet aan een werkorder gebonden. Verwijst naar Workorders.Werkbon-id.
  `WerkbonID`    int(10) DEFAULT NULL,

  -- Hele dagen; einde is de laatste dag die meetelt (t/m).
  `Start`        date NOT NULL,
  `Einde`        date NOT NULL,

  `Omschrijving` varchar(200) NOT NULL DEFAULT '',

  -- Geplande uren voor de hele balk; leeg als dat niet van toepassing is
  -- (verlof, sluiting). Voor later: vergelijken met de geboekte uren.
  `GeplandeUren` decimal(7,2) DEFAULT NULL,

  `AangemaaktOp`   datetime DEFAULT NULL,
  `AangemaaktDoor` int(10) DEFAULT NULL,
  `GewijzigdOp`    datetime DEFAULT NULL,
  `GewijzigdDoor`  int(10) DEFAULT NULL,

  `last_updated` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP
                 ON UPDATE CURRENT_TIMESTAMP,

  PRIMARY KEY (`PlanningID`),
  KEY `IX_Planning_Periode`   (`Start`, `Einde`),
  KEY `IX_Planning_Werknemer` (`WerknemerID`),
  KEY `IX_Planning_Werkbon`   (`WerkbonID`),

  CONSTRAINT `FK_Planning_Soort`
    FOREIGN KEY (`Soort`) REFERENCES `PlanningSoorten` (`Soort`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ---- de pijlen: van het einde van de een naar het begin van de ander ----
-- Een aparte tabel en geen kolom op Planning: een balk kan op meerdere
-- voorgangers wachten (monteren pas als draaiwerk én inkoopdelen klaar zijn)
-- en zelf meerdere opvolgers hebben.
CREATE TABLE IF NOT EXISTS `PlanningAfhankelijkheden` (
  -- De voorganger: hier vertrekt de pijl (aan het einde van de balk)…
  `VanID`  int(10) NOT NULL,

  -- …en dit is de opvolger: daar komt de pijl aan (aan het begin).
  `NaarID` int(10) NOT NULL,

  -- Speling in dagen: 2 = de opvolger kan pas 2 dagen ná de voorganger
  -- beginnen (drogen, transport). Mag ook negatief (mag 1 dag overlappen).
  `Speling` int(10) NOT NULL DEFAULT 0,

  `last_updated` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP
                 ON UPDATE CURRENT_TIMESTAMP,

  PRIMARY KEY (`VanID`, `NaarID`),
  KEY `IX_PlanningAfh_Naar` (`NaarID`),

  -- Verdwijnt een balk, dan verdwijnen zijn pijlen vanzelf mee.
  CONSTRAINT `FK_PlanningAfh_Van`
    FOREIGN KEY (`VanID`) REFERENCES `Planning` (`PlanningID`) ON DELETE CASCADE,
  CONSTRAINT `FK_PlanningAfh_Naar`
    FOREIGN KEY (`NaarID`) REFERENCES `Planning` (`PlanningID`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Verwijzingen naar Werknemers/Workorders kunnen als de kolomtypes kloppen;
-- bij twijfel weglaten, de app werkt ook zonder.
-- ALTER TABLE `Planning`
--   ADD CONSTRAINT `FK_Planning_Werknemer`
--   FOREIGN KEY (`WerknemerID`) REFERENCES `Werknemers` (`Werknemer-id`);
-- ALTER TABLE `Planning`
--   ADD CONSTRAINT `FK_Planning_Werkbon`
--   FOREIGN KEY (`WerkbonID`) REFERENCES `Workorders` (`Werkbon-id`);
