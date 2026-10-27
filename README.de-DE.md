<h1 align="center">KeeStashDock</h1>

<p align="center">
  <img src="docs/assets/app-icon.png" width="112" alt="Anwendungssymbol von KeeStashDock">
</p>

<p align="center">
  <strong>Ein portables Windows-Dock für Textbausteine, Screenshots und die Wiederverwendung von Text.</strong><br>
  Erfassen, was Sie brauchen, lokal organisieren und bei Bedarf übersetzen.
</p>

<p align="center">
  <a href="README.md" lang="en">English</a> ·
  <a href="README.zh-CN.md" lang="zh-Hans">简体中文</a> ·
  <a href="README.zh-TW.md" lang="zh-Hant">繁體中文</a> ·
  <a href="README.ja-JP.md" lang="ja">日本語</a> ·
  <a href="README.ko-KR.md" lang="ko">한국어</a> ·
  <a href="README.de-DE.md" lang="de">Deutsch</a> ·
  <a href="README.fr-FR.md" lang="fr">Français</a> ·
  <a href="README.es-ES.md" lang="es">Español</a> ·
  <a href="README.pt-BR.md" lang="pt-BR">Português (Brasil)</a>
</p>

<p align="center">
  <img alt="Version 1.3.0" src="docs/assets/badges/version.svg">
  <img alt=".NET 10" src="docs/assets/badges/dotnet.svg">
  <img alt="Windows 10 und 11" src="docs/assets/badges/windows.svg">
  <img alt="Apache-2.0-Lizenz" src="docs/assets/badges/license.svg">
</p>

**Version 1.3.0** · [Versionshinweise](docs/releases/1.3.0-notes.md) · [Prüfung und Veröffentlichungsstatus](docs/releases/1.3.0.md)

