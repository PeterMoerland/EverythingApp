-- ===========================================================================
--  Lege regels in de tabel Tariefsoort opruimen (gezien 19-11-2026 in de keuzelijst Tarief van de ureninvoer).
--
--  Stand 19-11-2026: naast Travel (1), Normal (2), Overtime (3), Overtime+ (4) en Fixed (5) staan er 15 regels
--  zonder naam, id 18 t/m 32, ontstaan op 17-7-2026 16:42, 18-9-2026 09:24 en 24-9-2026 06:43/06:45 (telkens een
--  paar tegelijk). De MDE-app schrijft nergens in deze tabel; het komt dus van Access of een handmatige bewerking.
--  Geen enkele urenregel verwijst ernaar (Uren gebruikt alleen 1 t/m 5; dat is ook de enige tabel die naar
--  Tariefsoort verwijst), dus weggooien is veilig. De app laat lege regels sinds 19-11-2026 zelf al weg.
--
--  Veilig om nog eens te draaien: de voorwaarde laat regels staan waar toch een urenregel naar verwijst.
-- ===========================================================================

DELETE FROM `Tariefsoort`
 WHERE (`Tariefsoort` IS NULL OR TRIM(`Tariefsoort`) = '')
   AND `Tariefsoort-id` NOT IN (SELECT DISTINCT u.`Tariefsoort-id` FROM `Uren` u WHERE u.`Tariefsoort-id` IS NOT NULL);

-- Nakijken: hier horen alleen nog de vijf echte tariefsoorten te staan
SELECT * FROM `Tariefsoort` ORDER BY `Tariefsoort-id`;

-- ---------------------------------------------------------------------------
--  OPTIONEEL (staat uit): nieuwe lege regels weigeren. Access krijgt dan een foutmelding op het moment dat een
--  scherm een lege tariefsoort wil toevoegen; zo zie je welk scherm het doet. Zet het pas aan als je dat wilt
--  uitzoeken, want de gebruiker die het veroorzaakt krijgt die melding te zien.
-- ---------------------------------------------------------------------------
-- ALTER TABLE `Tariefsoort` ADD CONSTRAINT `CK_Tariefsoort_Naam` CHECK (`Tariefsoort` IS NOT NULL AND TRIM(`Tariefsoort`) <> '');
