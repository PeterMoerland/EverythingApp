-- ---------------------------------------------------------------------------
-- De onderdelenbibliotheek van JLCPCB: per component het C-nummer
-- (ComponentCode), de MPN (Model) en de behuizing (Specification).
-- Gevuld via de knop 'Onderdelenbibliotheek verversen' op Tools ▸ Fusion ▸
-- Converteer naar CAM (interface getComponentLibraryList van het JLCPCB
-- Open Platform). Gebruikt om bij de JLCPCB-export de kolom 'JLCPCB Part #'
-- automatisch te vullen op basis van de MPN.
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS JlcComponenten (
    JlcComponentID  int(10)      NOT NULL AUTO_INCREMENT,
    ComponentCode   varchar(20)  NOT NULL,     -- C-nummer, bv. C14663
    Model           varchar(150) NOT NULL,     -- MPN
    Specification   varchar(150) NULL,         -- behuizing, bv. 0402
    last_updated    timestamp    NOT NULL DEFAULT CURRENT_TIMESTAMP
                                 ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (JlcComponentID),
    UNIQUE KEY UX_JlcComponenten_Code (ComponentCode),
    KEY IX_JlcComponenten_Model (Model)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
