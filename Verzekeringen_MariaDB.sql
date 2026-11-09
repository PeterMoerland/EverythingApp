-- ===========================================================================
--  Verzekeringen (wens gebruiker 9-11-2026): alle verzekeringen met de polissen erbij (Financiën > Organisatie >
--  Verzekeringen). Alleen Financial en Directie. De premie kan aan een vaste last hangen (dan telt hij al in
--  Liquiditeit en wordt hij niet dubbel ingevoerd). Een herinnering in de bel zodra de opzegtermijn nadert.
--  Polissen, voorwaarden enz. in de filestore: StoreType 361 'Verzekeringspolis' (MasterID = VerzekeringID).
--
--  Eenmalig uitvoeren op de MDE-database, VOOR het publiceren. Veilig om nog eens te draaien.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `Verzekeringen` (
    `VerzekeringID`        INT NOT NULL AUTO_INCREMENT,
    `BvID`                 INT NULL,                    -- verzekeringnemer: Relations.id_supplier van de eigen BV
    `Soort`                VARCHAR(30) NOT NULL DEFAULT 'overig',
    `Omschrijving`         VARCHAR(200) NOT NULL,
    `VerzekeraarID`        INT NULL,                    -- Relations.id_supplier
    `TussenpersoonID`      INT NULL,                    -- Relations.id_supplier
    `Polisnummer`          VARCHAR(60) NULL,
    `VerzekerdBedrag`      DECIMAL(14,2) NULL,
    `EigenRisico`          DECIMAL(12,2) NULL,
    `Premie`               DECIMAL(12,2) NULL,          -- per termijn, alleen als er geen vaste last aan hangt
    `PremieInterval`       TINYINT NULL,                -- zoals VasteLasten.interval: 1 week, 2 maand, 3 kwartaal, 4 jaar
    `VasteLastID`          INT NULL,                    -- VasteLasten.ID: de premie komt dan daarvandaan
    `Ingangsdatum`         DATE NULL,
    `Contractvervaldatum`  DATE NULL,                   -- eerste (of eerstvolgende) einddatum van het contract
    `StilzwijgendVerlengd` TINYINT(1) NOT NULL DEFAULT 1,
    `VerlengingMaanden`    INT NOT NULL DEFAULT 12,
    `OpzegtermijnMaanden`  INT NULL,
    `Status`               VARCHAR(12) NOT NULL DEFAULT 'actief',   -- actief | opgezegd | beeindigd
    `BeheerderID`          INT NULL,                    -- Werknemers: krijgt ook de herinnering
    `Dekking`              VARCHAR(2000) NULL,
    `Opmerking`            VARCHAR(2000) NULL,
    `GemeldVoor`           DATE NULL,                   -- de vervaldatum waarvoor de herinnering al verstuurd is
    `AangemaaktOp`         DATETIME NOT NULL,
    `AangemaaktDoor`       INT NULL,
    `GewijzigdOp`          DATETIME NULL,
    `GewijzigdDoor`        INT NULL,
    PRIMARY KEY (`VerzekeringID`),
    KEY `IX_Verzekeringen_Bv` (`BvID`),
    KEY `IX_Verzekeringen_VasteLast` (`VasteLastID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

INSERT IGNORE INTO `FileStoreType` (`FilestoreTypeID`, `FileStoreTypeDescription`) VALUES (361, 'Verzekeringspolis');
