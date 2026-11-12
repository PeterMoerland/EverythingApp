-- ===========================================================================
--  Opbouw deel 3 (voorstel Sjoerd Quist, 12-11-2026): fouten in de opbouw en todo's per fase.
--
--  1. ProductOpbouwBlokkades: een fout die tijdens de opbouw van een exemplaar is gemeld. Zolang hij open staat is de
--     opbouw van dat exemplaar op slot (afvinken, Fase klaar, Overslaan, Product gereed). Oplossen gebeurt met de hand,
--     met wat er gedaan is. Foto's staan in de FileStore met StoreType 332 (MasterID = BlokkadeID).
--     Dit is iets anders dan de status FAILED (Product afgekeurd): dat is definitief.
--  2. ProductTodo.FaseStapID: een todo-punt kan bij een hoofdfase van de opbouwboom horen. Het staat dan in die fase op
--     de tab Opbouw en moet gereed zijn voor "Fase klaar".
--
--  3. FileStoreType 331: documenten bij een opbouwstap krijgen een eigen soort. Dat was 330, maar 330 is de soort van
--     de bezoekverslagen; een document bij stap 7 zou dan ook bij bezoekverslag 7 staan. Zonder dit script kun je bij
--     een opbouwstap geen document toevoegen (foreign key).
--
--  "Terug naar een fase" heeft geen script nodig: dat zet alleen Klaar op 0 in ProductOpbouwStatus.
--
--  Eerst ProductOpbouwBoom_MariaDB.sql (is op 12-11-2026 al gedraaid). Veilig om nog eens te draaien.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `ProductOpbouwBlokkades` (
    `BlokkadeID`    INT NOT NULL AUTO_INCREMENT,
    `ProductID`     INT NOT NULL,                  -- Producten.ProductenID: het exemplaar
    `FaseStapID`    INT NULL,                      -- ProductOpbouwStappen.OpbouwStapID van de hoofdfase; NULL bij een type zonder fases
    `Melding`       VARCHAR(1000) NOT NULL,        -- wat er mis is
    `GemeldOp`      DATETIME NOT NULL,
    `GemeldDoor`    INT NULL,                      -- Werknemers.Werknemer-id
    `Oplossing`     VARCHAR(1000) NULL,            -- wat er gedaan is
    `OpgelostOp`    DATETIME NULL,                 -- leeg = staat nog open: de opbouw is op slot
    `OpgelostDoor`  INT NULL,
    PRIMARY KEY (`BlokkadeID`),
    KEY `IX_OpbouwBlokkade_Product` (`ProductID`, `OpgelostOp`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Foto's bij een fout (FileStore.StoreType heeft een foreign key naar FileStoreType)
INSERT INTO `FileStoreType` (`FilestoreTypeID`, `FileStoreTypeDescription`)
SELECT 332, 'Opbouw: fout (foto)' FROM DUAL
 WHERE NOT EXISTS (SELECT 1 FROM `FileStoreType` WHERE `FilestoreTypeID` = 332);

-- Documenten bij een opbouwstap: eigen soort (was 330, de soort van de bezoekverslagen)
INSERT INTO `FileStoreType` (`FilestoreTypeID`, `FileStoreTypeDescription`)
SELECT 331, 'Opbouwstap' FROM DUAL
 WHERE NOT EXISTS (SELECT 1 FROM `FileStoreType` WHERE `FilestoreTypeID` = 331);

-- Op 12-11-2026 stonden er geen bestanden met soort 330, dus er hoeft niets om. Controleer het zo:
--   SELECT f.`FileStoreID`, f.`MasterID`, f.`FileNaam`, f.`DatumToegevoegd` FROM `FileStore` f WHERE f.`StoreType` = 330;
-- Staan er toch regels, zet ze dan NIET blind om: 330 kan ook een bijlage van een bezoekverslag zijn (MasterID =
-- Bezoekverslagen.BezoekID). Alleen een regel die bij de tab Opbouw van een producttype is toegevoegd hoort bij een
-- opbouwstap; die ene regel zet je om met
--   UPDATE `FileStore` SET `StoreType` = 331 WHERE `FileStoreID` = <nummer>;

-- Een todo-punt bij een fase
ALTER TABLE `ProductTodo` ADD COLUMN IF NOT EXISTS `FaseStapID` INT NULL;   -- hoofdfase van de opbouwboom; NULL = geen fase
