-- ===========================================================================
--  Schermafdrukken bij verbeterverzoeken (lampje 💡): de filestore-soort 340.
--  FileStore.StoreType verwijst naar FileStoreType (FK_FileStore_StoreType); zonder deze regel weigert de database
--  de schermafdruk en krijgt de collega "konden niet worden opgeslagen" (gezien 22-10-2026, verzoek 20).
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

INSERT IGNORE INTO `FileStoreType` (`FilestoreTypeID`, `FileStoreTypeDescription`) VALUES (340, 'Verbeterverzoek schermafdruk');
