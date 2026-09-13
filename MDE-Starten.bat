@echo off
REM ============================================================
REM  De MDE-app starten — en en passant deze laptop aanmelden.
REM
REM  Zet dit bestand (samen met laptop-registreren.ps1) op de share of
REM  kopieer beide naar de laptop, en maak er een snelkoppeling van op het
REM  bureaublad of in de taakbalk. Het aanmelden gebeurt onzichtbaar op de
REM  achtergrond; de browser opent meteen.
REM ============================================================

start "" /min powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden ^
    -File "%~dp0laptop-registreren.ps1"

start "" "https://everything.mde-automation.nl/mde/"
