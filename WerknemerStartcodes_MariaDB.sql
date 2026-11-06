-- Startcodes voor werkplekken (wens gebruiker 6-11-2026): iemand zonder Google-account (inlener, stagiair) of die zijn
-- pincode vergat, krijgt van een beheerder (Beheer > Werkplekken) een eenmalige startcode van 6 cijfers. Op een
-- werkplek kiest hij zijn naam, vult de startcode in en kiest dan zelf een pincode. Alleen een hash, nooit de code zelf.
-- Een keer uitvoeren; veilig om opnieuw te draaien.

CREATE TABLE IF NOT EXISTS `WerknemerStartcodes` (
    `WerknemerID`     INT NOT NULL,                              -- Werknemers.Werknemer-id
    `Hash`            VARCHAR(200) NOT NULL,                     -- PBKDF2, zoals WerknemerPincodes
    `GeldigTot`       DATETIME NOT NULL,
    `AangemaaktDoor`  INT NULL,                                  -- Werknemers.Werknemer-id van de beheerder
    `AangemaaktOp`    DATETIME NOT NULL,
    PRIMARY KEY (`WerknemerID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
