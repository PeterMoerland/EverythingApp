-- Historiesoort 'Reparatie' toevoegen (keuzelijst bij Producten ▸ exemplaar ▸ Historie).
-- De app leest de soorten uit de tabel met historietypes; in de Access-migratie heet die
-- `HistorieTypes` met kolom `HistoryType`. Draai het blok dat bij jouw tabel hoort.

-- Controleer eerst welke tabel bestaat:
SELECT table_name, column_name
  FROM information_schema.columns
 WHERE table_schema = DATABASE()
   AND table_name IN ('HistorieTypes', 'ProductHistorieType', 'HistorieType', 'ProductHistorieTypes')
 ORDER BY table_name, ordinal_position;

-- Gangbare variant (HistorieTypes.HistoryType):
INSERT INTO `HistorieTypes` (`HistoryType`)
SELECT 'Reparatie'
 WHERE NOT EXISTS (SELECT 1 FROM `HistorieTypes` WHERE `HistoryType` = 'Reparatie');

-- Daarna: de app leest de lijst één keer per proces — de MDEApp-dienst herstarten (of opnieuw publiceren).
