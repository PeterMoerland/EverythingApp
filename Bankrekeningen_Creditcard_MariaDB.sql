-- ===========================================================================
--  Creditcards als rekening (wens gebruiker 11-11-2026)
--
--  Bankrekeningen.Soort: 0 = bankrekening (rekeningnummer = IBAN), 1 = creditcard (rekeningnummer = kaartnummer uit de
--  export van de Rabobank, de laatste 4 cijfers, bv. 6481). De transacties van de kaart komen met dat nummer in
--  banktransacties.Rekening, dus koppelen aan facturen, grootboek, BV en rechten werken als bij een gewone rekening.
--  AfgerekendVia: het IBAN waarvan de maandafrekening van de kaart wordt afgeschreven; mag bij meerdere kaarten gelijk
--  zijn. De import van de creditcard-export vult het zelf in als het leeg is.
--
--  Eenmalig uitvoeren op de MDE-database. Zonder deze kolommen kan een creditcard niet worden ingevoerd (alleen IBAN).
-- ===========================================================================

ALTER TABLE `Bankrekeningen`
    ADD COLUMN IF NOT EXISTS `Soort` TINYINT NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS `AfgerekendVia` VARCHAR(34) NULL;

-- Controle:
-- SELECT `Bankrekeningen_id`, `Rekeningnummer`, `OmschrijvingRekening`, `Soort`, `AfgerekendVia` FROM `Bankrekeningen`;
