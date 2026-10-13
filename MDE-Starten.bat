@echo off
REM ============================================================
REM  De MDE-app starten — en en passant deze computer aanmelden.
REM
REM  Zet dit bestand (samen met computer-registreren.ps1) op de share of
REM  kopieer beide naar de pc, en maak er een snelkoppeling van op het
REM  bureaublad of in de taakbalk. Het aanmelden gebeurt onzichtbaar op de
REM  achtergrond; de browser opent meteen.
REM ============================================================

start "" /min powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden ^
    -File "%~dp0computer-registreren.ps1"

start "" "https://everything.mde-automation.nl/mde/"
