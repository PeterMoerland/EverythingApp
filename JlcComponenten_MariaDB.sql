-- ---------------------------------------------------------------------------
-- De onderdelenlijst van JLCPCB: per component het C-nummer (LCSC-nummer),
-- de MPN, de fabrikant, de behuizing en of het een 'basic' onderdeel is.
-- Gevuld vanuit de app (Tools ▸ Fusion ▸ JLCPCB, knop 'Lijst ophalen'), die
-- de interface getComponentInfos pagina voor pagina leegt — duizend
-- onderdelen per keer. Daarmee vult de JLCPCB-export de kolom
-- 'JLCPCB Part #' automatisch op basis van de MPN.
--
-- Draai dit script ook als de tabel al bestond: de ALTER-regels onderaan
-- voegen de nieuwe kolommen toe. Een melding 'Duplicate column name' bij
-- een tweede keer draaien is onschuldig.
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS JlcComponenten (
    JlcComponentID  int(10)      NOT NULL AUTO_INCREMENT,
    ComponentCode   varchar(20)  NOT NULL,     -- C-nummer, bv. C1002
    Model           varchar(150) NOT NULL,     -- MPN, bv. GZ1608D601TF
    Specification   varchar(150) NULL,         -- behuizing, bv. 0603
    Fabrikant       varchar(100) NULL,
    Soort           varchar(20)  NULL,         -- libraryType: base of extended
    Voorraad        int(11)      NULL,
    Categorie       varchar(150) NULL,
    last_updated    timestamp    NOT NULL DEFAULT CURRENT_TIMESTAMP
                                 ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (JlcComponentID),
    UNIQUE KEY UX_JlcComponenten_Code (ComponentCode),
    KEY IX_JlcComponenten_Model (Model)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Bestond de tabel al in de oude vorm? Dan deze kolommen erbij:
ALTER TABLE JlcComponenten ADD COLUMN Fabrikant varchar(100) NULL;
ALTER TABLE JlcComponenten ADD COLUMN Soort     varchar(20)  NULL;
ALTER TABLE JlcComponenten ADD COLUMN Voorraad  int(11)      NULL;
ALTER TABLE JlcComponenten ADD COLUMN Categorie varchar(150) NULL;
