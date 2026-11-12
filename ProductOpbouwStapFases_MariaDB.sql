-- ===========================================================================
--  Programmeren en testen in de fases (voorstel Sjoerd Quist, 12-11-2026).
--
--  Per producttype kies je bij elk blok van Programmeren en testen (Automatisch uitlezen, Programmeren, Testen,
--  EtherCAT node-adressen, Duurtest) in welke hoofdfase van de opbouw het hoort
--  (Producten ▸ producttype ▸ tab Techniek ▸ Stappen). Bij een exemplaar staat het blok dan in die fase op het
--  tabblad Opbouw, en is het daarbuiten op slot: zo haal je geen duurtest op bij een robot die net is aangemaakt.
--  Wie het recht Productcorrectie heeft (ProductOpbouwDeel5_MariaDB.sql) of Directie kan een blok buiten zijn fase
--  om gebruiken, bijvoorbeeld om een product dat gereed is nog eens uit te lezen.
--
--  Zolang er geen fase is gekozen verandert er niets: een blok zonder fase werkt zoals voorheen.
--  Eerst ProductOpbouwBoom_MariaDB.sql (is al gedraaid). Veilig om nog eens te draaien.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `ProductStapFases` (
    `ProductNameID` INT NOT NULL,              -- het producttype
    `Stap`          VARCHAR(20) NOT NULL,      -- Uitlezen, Programmeren, Testen, NodeAdres of Duurtest
    `FaseStapID`    INT NOT NULL,              -- ProductOpbouwStappen.OpbouwStapID van de hoofdfase
    PRIMARY KEY (`ProductNameID`, `Stap`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
