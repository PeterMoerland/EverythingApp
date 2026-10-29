-- ===========================================================================
--  Projectteam (Projecten ▸ project ▸ tab Team): per project welke werknemers welke rol hebben, een rol mag
--  meerdere personen hebben. De rollen zelf staan in ProjectRollen en zijn te onderhouden in Beheer ▸ Tabellen ▸
--  Projectrollen. Wens gebruiker 28-10-2026.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `ProjectRollen` (
    `RolID`    INT NOT NULL AUTO_INCREMENT,
    `Naam`     VARCHAR(60) NOT NULL,
    `Volgorde` INT NOT NULL DEFAULT 0,              -- volgorde in de tab Team (laag = boven)
    `Actief`   TINYINT(1) NOT NULL DEFAULT 1,       -- 0 = niet meer kiezen; bestaande teamleden blijven staan
    PRIMARY KEY (`RolID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `ProjectTeam` (
    `ProjectTeamID`  INT NOT NULL AUTO_INCREMENT,
    `ProjectID`      INT NOT NULL,                  -- Projects.Project-Id
    `RolID`          INT NOT NULL,                  -- ProjectRollen.RolID
    `WerknemerID`    INT NOT NULL,                  -- Werknemers.Werknemer-id
    `AangemaaktOp`   DATETIME NOT NULL,
    `AangemaaktDoor` INT NULL,
    PRIMARY KEY (`ProjectTeamID`),
    UNIQUE KEY `UX_ProjectTeam_Lid` (`ProjectID`, `RolID`, `WerknemerID`),
    KEY `IX_ProjectTeam_Werknemer` (`WerknemerID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- standaardrollen, alleen als ze er nog niet zijn
INSERT INTO `ProjectRollen` (`Naam`, `Volgorde`)
SELECT t.`Naam`, t.`Volgorde` FROM (
    SELECT 'Projectleider' AS `Naam`, 10 AS `Volgorde` UNION ALL
    SELECT 'Sales', 20 UNION ALL
    SELECT 'Mechanical lead', 30 UNION ALL
    SELECT 'Electro', 40 UNION ALL
    SELECT 'Software lead', 50 UNION ALL
    SELECT 'Software support', 60 UNION ALL
    SELECT 'Montage', 70 UNION ALL
    SELECT 'Testleider', 80
) t
WHERE NOT EXISTS (SELECT 1 FROM `ProjectRollen` r WHERE r.`Naam` = t.`Naam`);
