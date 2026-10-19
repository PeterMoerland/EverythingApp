-- ===========================================================================
--  ProductTodo: punten die bij een exemplaar nog gedaan moeten worden
--  (bv. "Doorsolderen" bij een print die in een robot zit).
--  Producten ▸ exemplaar ▸ tab Todo, en het overzicht Producten ▸ Todo-overzicht.
--
--  Los van de oude Access-tabel `Todo` (todo's per werkorder/activiteit); die blijft ongemoeid.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `ProductTodo` (
    `ProductTodoID`   INT NOT NULL AUTO_INCREMENT,
    `ProductenID`     INT NOT NULL,                  -- Producten.ProductenID: het exemplaar (robot, print, motor, ...)
    `Wat`             VARCHAR(60) NOT NULL,          -- kort en herbruikbaar, bv. 'Doorsolderen'; het overzicht groepeert hierop
    `Toelichting`     VARCHAR(250) NULL,             -- bv. 'R12 en R13 aan de onderkant'
    `AangemaaktOp`    DATETIME NOT NULL,
    `AangemaaktDoor`  INT NULL,                      -- Werknemers.Werknemer-id
    `GereedOp`        DATETIME NULL,                 -- leeg = staat nog open
    `GereedDoor`      INT NULL,
    PRIMARY KEY (`ProductTodoID`),
    KEY `IX_ProductTodo_Product` (`ProductenID`),
    KEY `IX_ProductTodo_Open` (`GereedOp`, `Wat`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
