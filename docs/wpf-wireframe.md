# WPF-Wireframe

## Zielbild

Der Launcher soll wie ein kleines Control-Center wirken: klar, warm, spielbezogen und technisch sauber.

## Aufbau

### 1. Window Shell

- schmale eigene Titelleiste
- links Icon + `Catan Launcher`
- rechts minimieren, maximieren, schliessen

### 2. Header

Linke Seite:

- `CATAN LAUNCHER`
- Untertitel mit Multiplayer-Hinweis
- kleine Hilfszeile zum Schnellverstaendnis

Rechte Seite:

- kompaktes Audio-Widget
- kleines Lautsprecher-Icon
- Text `Musik`
- Prozentanzeige
- horizontaler Slider

### 3. Action Bar

- grosser Primaerbutton `Catan starten (komplett)`
- Sekundaerbuttons `nur Catan starten`, `Status pruefen`, `Einstellungen`
- rechts Utility-Buttons `Radmin GUI oeffnen`, `dgVoodoo2 oeffnen`

### 4. Hauptbereich

Zweiteilung ueber `Grid`:

- links Dashboard-Karten
- rechts Log-Konsole

Linke Seite:

- Karte `Netzwerk`
- Karte `Konfiguration / Status`
- Karte `Aktionen`
- optional kleines atmosphaerisches Bildfeld darunter

Rechte Seite:

- Karte `Status / Log`
- dunkler Konsolenbereich mit Monospace-Schrift

## Visuelle Leitlinien

- Header: warmes Holzbraun
- Seitenflaeche: helles Pergament
- Karten: leicht abgesetzt, sanfte Rundungen
- Primaeraktion: goldbraun
- Aktiv-Status: gedecktes Brettspiel-Gruen
- Warnungen: warmes Orange
- Log: sehr dunkles Braun statt reines Schwarz

## Interaktionsideen

- sanfter Hover auf Buttons
- Audio-Widget bleibt kompakt
- Slider reagiert sofort
- Status-Badges statt langer Fliesstexte

## Beispiel-Struktur in XAML

```xaml
<Grid>
  <Grid.RowDefinitions>
    <RowDefinition Height="40" />
    <RowDefinition Height="110" />
    <RowDefinition Height="70" />
    <RowDefinition Height="*" />
  </Grid.RowDefinitions>

  <local:TitleBarControl Grid.Row="0" />
  <local:HeaderControl Grid.Row="1" />
  <local:ActionBarControl Grid.Row="2" />

  <Grid Grid.Row="3">
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width="3*" />
      <ColumnDefinition Width="1.1*" />
    </Grid.ColumnDefinitions>

    <local:DashboardView Grid.Column="0" />
    <local:LogView Grid.Column="1" />
  </Grid>
</Grid>
```

## Empfehlung fuer die erste WPF-Version

Version 1 sollte nur diese Flaechen fertig enthalten:

- Header mit Audio-Widget
- Action Bar
- Netzwerkkarte
- Konfigurationskarte
- Aktionskarte
- Logkarte

Alles andere kann danach folgen.
