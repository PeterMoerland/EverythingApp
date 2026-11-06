-- Wie staat er in "Wie ben jij?" op een werkplek (wens gebruiker 6-11-2026): standaard iedereen met het vinkje
-- Werkplek gebruiker; staan er voor een werkplek rijen in deze tabel, dan alleen die mensen (bv. de Fusion-werkplek van
-- een inlener niet tussen de robotproductie, en hij niet in het lijstje van de productiewerkplekken).
-- Een keer uitvoeren; veilig om opnieuw te draaien.

CREATE TABLE IF NOT EXISTS `WerkplekPersonen` (
    `WerkplekID`   INT NOT NULL,                                 -- Werkplekken.WerkplekID
    `WerknemerID`  INT NOT NULL,                                 -- Werknemers.Werknemer-id
    PRIMARY KEY (`WerkplekID`, `WerknemerID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
