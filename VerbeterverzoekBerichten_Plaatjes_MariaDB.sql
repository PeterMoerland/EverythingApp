-- ===========================================================================
--  Plaatjes bij een bericht in het gesprek onder een verbeterverzoek (wens gebruiker 19-11-2026):
--  filestore-soort 343, MasterID = VerbeterverzoekBerichten.BerichtID.
--  FileStore.StoreType verwijst naar FileStoreType (FK_FileStore_StoreType); zonder deze regel weigert de database het
--  plaatje. Het bericht zelf wordt dan wel geplaatst, met een melding dat het plaatje niet bewaard is.
--  (Zie ook: 340 schermafdruk bij een verzoek, 341 bijlage bij een bericht in de berichtenbox.)
--
--  Eenmalig uitvoeren op de MDE-database. Veilig om nog eens te draaien.
-- ===========================================================================

INSERT IGNORE INTO `FileStoreType` (`FilestoreTypeID`, `FileStoreTypeDescription`) VALUES (343, 'Verbeterverzoek bericht bijlage');