**Veröffentlichungsvorbereitung:** Dieser Branch dokumentiert 1.3.0. Die offizielle Veröffentlichung steht noch aus; die [neueste veröffentlichte Version](https://github.com/Keegankei/KeeStashDock/releases/latest) ist derzeit **1.2.3**. Das Prüfprotokoll hält die noch offenen Freigabeprüfungen fest.

Neue Vorschauen zeigen die **englische Oberfläche von 1.3.0** mit isolierten Beispieldaten. Ältere Screenshots und GIFs bleiben als **1.2.3** gekennzeichnet; illustrierte Designs und kombinierte Blasenaktionen sind optional.

[Einstieg](#get-started) · [Funktionen](#features) · [Tastenkürzel](#shortcuts) · [Daten und Upgrades](#data-and-upgrades) · [Entwicklung](#development) · [Lizenz](#license)

<a id="get-started"></a>
## Einstieg

**[Neueste veröffentlichte Version herunterladen](https://github.com/Keegankei/KeeStashDock/releases/latest)**

Für 1.3.0 sind unter **Assets** die Dateien `KeeStashDock-1.3.0-win-x64.zip` und `KeeStashDock-1.3.0-win-x64.zip.sha256` vorgesehen. Diese Namen bezeichnen die geplanten Veröffentlichungsdateien, keinen bereits verfügbaren Download.

1. Laden Sie unter **Assets** das ZIP-Paket der Anwendung herunter, nicht das für die Entwicklung gedachte **Source code**-Archiv von GitHub.
2. Entpacken Sie das vollständige Paket in einen beschreibbaren Ordner und starten Sie `KeeStashDock.exe`. Lassen Sie alle mitgelieferten Ordner beisammen.
3. Drücken Sie **Alt+Q**, um das Schnellpanel anzuzeigen. Geben Sie einen Textbaustein ein und speichern Sie ihn mit **Enter**; ein Klick auf die Karte kopiert ihn.
4. Öffnen Sie **Einstellungen** über das Panel oder den Infobereich der Taskleiste, um Sprache, Design und optionale Komponenten auszuwählen.

Das Windows-x64-Paket enthält .NET; eine separate .NET-Installation ist nicht nötig. Windows 11 ist die primäre Zielplattform, Windows 10 22H2 wird nach Möglichkeit unterstützt. OCR und Offline-Übersetzung benötigen möglicherweise die Visual-C++-Laufzeit von Microsoft, die über die Komponenteninstallation in der App installiert werden kann. Den getesteten Umfang beschreibt das [Abnahmeprotokoll für 1.3.0](docs/releases/1.3.0.md).

<details>
<summary>Das heruntergeladene ZIP prüfen</summary>

Führen Sie diesen Befehl im Downloadordner aus und vergleichen Sie das Ergebnis mit der zugehörigen `.sha256`-Datei:

```powershell
Get-FileHash .\KeeStashDock-1.3.0-win-x64.zip -Algorithm SHA256
```

Eine Prüfsumme prüft die Dateiintegrität; sie ist keine Herausgebersignatur. Das aktuelle Paket ist nicht signiert.

</details>

**Voreinstellungen bei Neuinstallation · 1.3.0:** Englisch; **Alt+Q** öffnet das Panel, **Alt+W** startet einen Screenshot. OCR und lokale Übersetzung sind nicht vorinstalliert. Updates behalten gespeicherte Einstellungen bei.

Die Auswahlblase ist mit **Nur kopieren** aktiviert; Übersetzungsanbieter ist **Google**. Blasenstil: Standard, Textleuchten aus, Schatten an. Autostart, automatische Zwischenablage-Erfassung und direktes Einfügen sind aus. Automatisches Ausblenden am Bildschirmrand ist an: **2 s** zum Einklappen, **0,1 s** zum Aufklappen. Systemdesign, **Standard**-Modus, **Ziehen mit gedrückter rechter Maustaste**, automatische Textfarben und **90 %** Deckkraft der Panel-Steuerelemente sind voreingestellt. Kein Hintergrundbild; Überlagerung **20 %**, Unschärfe aus, gespeicherter Radius **4 px**. Automatische Sicherungen sind alle **24 Stunden** aktiviert.

Die Textgröße beginnt bei **100 %**, die Deckkraft der Einstellungsoberflächen bei **100 %**. Milchglaseffekte für Einstellungen und Blasen sind **ausgeschaltet**. Die mitgelieferten Designs sind optional und werden nicht automatisch angewendet.

<details>
<summary>Einstellungen einer Neuinstallation · 1.2.3 · englische Oberfläche</summary>

<p align="center"><img src="docs/images/default-settings.png" width="720" alt="Einstellungen von 1.2.3 mit Englisch, Alt+Q und Alt+W als Voreinstellungen"></p>

</details>

<a id="features"></a>
## Funktionen

### Neu in 1.3.0

| Bereich | Verwendung |
| --- | --- |
| Suche im Schnellpanel | Drücken Sie bei aktivem Panel **Strg+F**. Durchsuchen Sie Titel und gespeicherten Text einschließlich vorhandener OCR-Ergebnisse und kombinieren Sie Gruppen-, Tag- und Text/Bild/Vorlagenfilter. Ergebnisse werden in Seiten mit 50 Einträgen geladen. **Enter** kopiert das ausgewählte Ergebnis, **Esc** kehrt zur Erfassung zurück. Der Eingabeentwurf bleibt erhalten. |
| Sammelaktionen | Wählen Sie Einträge im Arbeitsbereich, um sie in Gruppen zu verschieben, Tags hinzuzufügen/zu entfernen, anzuheften/zu lösen, in den Papierkorb zu verschieben oder wiederherzustellen. **Strg+A wählt nur geladene Ergebnisse aus.** Eine Aktion wird vollständig übernommen oder bei Konflikten ohne Änderungen abgebrochen. Das Verschieben in den Papierkorb lässt sich rückgängig machen, solange die Einträge unverändert sind. |
| Textvorlagen | Kennzeichnen Sie einen Texteintrag ausdrücklich als Vorlage. Nutzen Sie `{{date}}`, `{{time}}` und Variablen wie `{{name}}`, füllen Sie sie aus und prüfen Sie die Vorschau vor dem Kopieren. Das Original bleibt erhalten; Skripte und Ausdrücke werden nicht ausgeführt. Gruppenpakete mit Vorlagen verwenden Format v3. |
| Vollständige Designs | Zeigen Sie abgestimmte helle/dunkle Farben und Hintergründe für die App in der Vorschau an und wenden Sie sie an. Beide Varianten und Bilder lassen sich als gemeinsames Design-ZIP importieren/exportieren. Designs sind vom Standard-/Raummodus unabhängig. Abbrechen der Vorschau stellt das vorige Erscheinungsbild wieder her. |
| Globale Textgröße | Wählen Sie unter **Einstellungen > Erscheinungsbild > Textgröße** einen Wert von **90 % bis 150 %** für Schnellpanel, Arbeitsbereich, Einstellungen und weitere App-Texte. **100 %** stellt die ursprüngliche Größe wieder her. |
| Neuer Arbeitsbereich | Navigation, Eintragsliste und Editor sind auf das Design der Einstellungen abgestimmt. Listen und Sicherungszeilen erhalten im dunklen Modus passende Hintergründe und gut lesbaren Text. |
| Kompakte Blasen | Kopieraktionen direkt neben Original und Ergebnis sowie ein kompakteres Layout für Zielsprache, Gruppe und Speichern. Standardstil und Wisteria Atelier verwenden dieselben Bedienelemente mit abgestimmten Farben. |
| Deckkraft und Milchglas | Die Einstellungsoberflächen lassen sich unabhängig auf **10 % bis 100 %** einstellen. Optionaler Milchglaseffekt zeichnet das Hintergrundbild der Einstellungen mit **1–18 px** weich. Blasen nutzen die Windows-Komposition mit einer undurchsichtigen Ersatzdarstellung. Text bleibt scharf. |

Neuinstallationen starten weiterhin auf Englisch, mit dem Systemdesign, im Standardmodus und ohne Hintergrundbild. Ein Upgrade aktiviert kein illustriertes Design automatisch und setzt gespeicherte Einstellungen nicht zurück.

**Einstellungen:** Die Navigation gliedert sich in **Allgemein**, **Erfassen & Übersetzen**, **Darstellung**, **Lokale Komponenten**, **Sicherung & Daten** und **Über**. Abhängige Optionen erscheinen bei Bedarf; ausgeblendete Werte bleiben gespeichert.

Unter **Erfassen & Übersetzen** lassen sich die Zielsprachauswahl und die Gruppen-Speicherfunktionen im Ergebnisfenster unabhängig einblenden. Beide sind standardmäßig aktiv und erscheinen nach OCR oder Übersetzung. Auch ausgeschaltet bleiben Ergebnis und Kopieraktionen verfügbar.

Den Sicherungsordner wählen Sie unter **Sicherung & Daten**, die separaten OCR- und Offline-Übersetzungsordner unter **Lokale Komponenten**. Ein Ordnerwechsel **verschiebt keine vorhandenen Dateien**; bei einem leeren neuen Ordner installieren Sie die Komponente erneut. **Über** zeigt den aktiven Datenordner schreibgeschützt an. Nutzen Sie Export und Import, um die Bibliothek sicher zu verschieben.

Die Vorschauen für Arbeitsbereich, Designs und Blasen in **1.3.0** sind außerhalb des sichtbaren Desktops gerenderte echte WPF-Steuerelemente mit fiktivem Inhalt. Die übrigen Screenshots und GIFs aus **1.2.3** zeigen bestehende Abläufe. Zeiger im Betrachtungs-GIF und Veröffentlichung in der Updatevorschau sind simuliert. Installations- und Datenpfade sind verdeckt; persönliche Desktop- oder Zwischenablageinhalte fehlen. [Bildquellen und Reproduktion](docs/images/README.md).

<p align="center"><img src="docs/images/standard-panel-workflow.gif" width="400" alt="Ablauf im Standardpanel von 1.2.3: Text eingeben, speichern, Karte anheften und lösen"></p>

*1.2.3 · Standardmodus · Eingeben, Speichern, Anheften und Lösen · englische Oberfläche. Skriptgesteuerte Bedienung echter Steuerelemente mit Beispieldaten.*

### 1. Erfassen, finden und organisieren

<p align="center"><img src="docs/images/1.3.0/workbench-light.png" width="900" alt="1.3.0-Arbeitsbereich mit Suche, Mehrfachauswahl und Vorlageneditor im optionalen hellen Wisteria-Atelier-Design"></p>

*1.3.0 · Arbeitsbereich und Textvorlageneditor · Optionales helles Wisteria-Atelier-Design.*

Verwenden Sie das Schnellpanel für kurze Eingaben und die **Werkbank** zur Verwaltung Ihrer Sammlung. Öffnen Sie die Werkbank über die entsprechende Schaltfläche im Panel oder den Infobereich der Taskleiste.

| Werkzeug | Möglichkeiten |
| --- | --- |
| Schnelleingabe und Suche | Text mit Enter speichern; mit Alt+Enter oder Shift+Enter Zeilenumbrüche einfügen. Ctrl+F öffnet die Suche im Schnellpanel. Panel und Arbeitsbereich unterstützen Gruppen-, Tag- und Typfilter. |
| In das Dock ziehen | Text ablegen, um ihn zu speichern, oder Dateien und Ordner ablegen, um deren Pfade zu speichern. Ein Klick auf eine Pfadkarte öffnet das Ziel; über **Kopieren** im Rechtsklickmenü wird der Pfad kopiert. Dateiinhalte werden nicht importiert. |
| Gruppen, Tags und Anheften | Zusammengehörige Einträge bündeln, Titel/Text/Tags bearbeiten und häufig verwendete Inhalte anheften. |
| Ablauf und Papierkorb | Nie, einen Tag, sieben Tage oder Löschen nach dem Lesen wählen; abgelaufene und gelöschte Einträge gelangen zur Wiederherstellung in den Papierkorb. Einträge im Papierkorb werden standardmäßig nach 30 Tagen endgültig entfernt. |
| Desktop-Haftnotizen | Eine Text- oder Pfadkarte aus dem Panel ziehen, um sie für die aktuelle Sitzung als eigene Desktop-Notiz zu behalten. Beim Beenden der App werden diese Notizen geschlossen; die gespeicherten Einträge bleiben in Ihrer Sammlung. Bildkarten bleiben Bildeinträge. |
| QR-Codes | Per Rechtsklick auf eine Textkarte einen QR-Code für bis zu 1.800 Zeichen anzeigen. |
| Textwerkzeuge | Im Menü „Umwandeln“ der Werkbank Groß-/Kleinschreibung ändern, Leerraum bereinigen, JSON formatieren sowie URL- und Base64-Kodierung bzw. -Dekodierung nutzen. |
| Gruppen importieren/exportieren | `.ksdgrp`-Pakete teilen oder Befehlsvorlagen für Git, Docker, Shells und weitere Werkzeuge laden. Vorlagenpakete verwenden Format 3 und benötigen mindestens 1.3.0; Formate 1 und 2 bleiben importierbar. |
| Zwischenablageverlauf | Kopierten **Text** auf Wunsch automatisch sammeln; leere und zu große Texte werden ignoriert, Duplikate nicht erneut hinzugefügt. Standardmäßig deaktiviert. |
| Direkt einfügen | Einen kopierten Texteintrag auf Wunsch in die vorherige Anwendung einfügen, wenn sich deren Zielfenster sicher bestätigen lässt. Standardmäßig deaktiviert. |

### 2. Panelmodi Standard und Räumlich

*Die Panel-Screenshots und das Bewegungs-GIF in diesem Abschnitt zeigen 1.2.3.*

Wählen Sie **Einstellungen > Darstellung > Panelmodus**. Beide Modi behalten dieselben Bedienelemente, Schriften, Farben und Kartenanordnungen bei.

<table>
  <tr><th>Standard</th><th>Räumlich</th></tr>
  <tr>
    <td><img src="docs/images/classic-panel.png" width="290" alt="Standardpanel mit flacher Ansicht und weichen Schatten"></td>
    <td><img src="docs/images/floating-panel.png" width="290" alt="Räumliches Panel mit Perspektive, gestaffelten Karten und einer dünnen Seitenkante"></td>
  </tr>
  <tr><td>Eine ruhige, flache Darstellung mit weichen Schatten.</td><td>Perspektive, unabhängige Kartenreaktionen und sichtbare Tiefe zwischen den Ebenen.</td></tr>
</table>

<p align="center"><img src="docs/images/floating-panel-motion.gif" width="400" alt="Räumlicher Modus: Reaktion naher Karten, anschließend Ansichtsprüfung durch Ziehen bei gedrückter Maustaste"></p>

*Der weiße Zeiger zeigt die Reaktion benachbarter Karten; der orangefarbene Zeiger kennzeichnet das Ziehen bei gedrückter Maustaste zur Ansichtsprüfung. Die Animation veranschaulicht die Bedienung und ist kein Bildraten-Benchmark.*

- **Feste Perspektive:** eine stabile geneigte Ansicht beibehalten, während nahe Karten auf den Zeiger reagieren.
- **Zum Betrachten ziehen:** die **rechte Maustaste** auf einer Kartenfläche oder dem Panelhintergrund gedrückt halten und ziehen. Beim Loslassen bleibt der Winkel erhalten. Ein Rechtsklick ohne Ziehen öffnet weiterhin das Kartenmenü. In den Einstellungen lassen sich auch die mittlere Maustaste oder eine eigene Kombination aus Maustaste und Zusatztasten wählen.
- **Kompakte Leiste:** Das eingeklappte Panel enthält sechs Aktionen: Minimieren, Ein-/Ausklappen, Sperren, Screenshot, Werkbank und Einstellungen. Beim Ausklappen erhält das Panel seine vorherige Größe zurück.
- **Am Bildschirmrand automatisch einklappen:** standardmäßig aktiviert; das Panel an einen der vier Bildschirmränder ziehen und den Zeiger wegbewegen. Ein kleiner Griff bleibt sichtbar; zeigen Sie darauf oder klicken Sie ihn an, um das Panel wiederherzustellen. Durch Sperren bleibt es sichtbar.

Ziehen Sie in beiden Modi an den Kanten oder Ecken des ausgeklappten Panels, um seine Größe zu ändern; sie wird beim Neustart wiederhergestellt. Die kompakte Leiste mit sechs Schaltflächen hat eine feste Größe.

Helles, dunkles und systemabhängiges Design stehen zur Verfügung, ergänzt um eigene Hintergründe, Deckkraft und Unschärfe. Die Systemeinstellungen für hohen Kontrast und reduzierte Animationen aktivieren statische Ersatzdarstellungen. [Dunkle Vorschau](docs/images/floating-panel-dark.png) · [Vorschau mit fester Perspektive](docs/images/floating-panel-fixed.png).

Die Einstellungen bieten außerdem Textfarbpaletten für das Panel, eine eigene Textfarbe und automatischen Kontrast sowie unabhängige Optionen für Stil, Leuchten, Schatten und Deckkraft der Auswahlblase.

<details>
<summary>Voreinstellungen für die Darstellung</summary>

<p align="center"><img src="docs/images/default-appearance.png" width="720" alt="Darstellungseinstellungen mit Standardmodus und den Voreinstellungen einer Neuinstallation"></p>

</details>
#### Vollständige Erscheinungsbilder

Öffnen Sie **Einstellungen > Darstellung > Vollständige Designs**, wählen Sie ein Design und dann **Vorschau und anwenden**. Wählen Sie Hell, Dunkel oder System und legen Sie fest, ob Auswahlblasen und Ergebnisfenster dem Design folgen. **Abbrechen** stellt die vorherige Darstellung wieder her. Im Systemmodus folgt die Tag-/Nachtvariante dem hellen oder dunklen Windows-Design.

| Design | Gestaltung |
| --- | --- |
| **Sunlit Gallery** | Ruhige, cremefarbene Fenstergalerie mit Tageslicht und einer mondbeschienenen Nachtvariante; ohne Personen. |
| **Twilight Study** | Arbeitszimmer in tiefem Oliv und Walnuss, mit Tages- und Nachtvariante; ohne Personen. |
| **Wisteria Atelier** | Anime-Pianistin in einem lichtdurchfluteten Atelier, mit abgestimmter heller und dunkler Variante. |
| **Wisteria Reverie** | Anime-Pianistin in einem lichtdurchfluteten Wintergarten in Lavendeltönen, mit abgestimmten Tag-/Nachtvarianten im Hoch- und Querformat. |

Alle Designs sind optional. Neuinstallationen verwenden weiterhin das Systemdesign ohne Hintergrundbild.

[Design-Anleitung und Hintergrundvorschau](docs/THEMES.md).

<p align="center"><img src="docs/images/1.3.0/theme-preview-light.png" width="680" alt="1.3.0-Vorschau von Wisteria Atelier mit Erscheinungsbild, Beispiel-Steuerelementen und festen Schaltflächen zum Anwenden und Abbrechen"></p>

Mit **Design importieren** laden Sie ein Design-ZIP. **Aktuelles Erscheinungsbild exportieren** teilt beide hellen/dunklen Varianten einschließlich Bildern und Darstellungseinstellungen. Persönliche Hintergrundbilder müssen ausdrücklich eingeschlossen werden. Designs enthalten keine Einträge, API-Schlüssel, Tastenkürzel oder Komponenteninstallationen. Die Dateien liegen in `theme/` neben der Anwendung; Updates erhalten importierte Designs unter `theme/custom/`.

Die **Deckkraft der Einstellungsoberflächen** verändert Karten, Navigation und Eingabefelder, ohne Text zu verblassen. **Milchglas-Hintergrund der Einstellungen** zeichnet nur das Designbild weich; **Milchglas-Hintergrund der Blasen** ist eine separate Option unter **Blasen & Benachrichtigungen**. Beide Effekte sind zunächst ausgeschaltet. Ohne Einstellungsbild, bei hohem Kontrast oder fehlender Blasenkomposition werden entsprechend lesbare, undurchsichtige Oberflächen verwendet.

<p align="center"><img src="docs/images/1.3.0/settings-frosted-dark.png" width="760" alt="Darstellungseinstellungen von 1.3.0 mit optionalem dunklem Wisteria-Design, 60 Prozent Oberflächendeckkraft und 8px Hintergrundunschärfe"></p>

*1.3.0 · Optionales dunkles Wisteria-Design, 60 % Deckkraft der Einstellungsoberflächen und 8 px Hintergrundunschärfe. Dies ist weder die Neuinstallationsdarstellung noch eine Vorführung nativer Desktop-Unschärfe.*

### 3. Screenshots und lokale Texterkennung

*Die Bilder zum Screenshot-Ablauf in diesem Abschnitt zeigen 1.2.3.*

<p align="center"><img src="docs/images/screenshot-entry.png" width="720" alt="Gespeicherter Screenshot-Eintrag mit Originalbild und bearbeitbarem erkanntem Text"></p>

Drücken Sie **Alt+W** oder eine Screenshot-Schaltfläche und ziehen Sie einen Auswahlbereich auf. Gespeicherte Screenshots werden zu Bildeinträgen in der ausgewählten Gruppe. Öffnen Sie einen Eintrag, um ihn anzusehen, zu kopieren, als PNG zu speichern oder **Text erkennen** zu wählen. Der erkannte Text lässt sich bearbeiten und durchsuchen; das Originalbild bleibt erhalten.

Nach der Auswahl bietet eine Werkzeugleiste drei getrennte Aktionen:

| Aktion | Ergebnis |
| --- | --- |
| Screenshot speichern | Einen Bildeintrag speichern. Enter wählt diese Aktion. |
| Text extrahieren | Lokale OCR ausführen und vorübergehend Text anzeigen, ohne einen Eintrag zu speichern oder automatisch zu kopieren. |
| Extrahieren und übersetzen | Lokale OCR ausführen und den erkannten Text anschließend an den gewählten Übersetzungsanbieter senden. Der Screenshot selbst wird nicht hochgeladen. |

<p align="center"><img src="docs/images/screenshot-text.png" width="480" alt="Vorübergehendes OCR-Ergebnis mit eigenen Schaltflächen zum Kopieren des Textes in Version 1.2.3"></p>

Erneut ziehen, um den Bereich neu auszuwählen; **Esc** oder ein Rechtsklick bricht ab. Vorübergehende Ergebnisse unterstützen bewusstes Kopieren und erneute Übersetzungsversuche; der Ausgangstext bleibt bei Übersetzungsfehlern erhalten. OCR dient der Texterkennung und übersetzt nicht automatisch. [Screenshot-Abläufe](docs/SCREENSHOTS.md) · [OCR-Unterstützung und Grenzen](docs/LOCAL_OCR.md).

Temporäre OCR- und Übersetzungsergebnisse bieten **Original speichern**, **Übersetzung speichern** und **Beides speichern** sowie eine Gruppenauswahl mit der aktuellen Gruppe als Vorgabe. Beides erstellt einen Texteintrag mit beschrifteten Abschnitten. Gespeichert wird nur auf ausdrücklichen Wunsch; Zwischenablage und Screenshot bleiben unverändert. Bei Übersetzungsfehlern lässt sich das Original weiterhin speichern; bei Speicherfehlern bleibt das Ergebnis zum erneuten Versuch verfügbar. Identischer Text verwendet den bestehenden Eintrag und ordnet ihn der gewählten Gruppe zu.

### 4. Aktionen für markierten Text und Übersetzung

<table>
  <tr><th>1.3.0 · Standardstil</th><th>1.3.0 · Dunkler Wisteria-Stil</th></tr>
  <tr>
    <td><img src="docs/images/1.3.0/selection-default.png" width="345" alt="1.3.0-Standardblase mit optionalen Aktionen zum Kopieren und Übersetzen"></td>
    <td><img src="docs/images/1.3.0/selection-wisteria-dark.png" width="345" alt="1.3.0-Blase im dunklen Wisteria-Stil mit optionalen Aktionen zum Kopieren und Übersetzen"></td>
  </tr>
</table>

*Beide 1.3.0-Vorschauen aktivieren den optionalen Modus Kopieren und übersetzen. Neuinstallationen verwenden weiterhin Nur kopieren.*

Aktivieren Sie die Auswahlblase unter **Einstellungen > Erfassen & Übersetzen** und markieren Sie anschließend Text in einer anderen Anwendung. **Kopieren** übernimmt die Auswahl in die aktuelle Gruppe des Docks; **Übersetzen** zeigt eine Übersetzung an, ohne einen Eintrag anzulegen.

Wählen Sie **Kopieren und übersetzen**, damit beide Schaltflächen sichtbar bleiben. Nach dem Kopieren können Sie dieselbe Auswahl noch übersetzen, solange die Blase geöffnet ist. Bei Übersetzungsfehlern bleibt der Ausgangstext erhalten.

Wählen Sie die Zielsprache in den Einstellungen unabhängig von der Oberflächensprache. **Oberflächensprache verwenden** ist der Standard bei Neuinstallationen; Upgrades bewahren Ihre Auswahl. Auswahlübersetzung, Screenshot-Übersetzung und Übersetzungstest verwenden dieselbe Einstellung. Im Ergebnisfenster lässt sich der erhaltene Ausgangstext vorübergehend in eine andere Sprache übersetzen; das ändert die globale Einstellung nicht. Verfügbare Ziele hängen vom Anbieter und den installierten Modellen ab. Nicht unterstützte Sprachpaare führen zu einer Fehlermeldung, ohne Anbieterwechsel oder Upload von Offline-Text.

<table>
  <tr><th>1.3.0 · Standardstil</th><th>1.3.0 · Dunkler Wisteria-Stil</th></tr>
  <tr>
    <td><img src="docs/images/1.3.0/translation-default.png" width="420" alt="Kompaktes Übersetzungsergebnis in 1.3.0 im Standardstil mit Kopieren, Zielsprache und Speicheraktionen"></td>
    <td><img src="docs/images/1.3.0/translation-wisteria-dark.png" width="420" alt="Kompaktes Übersetzungsergebnis in 1.3.0 im dunklen Wisteria-Stil mit denselben Aktionen"></td>
  </tr>
</table>

*1.3.0 · Vorab eingetragene Beispieltexte in echten Ergebnis-Steuerelementen; OCR und Übersetzungsdienste wurden nicht ausgeführt. Zielsprache und Speicheraktionen sind in beiden Ansichten sichtbar und lassen sich in den Einstellungen unabhängig ausblenden. Nativer Milchglaseffekt der Blasen wird in diesen Offscreen-Vorschauen nicht erfasst.*

| Anbieter | Einrichtung und Verfügbarkeit |
| --- | --- |
| Bing (China), Google, Lingva | Kein Schlüssel nötig. Die Verfügbarkeit ist nicht garantiert. Neue Installationen verwenden standardmäßig Google; bestehende Einstellungen bleiben erhalten. |
| Baidu, Alibaba Cloud, DeepL | Eigene Zugangsdaten erforderlich. Freikontingente und Gebühren hängen vom Anbieter und Ihrem Konto ab. Alibaba Cloud benötigt einen aktivierten Dienst für maschinelle Übersetzung sowie die Berechtigung `alimt:TranslateGeneral`. |
| Lokal offline | Die optionale lokale Engine installieren; sie nutzt einen Loopback-Dienst ohne Konto oder API-Gebühr. |

Bei Cloud-Übersetzungen wird nur der angeforderte Text an den ausgewählten Anbieter gesendet; es erfolgt kein automatischer Anbieterwechsel. OCR lädt keine Bilder hoch. Zugangsdaten der Anbieter werden **im Klartext** in der lokalen `settings.json` gespeichert; verdeckte Eingabefelder verschlüsseln sie nicht.

<details>
<summary>Voreinstellungen für Textauswahl und Übersetzung</summary>

<p align="center"><img src="docs/images/translation-settings.png" width="720" alt="Aufnahme- und Übersetzungseinstellungen von 1.2.3 mit Nur kopieren, Ergebnisoptionen, Google und unabhängiger Zielsprache"></p>

</details>

### 5. Optionale Komponenten direkt in der App installieren

<p align="center"><img src="docs/images/component-setup.png" width="720" alt="Einstellungen für optionale Komponenten mit Schaltflächen zum Installieren und Reparieren von OCR und Offline-Übersetzung"></p>

*Die Komponenteneinstellungen zeigen den Zustand einer Neuinstallation von 1.2.3: OCR und Offline-Übersetzung sind nicht installiert. Die Installation erfolgt über diese Schaltflächen in der App; ein Terminalbefehl ist nicht nötig.*

<details>
<summary>Installationsordner der Komponenten</summary>

<p align="center"><img src="docs/images/component-folders.png" width="720" alt="Komponentenordner in der englischen Oberfläche von 1.2.3; die Pfade sind verdeckt."></p>

*Komponentenordner in der englischen Oberfläche von 1.2.3; die Pfade sind verdeckt.*

</details>

1. Öffnen Sie **Einstellungen > Lokale Komponenten**.
2. Wählen Sie vor der ersten OCR-Nutzung unter **Texterkennung (Chinesisch / Englisch)** die Aktion **Installieren / reparieren**. Das Standardpaket enthält keine vorinstallierten OCR- oder lokalen Übersetzungsengines und Modelle. Erst die ausdrücklich gestartete Einrichtung lädt und prüft die benötigten Dateien und kontrolliert die native Laufzeit.
3. Wählen Sie für lokale Übersetzungen unter **Offline-Übersetzung (Chinesisch / Englisch)** die Aktion **Installieren / reparieren**. Lassen Sie die Einstellungen während des ersten Downloads geöffnet; planen Sie etwa **1 GB freien Speicherplatz** und **2 GB verfügbaren Arbeitsspeicher** ein.
4. Folgen Sie bei Bedarf dem Installationsprogramm von Microsoft. Meldet die Einrichtung **Neustart erforderlich**, starten Sie Windows neu und versuchen Sie es erneut. Ein Abbruch der App-Einrichtung macht eine bereits gestartete Windows-Laufzeitinstallation nicht rückgängig.
5. Wählen Sie **Lokal offline (Argos / LibreTranslate)** als Übersetzungsengine. Behalten Sie `http://127.0.0.1:5000` bei und verwenden Sie **Übersetzung testen**. Der Dienst startet bei Bedarf; **Dienst starten** und **Dienst stoppen** stehen ebenfalls zur Verfügung.

Die erste Einrichtung benötigt Internetzugriff auf die jeweiligen Downloadserver von NuGet, GitHub, Python, Paket- und Modellanbietern sowie Microsoft. Fehlgeschlagene Downloads können wiederholt werden; gültige zwischengespeicherte Dateien werden wiederverwendet. Nach der Installation arbeiten OCR und lokale Übersetzung offline. Der Übersetzungsdienst läuft nach dem Schließen der App weiter, bis er gestoppt wird.

**Unterstützte Sprachen:** OCR erkennt gedrucktes Englisch und vereinfachtes Chinesisch. Die optional heruntergeladenen Übersetzungsmodelle unterstützen **Englisch ↔ vereinfachtes Chinesisch**. Die **20 Oberflächensprachen** der App bedeuten nicht, dass OCR oder Offline-Übersetzung 20 Sprachen unterstützen. Fehlende Modelle lösen niemals einen Wechsel zur Cloud-Übersetzung aus. [Offline-Einrichtung und Fehlerbehebung](docs/OFFLINE_TRANSLATION.md).

### 6. Nach Updates suchen

<p align="center"><img src="docs/images/update-check.png" width="720" alt="Updatedialog mit einer simulierten neueren Veröffentlichung und verdecktem Installationspfad"></p>

*Die angezeigte Veröffentlichung ist simuliert, der Installationspfad ist verdeckt. Dieses Bild kündigt keine verfügbare Veröffentlichung an.*

Verwenden Sie **Nach Updates suchen** im Infobereich-Menü oder unter **Einstellungen > Über**. Eine Prüfung lädt oder installiert nichts automatisch. Beim Download werden SHA-256 und Paketinhalt geprüft; **Neu starten und aktualisieren** benötigt eine separate Bestätigung. Der Updater ersetzt verwaltete Anwendungsdateien am vorhandenen Pfad, bewahrt Benutzerdaten und installierte Komponenten und hält Wiederherstellungsinformationen bereit, falls der Start fehlschlägt.

Das geordnete Paket enthält drei Dateien im Hauptverzeichnis: `KeeStashDock.exe`, `StartHere.md` und `update-manifest.json`. Anleitungen liegen in `Docs`, Lizenzhinweise in `Licenses`; Sprachen und optionale Komponenten behalten eigene Ordner. Lassen Sie das vollständige Paket zusammen. [Updates und Wiederherstellung](docs/UPDATING.md).

<a id="shortcuts"></a>
## Tastenkürzel

Kann ein Panel- oder Screenshot-Kürzel beim Start nicht registriert werden, nennt eine Meldung das Kürzel und bietet den Weg zu den Einstellungen. Der Status bleibt dort sichtbar; Infobereich und Schaltflächen funktionieren weiter. Eine fehlgeschlagene Änderung behält die zuvor funktionierende Belegung bei.

| Aktion | Standard-Tastenkürzel oder Geste |
| --- | --- |
| Schnellpanel anzeigen/ausblenden | **Alt+Q**; konfigurierbar |
| Im Schnellpanel suchen | **Ctrl+F** bei aktivem Panel; **Esc** kehrt zur Erfassung zurück |
| Ergebnisse im Arbeitsbereich auswählen | **Ctrl+A** bei fokussierter Liste; wählt nur geladene Zeilen aus |
| Eingegebenen Text speichern | **Enter** |
| Zeilenumbruch einfügen | **Alt+Enter** oder **Shift+Enter** |
| Screenshot | **Alt+W**; unabhängig konfigurierbar |
| Ausgewählten Screenshot-Bereich speichern | **Enter** in der Aufnahmeleiste |
| Screenshot abbrechen | **Esc** oder Rechtsklick |
| Räumliches Panel betrachten | **Rechte Maustaste** gedrückt halten und ziehen; konfigurierbar |
| Über die Auswahlblase kopieren/übersetzen | **Ctrl+Alt+Shift+C** / **Ctrl+Alt+Shift+T**, nur solange die Blase sichtbar ist und die Tastenkürzel verfügbar sind |

<a id="data-and-upgrades"></a>
## Daten, Sicherung und Upgrades

**Datenbankkompatibilität in 1.3.0:** Die Anwendung aktualisiert die Bibliothek auf **Schema 5**. Vor Änderungen an einer älteren Datenbank wird eine konsistente Sicherung einschließlich bestätigter WAL-Daten erstellt und geprüft. Schlägt die Sicherung fehl, wird die Migration gestoppt. **1.2.3 und ältere Versionen können Schema 5 nicht öffnen.** Das Wiederherstellen älterer Programmdateien setzt die Datenbank nicht zurück. Bewahren Sie den aktiven Datenordner und die Sicherung vor dem Upgrade auf. Stellen Sie für eine Rückkehr eine separate kompatible Sicherungskopie wieder her, statt neuere Daten zu überschreiben.

Designs liegen neben der ausführbaren Datei unter `theme/`: `builtin/` enthält von der App verwaltete Ressourcen, `custom/` importierte Benutzerdesigns. Updates erhalten `theme/custom/`. **Datenbanksicherungen enthalten keine Designs.** Exportieren Sie das vollständige helle/dunkle Erscheinungsbild samt Bildern separat über die Einstellungen, um es zu sichern oder zu übertragen.

Die Einstellungen zeigen letzte erfolgreiche Sicherung, letzte Prüfung, nächste geplante Prüfung, Status, Speicherort und Anzahl der Snapshots. **Jetzt sichern**, Ordner öffnen und Wiederherstellen aus der Liste über den abgesicherten Ablauf sind verfügbar. Intervalle: **15 / 30 / 60 Minuten**, 6 / 12 / 24 / 48 Stunden oder 7 Tage; Standard bleibt **24 Stunden**. Vorhandene eigene Intervalle und Aufbewahrungszahlen bleiben erhalten. Sieben Snapshots im Abstand von 15 Minuten decken vom ältesten bis zum neuesten nur **90 Minuten** ab; wählen Sie die Anzahl anhand des Bedarfs und der Speicherplatzschätzung. Jede geplante Sicherung erstellt auch bei unveränderter Datenbank einen vollständigen Snapshot; kürzere Intervalle ersetzen den aufbewahrten Verlauf schneller. Gesichert wird die Eintragsdatenbank einschließlich gespeicherter Bilder, **nicht sämtliche Einstellungen, Komponenten oder Hintergründe**. Automatische Bereinigung entfernt keine manuellen Exporte oder Wiederherstellungskopien.

<details>
<summary>Sicherungseinstellungen · standardmäßig alle 24 Stunden</summary>

<p align="center"><img src="docs/images/backup-settings.png" width="720" alt="Dateneinstellungen mit automatischen Sicherungen alle 24 Stunden und verdeckten Pfaden"></p>

<p align="center"><img src="docs/images/backup-status.png" width="720" alt="Sicherungsstatus, Momentaufnahmen und manuelle Aktionen mit isolierten Beispieldaten. Die Pfade sind verdeckt; das Standardintervall bleibt bei 24 Stunden."></p>

*Sicherungsstatus, Momentaufnahmen und manuelle Aktionen mit isolierten Beispieldaten. Die Pfade sind verdeckt; das Standardintervall bleibt bei 24 Stunden.*

</details>

<details>
<summary>Info · Anwendungsversion und Datenordner</summary>

<p align="center"><img src="docs/images/about-settings.png" width="720" alt="Infoseite von 1.2.3 mit Anwendungsinformationen und verdecktem Datenpfad"></p>

</details>

Einträge, Bilder, erkannter Text und Sicherungen werden lokal gespeichert. Portable Daten liegen normalerweise in `Data` neben der ausführbaren Datei. Ist dieser Ordner nicht beschreibbar, verwendet die App `%AppData%\KeeStashDock` und zeigt den Ersatzpfad in den Einstellungen an. Über die App installierte Komponenten nutzen normalerweise `%LOCALAPPDATA%\KeeStashDock`, getrennt von Ihrer Sammlung.

Unter **Einstellungen > Sicherung & Daten** können Sie eine Sicherung exportieren/importieren und geplante Sicherungen einrichten. Das erste Upgrade von **1.2.1 erfolgt manuell**:

1. Exportieren Sie eine Sicherung und beenden Sie die App anschließend über ihr Menü im Infobereich.
2. Entpacken Sie das neue ZIP in einen separaten Ordner. Kopieren Sie bei geschlossener App den **gesamten aktiven Datenordner** in den `Data`-Ordner der neuen Installation. Zeigen die Einstellungen einen Ersatzpfad unter `%AppData%` an, verwenden Sie diesen Ordner als Quelle.
3. Behalten Sie die ursprüngliche Installation und die Sicherung. Starten Sie die neue Version und prüfen Sie Ihre Einträge, bevor Sie alte Kopien entfernen. Bewahren Sie für eine Rückkehr zur alten Version eine Sicherung vor dem Upgrade auf; ältere Datenbankschemata sind nicht austauschbar.
4. War **Mit Windows starten** aktiviert und wurde die Anwendung in einen neuen Ordner verschoben, schalten Sie die Option in den Einstellungen der neuen App aus und wieder ein. Dadurch wird der neue Programmpfad registriert; andernfalls kann Windows bei der nächsten Anmeldung die alte Kopie starten.

Ab **1.2.2** können Sie **Nach Updates suchen** verwenden oder die manuellen Schritte oben befolgen. **1.3.0-Kandidaten (r1–r5)** müssen manuell ersetzt werden, da sie dieselbe numerische Version wie die endgültige Fassung haben. Bewahren Sie den gesamten aktiven Datenordner und die Sicherungen auf; beenden Sie den alten Prozess, bevor Sie die neue Kopie starten.

Fügen Sie einem Issue keine persönlichen `Data`-Dateien, Zugangsdaten, privaten Sicherungen oder Protokolle bei. Siehe [Support und bereinigte Fehlerberichte](SUPPORT.md), [Wiederherstellung nach Upgrades](docs/UPDATING.md) und die [Sicherheitsrichtlinie](SECURITY.md).

<a id="development"></a>
## Entwicklung und Dokumentation

Erstellen Sie das Projekt unter Windows mit dem in [`global.json`](global.json) ausgewählten SDK (10.0.401 oder einem kompatiblen 10.0.4xx-Patch):

```powershell
dotnet restore KeeStashDock.sln
dotnet build KeeStashDock.sln --configuration Release --no-restore --disable-build-servers
dotnet test --solution KeeStashDock.sln --configuration Release --no-build
```

Englisch ist die Referenzsprache der Repository-Dokumentation. Die übersetzten READMEs verwenden dieselbe Funktionsgliederung; die Oberflächenübersetzungen liegen in `Locales/*.json`. Übersetzungskorrekturen sind gemäß dem [Leitfaden für Beiträge](CONTRIBUTING.md) willkommen.

| Dokumentation | Inhalt |
| --- | --- |
| [Änderungsprotokoll](CHANGELOG.md) | Versionshistorie |
| [Architektur](docs/ARCHITECTURE.md) | Projekte, Zuständigkeiten und Speicherung |
| [Lokalisierung](docs/LOCALIZATION.md) | Konventionen für die Übersetzung von Oberfläche und README |
| [Veröffentlichungsprozess](docs/RELEASING.md) | Build, Paketierung und Abnahmeprüfungen |
| [Support](SUPPORT.md) · [Verhaltenskodex](CODE_OF_CONDUCT.md) | Fragen, Berichte und Beteiligung an der Community |
| [Hinweise zu Drittanbietern](THIRD_PARTY_NOTICES.md) | Lizenzen von Abhängigkeiten, Laufzeiten und Modellen |

<a id="license"></a>
## Lizenz

Copyright © 2026 KeeganKei. Lizenziert unter [Apache-2.0](LICENSE); Angaben zur Urheberschaft stehen in [NOTICE](NOTICE). Abhängigkeiten, optionale Laufzeiten und Modelle behalten ihre eigenen Lizenzen gemäß [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Die Projektlizenz gewährt keine Markenrechte.

Das Anwendungssymbol und die mitgelieferten Designbilder sind KI-generiert. Herkunfts- und Lizenzangaben: [Symbol](docs/assets/README.md), [Sunlit Gallery](theme/builtin/sunlit-gallery/SOURCE.md), [Twilight Study](theme/builtin/twilight-study/SOURCE.md), [Wisteria Atelier](theme/builtin/wisteria-atelier/SOURCE.md), [Wisteria Reverie](theme/builtin/wisteria-reverie/SOURCE.md).
