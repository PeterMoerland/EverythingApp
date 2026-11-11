-- ===========================================================================
--  Bankrekeningen van een relatie als lijst (wens gebruiker 11-11-2026), na RelatieIbans_MariaDB.sql.
--
--  RelatieIbans krijgt BIC, een omschrijving en "Standaard". De standaardrekening is wat de app ook in Relations.IBAN en
--  Relations.BIC zet: SEPA-betalingen klaarzetten, factuur inlezen (fraudecontrole op de IBAN), Access en de BV-rekeningen
--  lezen nog steeds die kolommen. In Relaties staat de lijst op het tabblad Bankrekeningen; de velden IBAN en BIC op
--  Opties verdwijnen zodra dit script gedraaid is.
--
--  Het tweede deel zet de huidige IBAN/BIC van elke relatie als standaardrekening in de lijst (11-11-2026: 558
--  relaties met een IBAN, 554 met een BIC).
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

ALTER TABLE `RelatieIbans`
    ADD COLUMN IF NOT EXISTS `BIC`           VARCHAR(11)  NULL,
    ADD COLUMN IF NOT EXISTS `Omschrijving`  VARCHAR(100) NULL,
    ADD COLUMN IF NOT EXISTS `Standaard`     TINYINT(1)   NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS `GewijzigdOp`   DATETIME     NULL,
    ADD COLUMN IF NOT EXISTS `GewijzigdDoor` INT          NULL;

-- de huidige IBAN/BIC van de relatie als standaardrekening
INSERT IGNORE INTO `RelatieIbans` (`RelatieID`, `IBAN`, `BIC`, `Standaard`, `Bron`, `AangemaaktOp`)
SELECT r.`id_supplier`, UPPER(REPLACE(REPLACE(r.`IBAN`, ' ', ''), '-', '')),
       NULLIF(UPPER(REPLACE(COALESCE(r.`BIC`, ''), ' ', '')), ''), 1, 'relatie', NOW()
  FROM `Relations` r
 WHERE COALESCE(TRIM(r.`IBAN`), '') <> '';

-- stond die rekening er al (geleerd uit een koppeling), dan wordt hij de standaard en krijgt hij de BIC
UPDATE `RelatieIbans` ri
  JOIN `Relations` r ON r.`id_supplier` = ri.`RelatieID`
   SET ri.`Standaard` = 1,
       ri.`BIC` = COALESCE(ri.`BIC`, NULLIF(UPPER(REPLACE(COALESCE(r.`BIC`, ''), ' ', '')), ''))
 WHERE UPPER(REPLACE(REPLACE(r.`IBAN`, ' ', ''), '-', '')) = ri.`IBAN`;
