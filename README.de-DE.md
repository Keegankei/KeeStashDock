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
  <img alt="Veröffentlichte Version 1.2.1" src="docs/assets/badges/version.svg">
  <img alt=".NET 10" src="docs/assets/badges/dotnet.svg">
  <img alt="Windows 10 und 11" src="docs/assets/badges/windows.svg">
  <img alt="Apache-2.0-Lizenz" src="docs/assets/badges/license.svg">
</p>

> **Veröffentlichungsstand:** Die veröffentlichte Version ist **1.2.1**. Dieser Branch bereitet **1.2.2** vor. Mit **1.2.2-Kandidat** gekennzeichnete Abschnitte beschreiben noch nicht veröffentlichte Funktionen; die Downloadlinks führen weiterhin zu 1.2.1. Siehe das [Abnahmeprotokoll des Kandidaten](docs/releases/1.2.2.md).

[Einstieg](#get-started) · [Funktionen](#features) · [Tastenkürzel](#shortcuts) · [Daten und Upgrades](#data-and-upgrades) · [Entwicklung](#development) · [Lizenz](#license)

<a id="get-started"></a>
## Einstieg

**[Neueste Veröffentlichung](https://github.com/Keegankei/KeeStashDock/releases/latest)** · **[Windows-x64-ZIP — 1.2.1](https://github.com/Keegankei/KeeStashDock/releases/download/v1.2.1/KeeStashDock-1.2.1-win-x64.zip)** · [SHA-256-Prüfsumme](https://github.com/Keegankei/KeeStashDock/releases/download/v1.2.1/KeeStashDock-1.2.1-win-x64.zip.sha256)

1. Laden Sie unter **Assets** das ZIP-Paket der Anwendung herunter, nicht das für die Entwicklung gedachte **Source code**-Archiv von GitHub.
2. Entpacken Sie das vollständige Paket in einen beschreibbaren Ordner und starten Sie `KeeStashDock.exe`. Lassen Sie alle mitgelieferten Ordner beisammen.
3. Drücken Sie **Alt+Q**, um das Schnellpanel anzuzeigen. Geben Sie einen Textbaustein ein und speichern Sie ihn mit **Enter**; ein Klick auf die Karte kopiert ihn.
4. Öffnen Sie **Einstellungen** über das Panel oder den Infobereich der Taskleiste, um Sprache, Design und optionale Komponenten auszuwählen.

Das Windows-x64-Paket enthält .NET; eine separate .NET-Installation ist nicht nötig. Windows 11 ist die primäre Zielplattform, Windows 10 22H2 wird nach Möglichkeit unterstützt. OCR und Offline-Übersetzung benötigen möglicherweise die Visual-C++-Laufzeit von Microsoft, die über die Komponenteninstallation in der App installiert werden kann. Den getesteten Umfang beschreibt das [Abnahmeprotokoll für 1.2.1](docs/releases/1.2.1.md).

<details>
<summary>Das heruntergeladene ZIP prüfen</summary>

Führen Sie diesen Befehl im Downloadordner aus und vergleichen Sie das Ergebnis mit der zugehörigen `.sha256`-Datei:

```powershell
Get-FileHash .\KeeStashDock-1.2.1-win-x64.zip -Algorithm SHA256
```

Eine Prüfsumme prüft die Dateiintegrität; sie ist keine Herausgebersignatur. Das aktuelle Paket ist nicht signiert.

</details>

<a id="features"></a>
## Funktionen

Die folgenden Vorschaubilder zeigen echte Bedienelemente der Anwendung mit synthetischen Beispieldaten. Das GIF zur Ansichtsprüfung wird aus diesen Bedienelementen mit einem simulierten Zeiger erzeugt. Die Updatevorschau verwendet eine simulierte Veröffentlichung; persönliche Desktop- oder Zwischenablageinhalte werden nicht gezeigt. [Bildquellen und Reproduktion](docs/images/README.md).

### 1. Erfassen, finden und organisieren

<p align="center"><img src="docs/images/main-window.png" width="900" alt="Werkbank mit Suche, Gruppen, angehefteten Textbausteinen und Beispielbildern"></p>

Verwenden Sie das Schnellpanel für kurze Eingaben und die **Werkbank** zur Verwaltung Ihrer Sammlung. Öffnen Sie die Werkbank über die entsprechende Schaltfläche im Panel oder den Infobereich der Taskleiste.

| Werkzeug | Möglichkeiten |
| --- | --- |
| Schnelleingabe und Suche | Text mit Enter speichern; mit Alt+Enter oder Shift+Enter Zeilenumbrüche einfügen; Einträge während der Eingabe durchsuchen. |
| In das Dock ziehen | Text ablegen, um ihn zu speichern, oder Dateien und Ordner ablegen, um deren Pfade zu speichern. Ein Klick auf eine Pfadkarte öffnet das Ziel; über **Kopieren** im Rechtsklickmenü wird der Pfad kopiert. Dateiinhalte werden nicht importiert. |
| Gruppen, Tags und Anheften | Zusammengehörige Einträge bündeln, Titel/Text/Tags bearbeiten und häufig verwendete Inhalte anheften. |
| Ablauf und Papierkorb | Nie, einen Tag, sieben Tage oder Löschen nach dem Lesen wählen; abgelaufene und gelöschte Einträge gelangen zur Wiederherstellung in den Papierkorb. Einträge im Papierkorb werden standardmäßig nach 30 Tagen endgültig entfernt. |
| Desktop-Haftnotizen | Eine Text- oder Pfadkarte aus dem Panel ziehen, um sie für die aktuelle Sitzung als eigene Desktop-Notiz zu behalten. Beim Beenden der App werden diese Notizen geschlossen; die gespeicherten Einträge bleiben in Ihrer Sammlung. Bildkarten bleiben Bildeinträge. |
| QR-Codes | Per Rechtsklick auf eine Textkarte einen QR-Code für bis zu 1.800 Zeichen anzeigen. |
| Textwerkzeuge | Im Menü „Umwandeln“ der Werkbank Groß-/Kleinschreibung ändern, Leerraum bereinigen, JSON formatieren sowie URL- und Base64-Kodierung bzw. -Dekodierung nutzen. |
| Gruppen importieren/exportieren | `.ksdgrp`-Pakete teilen oder Befehlsvorlagen für Git, Docker, Shells und weitere Werkzeuge laden. Pakete mit Bildern erfordern eine kompatible App-Version. |
| Zwischenablageverlauf | Kopierten **Text** auf Wunsch automatisch sammeln; leere und zu große Texte werden ignoriert, Duplikate nicht erneut hinzugefügt. Standardmäßig deaktiviert. |
| Direkt einfügen | Einen kopierten Texteintrag auf Wunsch in die vorherige Anwendung einfügen, wenn sich deren Zielfenster sicher bestätigen lässt. Standardmäßig deaktiviert. |

### 2. Panelmodi Standard und Räumlich · 1.2.2-Kandidat

Wählen Sie **Einstellungen > Panelmodus**. Beide Modi behalten dieselben Bedienelemente, Schriften, Farben und Kartenanordnungen bei.

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
- **Am Bildschirmrand automatisch einklappen:** diese Option aktivieren, das Panel an einen der vier Bildschirmränder ziehen und den Zeiger wegbewegen. Ein kleiner Griff bleibt sichtbar; zeigen Sie darauf oder klicken Sie ihn an, um das Panel wiederherzustellen. Durch Sperren bleibt es sichtbar.

Helles, dunkles und systemabhängiges Design stehen zur Verfügung, ergänzt um eigene Hintergründe, Deckkraft und Unschärfe. Die Systemeinstellungen für hohen Kontrast und reduzierte Animationen aktivieren statische Ersatzdarstellungen. [Dunkle Vorschau](docs/images/floating-panel-dark.png) · [Vorschau mit fester Perspektive](docs/images/floating-panel-fixed.png) · [Gemessene Leistung und Grenzen](docs/performance/1.2.2-floating.md).

Die Einstellungen bieten außerdem Textfarbpaletten für das Panel, eine eigene Textfarbe und automatischen Kontrast sowie unabhängige Optionen für Stil, Leuchten, Schatten und Deckkraft der Auswahlblase.

### 3. Screenshots und lokale Texterkennung

<p align="center"><img src="docs/images/screenshot-entry.png" width="720" alt="Gespeicherter Screenshot-Eintrag mit Originalbild und bearbeitbarem erkanntem Text"></p>

Drücken Sie **Ctrl+Alt+S** oder eine Screenshot-Schaltfläche und ziehen Sie einen Auswahlbereich auf. Gespeicherte Screenshots werden zu Bildeinträgen in der ausgewählten Gruppe. Öffnen Sie einen Eintrag, um ihn anzusehen, zu kopieren, als PNG zu speichern oder **Text erkennen** zu wählen. Der erkannte Text lässt sich bearbeiten und durchsuchen; das Originalbild bleibt erhalten.

**1.2.2-Kandidat:** Nach der Auswahl bietet eine Werkzeugleiste drei getrennte Aktionen:

| Aktion | Ergebnis |
| --- | --- |
| Screenshot speichern | Einen Bildeintrag speichern. Enter wählt diese Aktion. |
| Text extrahieren | Lokale OCR ausführen und vorübergehend Text anzeigen, ohne einen Eintrag zu speichern oder automatisch zu kopieren. |
| Extrahieren und übersetzen | Lokale OCR ausführen und den erkannten Text anschließend an den gewählten Übersetzungsanbieter senden. Der Screenshot selbst wird nicht hochgeladen. |

<p align="center"><img src="docs/images/screenshot-text.png" width="480" alt="Vorübergehendes OCR-Ergebnis mit eigenen Schaltflächen zum Kopieren des Textes im Kandidaten 1.2.2"></p>

Erneut ziehen, um den Bereich neu auszuwählen; **Esc** oder ein Rechtsklick bricht ab. Vorübergehende Ergebnisse unterstützen bewusstes Kopieren und erneute Übersetzungsversuche; der Ausgangstext bleibt bei Übersetzungsfehlern erhalten. OCR dient der Texterkennung und übersetzt nicht automatisch. [Screenshot-Abläufe](docs/SCREENSHOTS.md) · [OCR-Unterstützung und Grenzen](docs/LOCAL_OCR.md).

### 4. Aktionen für markierten Text und Übersetzung

<p align="center"><img src="docs/images/selection-actions.png" width="480" alt="Auswahlblase von 1.2.2 mit getrennten Schaltflächen für Kopieren und Übersetzen"></p>

Aktivieren Sie die Auswahlblase unter **Einstellungen > Verhalten** und markieren Sie anschließend Text in einer anderen Anwendung. **Kopieren** übernimmt die Auswahl in die aktuelle Gruppe des Docks; **Übersetzen** zeigt eine Übersetzung an, ohne einen Eintrag anzulegen.

**1.2.2-Kandidat:** Wählen Sie **Kopieren und übersetzen**, damit beide Schaltflächen sichtbar bleiben. Nach dem Kopieren können Sie dieselbe Auswahl noch übersetzen, solange die Blase geöffnet ist. Bei Übersetzungsfehlern bleibt der Ausgangstext erhalten.

Die Zielsprache der Übersetzung folgt der Oberflächensprache der App. Welche Zielsprachen verfügbar sind, hängt vom gewählten Anbieter oder den installierten Offline-Modellen ab.

| Anbieter | Einrichtung und Verfügbarkeit |
| --- | --- |
| Bing (China), Google, Lingva | Kein Schlüssel nötig. Die Verfügbarkeit ist nicht garantiert. Neue Installationen verwenden standardmäßig Bing; bestehende Einstellungen bleiben erhalten. |
| Baidu, Alibaba Cloud, DeepL | Eigene Zugangsdaten erforderlich. Freikontingente und Gebühren hängen vom Anbieter und Ihrem Konto ab. Alibaba Cloud benötigt einen aktivierten Dienst für maschinelle Übersetzung sowie die Berechtigung `alimt:TranslateGeneral`. |
| Lokal offline | Die optionale lokale Engine installieren; sie nutzt einen Loopback-Dienst ohne Konto oder API-Gebühr. |

Bei Cloud-Übersetzungen wird nur der angeforderte Text an den ausgewählten Anbieter gesendet; es erfolgt kein automatischer Anbieterwechsel. OCR lädt keine Bilder hoch. Zugangsdaten der Anbieter werden **im Klartext** in der lokalen `settings.json` gespeichert; verdeckte Eingabefelder verschlüsseln sie nicht.

### 5. Optionale Komponenten direkt in der App installieren

<p align="center"><img src="docs/images/component-setup.png" width="720" alt="Einstellungen für optionale Komponenten mit Schaltflächen zum Installieren und Reparieren von OCR und Offline-Übersetzung"></p>

*Die Komponentensteuerung zeigt den beispielhaften Zustand „nicht installiert“ aus 1.2.1. Die normale Einrichtung erfolgt über diese Schaltflächen in der App; ein Terminalbefehl ist nicht nötig.*

1. Öffnen Sie **Einstellungen > Optionale Komponenten**.
2. Wählen Sie für OCR unter **Texterkennung (Chinesisch / Englisch)** die Aktion **Installieren / reparieren**, falls eine Komponente fehlt. Das Standard-ZIP enthält bereits die Engine und Modelle für Englisch und vereinfachtes Chinesisch; die Einrichtung prüft auch die native Laufzeit.
3. Wählen Sie für lokale Übersetzungen unter **Offline-Übersetzung (Chinesisch / Englisch)** die Aktion **Installieren / reparieren**. Lassen Sie die Einstellungen während des ersten Downloads geöffnet; planen Sie etwa **1 GB freien Speicherplatz** und **2 GB verfügbaren Arbeitsspeicher** ein.
4. Folgen Sie bei Bedarf dem Installationsprogramm von Microsoft. Meldet die Einrichtung **Neustart erforderlich**, starten Sie Windows neu und versuchen Sie es erneut. Ein Abbruch der App-Einrichtung macht eine bereits gestartete Windows-Laufzeitinstallation nicht rückgängig.
5. Wählen Sie **Lokal offline (Argos / LibreTranslate)** als Übersetzungsengine. Behalten Sie `http://127.0.0.1:5000` bei und verwenden Sie **Übersetzung testen**. Der Dienst startet bei Bedarf; **Dienst starten** und **Dienst stoppen** stehen ebenfalls zur Verfügung.

Die erste Einrichtung benötigt Internetzugriff auf die jeweiligen Downloadserver von GitHub, Python, Paket- und Modellanbietern sowie Microsoft. Fehlgeschlagene Downloads können wiederholt werden; gültige zwischengespeicherte Dateien werden wiederverwendet. Nach der Installation arbeiten OCR und lokale Übersetzung offline. Der Übersetzungsdienst läuft nach dem Schließen der App weiter, bis er gestoppt wird.

**Unterstützte Sprachen:** OCR erkennt gedrucktes Englisch und vereinfachtes Chinesisch. Die enthaltenen Übersetzungsmodelle unterstützen **Englisch ↔ vereinfachtes Chinesisch**. Die **20 Oberflächensprachen** der App bedeuten nicht, dass OCR oder Offline-Übersetzung 20 Sprachen unterstützen. Fehlende Modelle lösen niemals einen Wechsel zur Cloud-Übersetzung aus. [Offline-Einrichtung und Fehlerbehebung](docs/OFFLINE_TRANSLATION.md).

### 6. Nach Updates suchen · 1.2.2-Kandidat

<p align="center"><img src="docs/images/update-check.png" width="720" alt="Updatedialog mit einer simulierten neueren Veröffentlichung und einem beispielhaften Installationspfad"></p>

*Die angezeigte Version und der Pfad dienen der Veranschaulichung; dieses Bild kündigt keine verfügbare Veröffentlichung an.*

Verwenden Sie **Nach Updates suchen** im Infobereich-Menü oder unter **Einstellungen > Über**. Eine Prüfung lädt oder installiert nichts automatisch. Beim Download werden SHA-256 und Paketinhalt geprüft; **Neu starten und aktualisieren** benötigt eine separate Bestätigung. Der Updater ersetzt verwaltete Anwendungsdateien am vorhandenen Pfad, bewahrt Benutzerdaten und installierte Komponenten und hält Wiederherstellungsinformationen bereit, falls der Start fehlschlägt.

Das geordnete Kandidatenpaket enthält drei Dateien im Hauptverzeichnis: `KeeStashDock.exe`, `StartHere.md` und `update-manifest.json`. Anleitungen liegen in `Docs`, Lizenzhinweise in `Licenses`; Sprachen und optionale Komponenten behalten eigene Ordner. Lassen Sie das vollständige Paket zusammen. [Updates und Wiederherstellung](docs/UPDATING.md).

<a id="shortcuts"></a>
## Tastenkürzel

| Aktion | Standard-Tastenkürzel oder Geste |
| --- | --- |
| Schnellpanel anzeigen/ausblenden | **Alt+Q**; konfigurierbar |
| Eingegebenen Text speichern | **Enter** |
| Zeilenumbruch einfügen | **Alt+Enter** oder **Shift+Enter** |
| Screenshot | **Ctrl+Alt+S**; unabhängig konfigurierbar |
| Ausgewählten Screenshot-Bereich speichern | **Enter** in der Aufnahmeleiste von 1.2.2 |
| Screenshot abbrechen | **Esc** oder Rechtsklick |
| Räumliches Panel betrachten | **Rechte Maustaste** gedrückt halten und ziehen; in 1.2.2 konfigurierbar |
| Über die Auswahlblase kopieren/übersetzen | **Ctrl+Alt+Shift+C** / **Ctrl+Alt+Shift+T** in 1.2.2, nur solange die Blase sichtbar ist und die Tastenkürzel verfügbar sind |

<a id="data-and-upgrades"></a>
## Daten, Sicherung und Upgrades

Einträge, Bilder, erkannter Text und Sicherungen werden lokal gespeichert. Portable Daten liegen normalerweise in `Data` neben der ausführbaren Datei. Ist dieser Ordner nicht beschreibbar, verwendet die App `%AppData%\KeeStashDock` und zeigt den Ersatzpfad in den Einstellungen an. Über die App installierte Komponenten nutzen normalerweise `%LOCALAPPDATA%\KeeStashDock`, getrennt von Ihrer Sammlung.

Unter **Einstellungen > Daten** können Sie eine Sicherung exportieren/importieren und geplante Sicherungen einrichten. Das erste Upgrade von **1.2.1 erfolgt manuell**:

1. Exportieren Sie eine Sicherung und beenden Sie die App anschließend über ihr Menü im Infobereich.
2. Entpacken Sie das neue ZIP in einen separaten Ordner. Kopieren Sie bei geschlossener App den **gesamten aktiven Datenordner** in den `Data`-Ordner der neuen Installation. Zeigen die Einstellungen einen Ersatzpfad unter `%AppData%` an, verwenden Sie diesen Ordner als Quelle.
3. Behalten Sie die ursprüngliche Installation und die Sicherung. Starten Sie die neue Version und prüfen Sie Ihre Einträge, bevor Sie alte Kopien entfernen. Bewahren Sie für eine Rückkehr zur alten Version eine Sicherung vor dem Upgrade auf; ältere Datenbankschemata sind nicht austauschbar.

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

Das Anwendungssymbol wurde unter Anleitung des Maintainers mit KI erzeugt. Gestaltung und Herkunft sind in [docs/assets/README.md](docs/assets/README.md) dokumentiert.
