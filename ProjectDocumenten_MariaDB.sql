-- ===========================================================================
--  Projectdocumenten krijgen een eigen filestore-soort (21) in plaats van soort 20.
--
--  Waarom: Access bewaart onder soort 20 ("inkomende PO") twee dingen, elk met hun eigen nummer als sleutel:
--    - de PDF van een PO klant            (MasterID = PO_Inkomend.PO_inkomendID)
--    - de documenten van een project      (MasterID = Projects.Project-Id)
--  Een project en een PO klant met hetzelfde nummer zien zo elkaars bestanden. Met een eigen soort voor
--  projectdocumenten kan dat niet meer.
--
--  Wat dit script doet:
--    1. soort 21 'Projectdocument' toevoegen aan FileStoreType;
--    2. de bestaande projectdocumenten van soort 20 naar soort 21 zetten. Een bestand telt als projectdocument als
--       er een project met dat nummer bestaat en het nummer BOVEN het hoogste PO-nummer ligt. Op 12-11-2026 waren
--       dat 14 bestanden bij 9 projecten (project-id 696 t/m 1768); de 145 PO-PDF's (nummers 3 t/m 152) blijven
--       soort 20.
--       Waarom "boven het hoogste PO-nummer" en niet "er bestaat geen PO met dat nummer": er zijn PO's verwijderd
--       (nummers 10, 22, 58, 59) waarvan de PDF nog in de filestore staat. Bij 22, 58 en 59 bestaat toevallig ook een
--       (oud, gesloten) project met dat nummer; die PDF's zijn geen projectdocumenten en blijven dus soort 20.
--
--  Gevolgen:
--    - De app (Projecten ▸ project ▸ tab Documenten) gebruikt soort 21 zodra die bestaat; tot dit script gedraaid is,
--      werkt het tabblad op de oude manier met soort 20.
--    - In Access staan deze documenten daarna niet meer op het tabblad Documenten van het project (Access kijkt naar
--      soort 20). De bestanden zelf blijven op dezelfde plek op schijf staan.
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

-- Vooraf bekijken welke bestanden omgezet worden:
--   SELECT f.`FileStoreID`, f.`MasterID`, p.`Projectnumber`, f.`FileOnly`, f.`DatumToegevoegd`
--     FROM `FileStore` f
--     JOIN `Projects` p ON p.`Project-Id` = f.`MasterID`
--    WHERE f.`StoreType` = 20
--      AND f.`MasterID` > (SELECT COALESCE(MAX(k.`PO_inkomendID`), 0) FROM `PO_Inkomend` k)
--    ORDER BY f.`MasterID`, f.`FileStoreID`;

-- 1. De nieuwe soort (FileStore.StoreType heeft een foreign key naar FileStoreType)
INSERT INTO `FileStoreType` (`FilestoreTypeID`, `FileStoreTypeDescription`)
SELECT 21, 'Projectdocument' FROM DUAL
 WHERE NOT EXISTS (SELECT 1 FROM `FileStoreType` WHERE `FilestoreTypeID` = 21);

-- 2. De bestaande projectdocumenten omzetten
UPDATE `FileStore` f
   SET f.`StoreType` = 21
 WHERE f.`StoreType` = 20
   AND EXISTS (SELECT 1 FROM `Projects` p WHERE p.`Project-Id` = f.`MasterID`)
   AND f.`MasterID` > (SELECT COALESCE(MAX(k.`PO_inkomendID`), 0) FROM `PO_Inkomend` k);

-- Nakijken: hoeveel staan er nu onder soort 21, en wat blijft er onder 20 (dat horen alleen PO-PDF's te zijn)
--   SELECT `StoreType`, COUNT(*) FROM `FileStore` WHERE `StoreType` IN (20, 21) GROUP BY `StoreType`;
