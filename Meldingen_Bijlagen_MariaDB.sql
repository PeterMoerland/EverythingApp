-- ===========================================================================
--  Plaatjes bij berichten aan collega's (berichtenbox 🔔, wens gebruiker 22-10-2026): filestore-soort 341,
--  MasterID = Meldingen.MeldingID. FileStore.StoreType verwijst naar FileStoreType (FK_FileStore_StoreType); zonder deze
--  regel weigert de database het plaatje (zie ook Verbeterverzoeken_Schermafdruk_MariaDB.sql, soort 340).
--  Alleen de ontvanger en de afzender kunnen het plaatje openen (Program.cs, BestandUitgeven).
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

INSERT IGNORE INTO `FileStoreType` (`FilestoreTypeID`, `FileStoreTypeDescription`) VALUES (341, 'Bericht bijlage');
