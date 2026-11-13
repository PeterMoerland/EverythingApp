-- ===========================================================================
--  KabelConnectoren: de connectortypes die MDE gebruikt, als keuzelijst in de
--  kabelconfigurator (Tools > Kabelconfigurator, wens gebruiker 13-11-2026).
--  Een regel per housing: de familie, het aantal pinnen, de steek en de
--  fabrikantnummers van de housing en de bijbehorende terminal.
--  Onderhouden via Tools > Kabelconfigurator > Connectortypes beheren
--  (/beheer/tabel/kabel-connectoren), of hier regels bijzetten.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien:
--  een housing die al in de tabel staat wordt niet nog eens toegevoegd.
--  Zonder deze tabel werkt de configurator ook; je vult de connector dan zelf in.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `KabelConnectoren` (
    `KabelConnectorID` INT NOT NULL AUTO_INCREMENT,
    `Familie`          VARCHAR(100) NOT NULL DEFAULT '',   -- bv. 'Molex Pico-Lock' of 'JST GH'
    `Pinnen`           INT NULL,                           -- aantal polen van deze housing
    `Steek`            VARCHAR(10) NULL,                   -- in mm, bv. '1.5' of '2,54' (tekst, zodat komma en punt allebei mogen)
    `Housing`          VARCHAR(60) NULL,                   -- fabrikantnummer van de housing
    `Terminal`         VARCHAR(60) NULL,                   -- fabrikantnummer van de terminal (crimpcontact)
    `Opmerking`        VARCHAR(200) NULL,
    `Actief`           TINYINT(1) NOT NULL DEFAULT 1,      -- 0 = niet meer in de keuzelijst
    PRIMARY KEY (`KabelConnectorID`),
    KEY `IX_KabelConnectoren_Familie` (`Familie`, `Pinnen`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ---------------------------------------------------------------------------
--  De types die MDE gebruikt (opgave gebruiker 13-11-2026). De nummers zijn
--  nagezocht bij distributeurs (de site van Molex zelf was niet bereikbaar);
--  controleer een nummer voor het de deur uit gaat.
--
--  JST GH, 1,25 mm, 2 t/m 12 pinnen: housing GHR-xxV-S, contact SSHL-002T-P0.2.
--  JST PH, 2,0 mm, 2 t/m 8 pinnen: housing PHR-x, contact SPH-002T-P0.5S.
--  JST XH, 2,5 mm, 2 t/m 8 pinnen: housing XHP-x, contact SXH-001T-P0.6.
--  Molex Pico-Lock 1,5 mm, 2 t/m 12 pinnen: housing 504051-xx01, terminal
--    504052-0098. De 8-polige is de connector van de kabel die LDZY al levert.
--  Molex Pico-Lock 2,0 mm: housing 205341-02xx. Die bestaat alleen met 2 t/m 6
--    pinnen; 7 t/m 12 is er in 2,0 mm niet. Terminal 205342-5128 (AWG 24-26) of
--    205342-5028 (AWG 20-22).
--  Molex Pico-Clasp enkelrijig, 1,0 mm, 2 t/m 15 pinnen: housing 501330-xx00,
--    terminal 501334-0100. 2 t/m 5 pinnen hebben een frictieslot, de rest een
--    positief slot. Van 6, 10, 11 en 13 pinnen is het nummer afgeleid uit de reeks
--    en niet apart teruggevonden: staat in de opmerking.
--  Molex Pico-Clasp dubbelrijig, 1,0 mm: housing 501189-xx10 met 20, 30, 40 of 50
--    pinnen. De terminal komt uit de 501193-serie; welke uitvoering (vertind of
--    verguld) is nog niet gekozen en staat daarom niet als nummer in de lijst.
-- ---------------------------------------------------------------------------
INSERT INTO `KabelConnectoren` (`Familie`, `Pinnen`, `Steek`, `Housing`, `Terminal`, `Opmerking`, `Actief`)
SELECT n.`Familie`, n.`Pinnen`, n.`Steek`, n.`Housing`, n.`Terminal`, n.`Opmerking`, 1
  FROM (
            SELECT 'JST GH' AS `Familie`, 2 AS `Pinnen`, '1.25' AS `Steek`, 'GHR-02V-S' AS `Housing`, 'SSHL-002T-P0.2' AS `Terminal`, 'AWG 26-30' AS `Opmerking`
  UNION ALL SELECT 'JST GH', 3, '1.25', 'GHR-03V-S', 'SSHL-002T-P0.2', 'AWG 26-30'
  UNION ALL SELECT 'JST GH', 4, '1.25', 'GHR-04V-S', 'SSHL-002T-P0.2', 'AWG 26-30'
  UNION ALL SELECT 'JST GH', 5, '1.25', 'GHR-05V-S', 'SSHL-002T-P0.2', 'AWG 26-30'
  UNION ALL SELECT 'JST GH', 6, '1.25', 'GHR-06V-S', 'SSHL-002T-P0.2', 'AWG 26-30'
  UNION ALL SELECT 'JST GH', 7, '1.25', 'GHR-07V-S', 'SSHL-002T-P0.2', 'AWG 26-30'
  UNION ALL SELECT 'JST GH', 8, '1.25', 'GHR-08V-S', 'SSHL-002T-P0.2', 'AWG 26-30'
  UNION ALL SELECT 'JST GH', 9, '1.25', 'GHR-09V-S', 'SSHL-002T-P0.2', 'AWG 26-30'
  UNION ALL SELECT 'JST GH', 10, '1.25', 'GHR-10V-S', 'SSHL-002T-P0.2', 'AWG 26-30'
  UNION ALL SELECT 'JST GH', 11, '1.25', 'GHR-11V-S', 'SSHL-002T-P0.2', 'AWG 26-30'
  UNION ALL SELECT 'JST GH', 12, '1.25', 'GHR-12V-S', 'SSHL-002T-P0.2', 'AWG 26-30'
  UNION ALL SELECT 'Molex Pico-Lock', 2, '1.5', '5040510201', '5040520098', 'AWG 24-28'
  UNION ALL SELECT 'Molex Pico-Lock', 3, '1.5', '5040510301', '5040520098', 'AWG 24-28'
  UNION ALL SELECT 'Molex Pico-Lock', 4, '1.5', '5040510401', '5040520098', 'AWG 24-28'
  UNION ALL SELECT 'Molex Pico-Lock', 5, '1.5', '5040510501', '5040520098', 'AWG 24-28'
  UNION ALL SELECT 'Molex Pico-Lock', 6, '1.5', '5040510601', '5040520098', 'AWG 24-28'
  UNION ALL SELECT 'Molex Pico-Lock', 7, '1.5', '5040510701', '5040520098', 'AWG 24-28'
  UNION ALL SELECT 'Molex Pico-Lock', 8, '1.5', '5040510801', '5040520098', 'AWG 24-28; uit LDZY-tekening W2464-08-G020007'
  UNION ALL SELECT 'Molex Pico-Lock', 9, '1.5', '5040510901', '5040520098', 'AWG 24-28'
  UNION ALL SELECT 'Molex Pico-Lock', 10, '1.5', '5040511001', '5040520098', 'AWG 24-28'
  UNION ALL SELECT 'Molex Pico-Lock', 11, '1.5', '5040511101', '5040520098', 'AWG 24-28'
  UNION ALL SELECT 'Molex Pico-Lock', 12, '1.5', '5040511201', '5040520098', 'AWG 24-28'
  UNION ALL SELECT 'Molex Pico-Lock', 2, '2.0', '2053410202', '2053425128', 'AWG 24-26; voor AWG 20-22 terminal 2053425028'
  UNION ALL SELECT 'Molex Pico-Lock', 3, '2.0', '2053410203', '2053425128', 'AWG 24-26; voor AWG 20-22 terminal 2053425028'
  UNION ALL SELECT 'Molex Pico-Lock', 4, '2.0', '2053410204', '2053425128', 'AWG 24-26; voor AWG 20-22 terminal 2053425028'
  UNION ALL SELECT 'Molex Pico-Lock', 5, '2.0', '2053410205', '2053425128', 'AWG 24-26; voor AWG 20-22 terminal 2053425028'
  UNION ALL SELECT 'Molex Pico-Lock', 6, '2.0', '2053410206', '2053425128', 'AWG 24-26; voor AWG 20-22 terminal 2053425028'
  UNION ALL SELECT 'Molex Pico-Clasp', 2, '1.0', '5013300200', '5013340100', 'AWG 28-32; frictieslot'
  UNION ALL SELECT 'Molex Pico-Clasp', 3, '1.0', '5013300300', '5013340100', 'AWG 28-32; frictieslot'
  UNION ALL SELECT 'Molex Pico-Clasp', 4, '1.0', '5013300400', '5013340100', 'AWG 28-32; frictieslot'
  UNION ALL SELECT 'Molex Pico-Clasp', 5, '1.0', '5013300500', '5013340100', 'AWG 28-32; frictieslot'
  UNION ALL SELECT 'Molex Pico-Clasp', 6, '1.0', '5013300600', '5013340100', 'AWG 28-32; nummer volgens de reeks, nog nakijken'
  UNION ALL SELECT 'Molex Pico-Clasp', 7, '1.0', '5013300700', '5013340100', 'AWG 28-32; positief slot'
  UNION ALL SELECT 'Molex Pico-Clasp', 8, '1.0', '5013300800', '5013340100', 'AWG 28-32; positief slot'
  UNION ALL SELECT 'Molex Pico-Clasp', 9, '1.0', '5013300900', '5013340100', 'AWG 28-32; positief slot'
  UNION ALL SELECT 'Molex Pico-Clasp', 10, '1.0', '5013301000', '5013340100', 'AWG 28-32; nummer volgens de reeks, nog nakijken'
  UNION ALL SELECT 'Molex Pico-Clasp', 11, '1.0', '5013301100', '5013340100', 'AWG 28-32; nummer volgens de reeks, nog nakijken'
  UNION ALL SELECT 'Molex Pico-Clasp', 12, '1.0', '5013301200', '5013340100', 'AWG 28-32; positief slot'
  UNION ALL SELECT 'Molex Pico-Clasp', 13, '1.0', '5013301300', '5013340100', 'AWG 28-32; nummer volgens de reeks, nog nakijken'
  UNION ALL SELECT 'Molex Pico-Clasp', 14, '1.0', '5013301400', '5013340100', 'AWG 28-32; positief slot'
  UNION ALL SELECT 'Molex Pico-Clasp', 15, '1.0', '5013301500', '5013340100', 'AWG 28-32; positief slot'
  UNION ALL SELECT 'Molex Pico-Clasp dubbelrijig', 20, '1.0', '5011892010', '501193 (uitvoering kiezen)', '2 rijen; terminal uit de 501193-serie: uitvoering en AWG nakijken'
  UNION ALL SELECT 'Molex Pico-Clasp dubbelrijig', 30, '1.0', '5011893010', '501193 (uitvoering kiezen)', '2 rijen; terminal uit de 501193-serie: uitvoering en AWG nakijken'
  UNION ALL SELECT 'Molex Pico-Clasp dubbelrijig', 40, '1.0', '5011894010', '501193 (uitvoering kiezen)', '2 rijen; terminal uit de 501193-serie: uitvoering en AWG nakijken'
  UNION ALL SELECT 'Molex Pico-Clasp dubbelrijig', 50, '1.0', '5011895010', '501193 (uitvoering kiezen)', '2 rijen; terminal uit de 501193-serie: uitvoering en AWG nakijken'
  UNION ALL SELECT 'JST PH', 2, '2.0', 'PHR-2', 'SPH-002T-P0.5S', 'AWG 24-30'
  UNION ALL SELECT 'JST PH', 3, '2.0', 'PHR-3', 'SPH-002T-P0.5S', 'AWG 24-30'
  UNION ALL SELECT 'JST PH', 4, '2.0', 'PHR-4', 'SPH-002T-P0.5S', 'AWG 24-30'
  UNION ALL SELECT 'JST PH', 5, '2.0', 'PHR-5', 'SPH-002T-P0.5S', 'AWG 24-30'
  UNION ALL SELECT 'JST PH', 6, '2.0', 'PHR-6', 'SPH-002T-P0.5S', 'AWG 24-30'
  UNION ALL SELECT 'JST PH', 7, '2.0', 'PHR-7', 'SPH-002T-P0.5S', 'AWG 24-30'
  UNION ALL SELECT 'JST PH', 8, '2.0', 'PHR-8', 'SPH-002T-P0.5S', 'AWG 24-30'
  UNION ALL SELECT 'JST XH', 2, '2.5', 'XHP-2', 'SXH-001T-P0.6', 'AWG 22-28'
  UNION ALL SELECT 'JST XH', 3, '2.5', 'XHP-3', 'SXH-001T-P0.6', 'AWG 22-28'
  UNION ALL SELECT 'JST XH', 4, '2.5', 'XHP-4', 'SXH-001T-P0.6', 'AWG 22-28'
  UNION ALL SELECT 'JST XH', 5, '2.5', 'XHP-5', 'SXH-001T-P0.6', 'AWG 22-28'
  UNION ALL SELECT 'JST XH', 6, '2.5', 'XHP-6', 'SXH-001T-P0.6', 'AWG 22-28'
  UNION ALL SELECT 'JST XH', 7, '2.5', 'XHP-7', 'SXH-001T-P0.6', 'AWG 22-28'
  UNION ALL SELECT 'JST XH', 8, '2.5', 'XHP-8', 'SXH-001T-P0.6', 'AWG 22-28'
       ) n
 WHERE NOT EXISTS (SELECT 1 FROM `KabelConnectoren` k WHERE k.`Housing` = n.`Housing`);

-- ---------------------------------------------------------------------------
--  Rechtzetting: housing 5040510801 is een Molex Pico-Lock. De eerste proefopzet
--  van de configurator noemde hem "Molex Micro-Lock Plus"; die naam staat daardoor
--  in de voorbeeldkabel die al opgeslagen is. Alleen die tekst wordt vervangen, en
--  alleen in kabels met deze housing. Doet niets als het al goed staat.
-- ---------------------------------------------------------------------------
UPDATE `KabelConnectoren`
   SET `Familie` = 'Molex Pico-Lock'
 WHERE `Housing` = '5040510801' AND `Familie` = 'Molex Micro-Lock Plus';

UPDATE `Kabelconfiguraties`
   SET `Spec` = REPLACE(`Spec`, 'Micro-Lock Plus', 'Pico-Lock'),
       `Naam` = REPLACE(`Naam`, 'Micro-Lock Plus', 'Pico-Lock')
 WHERE `Spec` LIKE '%5040510801%' AND (`Spec` LIKE '%Micro-Lock Plus%' OR `Naam` LIKE '%Micro-Lock Plus%');
