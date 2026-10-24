-- ===========================================================================
--  Kostenbegroting per project (Projecten ▸ project ▸ tab Kostenbegroting): wat het project naar verwachting kost,
--  in regels per kostensoort (uren, materiaal, inkoop, overig). Los van het verkoopbudget (BudgetProject), zodat de
--  voortgang ("Bepaal zelf") gemaakte kosten tegen begrote kosten afzet. Wens gebruiker 24-10-2026.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `ProjectKostenbegroting` (
    `KostenbegrotingID` INT NOT NULL AUTO_INCREMENT,
    `ProjectID`         INT NOT NULL,                     -- Projects.Project-Id
    `Soort`             VARCHAR(12) NOT NULL DEFAULT 'uren',   -- uren | materiaal | inkoop | overig
    `Omschrijving`      VARCHAR(150) NOT NULL DEFAULT '',
    `Bedrag`            DECIMAL(12,2) NOT NULL DEFAULT 0, -- kostprijs, excl. btw
    `AangemaaktOp`      DATETIME NOT NULL,
    `AangemaaktDoor`    INT NULL,
    `GewijzigdOp`       DATETIME NULL,
    `GewijzigdDoor`     INT NULL,
    PRIMARY KEY (`KostenbegrotingID`),
    KEY `IX_ProjectKostenbegroting_Project` (`ProjectID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
