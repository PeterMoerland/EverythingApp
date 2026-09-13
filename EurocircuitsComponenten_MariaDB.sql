-- ---------------------------------------------------------------------------
-- Standaardcomponenten van Eurocircuits (weerstanden en condensatoren).
-- Gevuld vanuit de CSV-exports van Eurocircuits via de app:
-- Tools ▸ Fusion ▸ Standaardcomponenten. Het GPN (General Part Number,
-- bv. GPR04021R2 of GPC0402100-25) is de unieke sleutel: opnieuw importeren
-- werkt bestaande regels bij en maakt dus geen dubbelen.
--
-- WaardeBasis is de genormaliseerde waarde in de grondeenheid (ohm of farad),
-- zodat er te sorteren en te filteren valt; WaardeTekst is hoe de export hem
-- schreef ("1,1 Ω", "0,5 pF").
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS EurocircuitsComponenten (
    EcComponentID   int(10)      NOT NULL AUTO_INCREMENT,
    Gpn             varchar(30)  NOT NULL,               -- uniek, bv. GPR04021R2
    Soort           char(1)      NOT NULL,               -- R = weerstand, C = condensator
    Grootte         varchar(10)  NOT NULL,               -- 0402, 0603, 0805, ...
    WaardeBasis     double       NOT NULL,               -- in ohm of farad
    WaardeTekst     varchar(30)  NOT NULL,               -- "1,1 Ω", "0,5 pF"
    Tolerantie      varchar(20)  NULL,                   -- "1", "±0.1pF"
    Vermogen        varchar(20)  NULL,                   -- alleen R: "62.5mW"
    Spanning        varchar(20)  NULL,                   -- "50V"
    Dielectricum    varchar(40)  NULL,                   -- alleen C: "C0G, NP0", "X7R"
    Omschrijving    varchar(255) NULL,
    last_updated    timestamp    NOT NULL DEFAULT CURRENT_TIMESTAMP
                                 ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (EcComponentID),
    UNIQUE KEY UX_EcComponenten_Gpn (Gpn),
    KEY IX_EcComponenten_SoortGrootte (Soort, Grootte)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
