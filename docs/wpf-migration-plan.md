# WPF-Migrationsplan

## Ziel

Den bestehenden WinForms-Launcher schrittweise in eine modernere WPF-Oberflaeche ueberfuehren, ohne die funktionierende Prozess-, INI- und Systemlogik auf einmal neu schreiben zu muessen.

## Empfohlene Reihenfolge

1. Bestehende Launcher-Logik aus der Form herausloesen
2. WPF-Oberflaeche als neues Projekt daneben aufbauen
3. Beide UIs voruebergehend parallel lauffaehig halten
4. WinForms nach erfolgreicher Migration stilllegen

## Phase 1 - Logik entkoppeln

Die aktuelle Datei `Catan/CatanLauncher.cs` enthaelt sehr viel UI- und Fachlogik in einer Klasse. Vor der Vollmigration sollten diese Bereiche getrennt werden:

- `ConfigService`
  - `LoadIni`, `SaveIni`, Pfad-Normalisierung
- `LaunchService`
  - Spielstart, dgVoodoo, Radmin, Admin-Neustart
- `SystemCheckService`
  - DirectPlay, Firewall, Versionen, Netzwerkstatus
- `MusicService`
  - Wiedergabe, Mute, Lautstaerke, Persistenz
- `LogService`
  - UI-unabhaengige Logevents statt direktem Schreiben ins RichTextBox-Control

## Phase 2 - WPF-Grundstruktur

Neues Projekt: `Catan.WpfLauncher`

Struktur:

- `Views/`
- `ViewModels/`
- `Services/`
- `Models/`
- `Themes/`
- `Assets/`

Technischer Stil:

- MVVM-light ohne schweres Framework zum Start
- `INotifyPropertyChanged` fuer Statuswerte
- Commands fuer Buttons
- `ResourceDictionary` fuer Farben, Buttons, Karten und Typografie

## Phase 3 - Zuerst nur die Startseite migrieren

Als erste WPF-Seite nur den Hauptlauncher bauen:

- Header
- Audio-Widget
- Hauptaktionsleiste
- Netzwerk-Karte
- Konfigurations-Karte
- Aktionen-Karte
- Log-Bereich

Noch nicht zuerst migrieren:

- Spezialdialoge
- selten genutzte Settings-Details
- Legacy-Helfer direkt in der UI

## Phase 4 - Bestehende Funktionen anbinden

Die WPF-Buttons sollten zunaechst dieselben Services verwenden wie WinForms:

- `Catan komplett starten`
- `nur Catan starten`
- `Status pruefen`
- `Radmin GUI oeffnen`
- `dgVoodoo2 oeffnen`
- Musik an/aus und Lautstaerke

So bleibt das Verhalten gleich, waehrend nur die Darstellung modernisiert wird.

## Phase 5 - Settings modernisieren

Danach das Einstellungsfenster separat in WPF ersetzen:

- Pfade fuer `Catan.exe`, `dgVoodooCpl.exe`, `RvRvpnGui.exe`
- Validierungsanzeigen direkt im Formular
- Speichern in `config.ini`

## Phase 6 - WinForms entfernen

Erst wenn das WPF-Projekt diese Punkte abdeckt, sollte WinForms weg:

- Launcher-Ansicht fertig
- Settings fertig
- Musik-Widget fertig
- Logging und Statusanzeigen getestet
- Spielstart, Firewall, DirectPlay und Radmin getestet

## Architektur-Empfehlung

Kurzfristig:

- WinForms bleibt funktional erhalten
- WPF-Projekt wird als neues Frontend aufgebaut

Mittelfristig:

- gemeinsame Services in eigene Dateien auslagern
- UI-spezifischen Code aus `Catan/CatanLauncher.cs` entfernen

Langfristig:

- WPF als Hauptprojekt
- WinForms nur noch als Fallback oder komplett entfernen

## Risiken

- Die aktuelle Monolith-Datei erschwert das Wiederverwenden einzelner Funktionen
- Prozess- und Adminlogik muss sauber aus der UI geloest werden
- Musik, Logging und Statusupdates sollten nicht direkt Controls manipulieren

## Empfehlung fuer den naechsten praktischen Schritt

1. WPF-Launcher als neues Projekt parallel anlegen
2. Startseite visuell fertig bauen
3. Danach Fachlogik schrittweise in Services verschieben
