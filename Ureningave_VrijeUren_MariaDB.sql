-- ===========================================================================
--  Ureningave, "Boek 8 uur" (punt van Gerard, 19-11-2026): de werkorders voor vrije uren bijwerken.
--
--  De knop boekt 8 uur op de werkorder uit GeneralSettingsSpecialOrders (`Order` = Workorders.Werkbon-id).
--  Die tabel was verouderd (gezien 19-11-2026):
--     1  Vrije dag  -> 37    1000-02 Vakantie              GESLOTEN
--     2  Dokter     -> 101   1000-06 Dokter Tandarts bezoek  open, in 2026 niet gebruikt (laten staan)
--     4  Feestdag   -> 36    1000-01 Feestdag              GESLOTEN
--     5  Ziek       -> 2600  220048-01 "Versturen"          FOUT (open, dus daar zou op geboekt worden)
--  In 2026 wordt in de praktijk geboekt op:
--     970   21634-01  MDE Vrije tijd / Vrije dag        (152 regels in 2026)
--     1402  21634-03  MDE Vrije tijd / Feestdag         (14 regels)
--     7007  220047-07 Ziekmeldingen / Ziekmeldingen 2026 (9 regels)
--
--  CONTROLEER de drie werkorders hieronder voordat je dit uitvoert. Uitvoeren VOOR het publiceren van de app:
--  zonder dit script weigert de app Vrije dag en Feestdag (werkorder gesloten) en boekt hij Ziek op "Versturen".
--  LET OP: Ziek heeft elk jaar een nieuwe werkorder (Ziekmeldingen 2027 …); werk regel 5 dan bij.
--  Veilig om nog eens te draaien.
-- ===========================================================================

UPDATE `GeneralSettingsSpecialOrders` SET `Order` = 970  WHERE `GeneralSettingsSpecialOrders_id` = 1;   -- Vrije dag
UPDATE `GeneralSettingsSpecialOrders` SET `Order` = 1402 WHERE `GeneralSettingsSpecialOrders_id` = 4;   -- Feestdag
UPDATE `GeneralSettingsSpecialOrders` SET `Order` = 7007 WHERE `GeneralSettingsSpecialOrders_id` = 5;   -- Ziek (2026)

-- Nakijken: elke regel hoort een open werkorder te tonen (Workorderstatus = 1)
SELECT s.`GeneralSettingsSpecialOrders_id` AS id, s.`Description`, s.`Order`,
       CONCAT(p.`Projectnumber`, '-', LPAD(w.`Werkbonnummer`, 2, '0')) AS werkorder, w.`Werkbonomschrijving`, w.`Workorderstatus`
  FROM `GeneralSettingsSpecialOrders` s
  LEFT JOIN `Workorders` w ON w.`Werkbon-id` = s.`Order`
  LEFT JOIN `Projects` p ON p.`Project-Id` = w.`Project-id`;
