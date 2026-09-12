# Werkzeuge am Arbeitsplatz

Bauernhof, Mine und Schmelze besitzen jeweils ein Werkzeug als Startbestand.
Es gehört zum `BuildingWarehouse`; NPCs halten nur eine Referenz darauf.
Der bestehende allgemeine Warenbezeichner `Werkzeuge` wird weiterverwendet,
einschließlich Peters unveränderter Werkzeugproduktion.

Testwerte im jeweiligen Gebäude-Warenlager, vor Play einstellbar:

- `Requires Work Tools`: eingeschaltet für diese drei Arbeitsplätze.
- `Tool Maximum Durability`: 100.
- `Tool Wear Per Work Hour`: 10; ein Werkzeug hält damit zehn tatsächliche Spiel-Arbeitsstunden.
- `Initial Stock / Werkzeuge`: 1.

Ein Werkzeug wird am Arbeitsplatz benutzt, übrige Werkzeuge sind frische Reserven.
Der Bestand enthält das verwendete Werkzeug, bis es bei Haltbarkeit 0 entfernt
wird. Eine vorhandene Reserve wird anschließend verwendet. Das Hinzufügen neuer
Werkzeuge repariert ein bereits benutztes Werkzeug nicht.

Verschleiß entsteht nur im Zustand `Working`, mit vorhandenem Werkzeug. Wege,
Lieferungen, Essen, Trinken und Schlaf verbrauchen keine Haltbarkeit. Bruch ist
eine eigene Zeitgrenze in der gemeinsamen Simulation, auch beim Vorspulen.
Ohne Werkzeug läuft kein Produktionsfortschritt weiter. Bereits geleistete
Teilarbeit bleibt erhalten; Tagesablauf, Bedürfnisse und Lieferungen bleiben aktiv.

Mine und Schmelze haben noch keine Erz-/Eisenproduktion. Hier verschleißt das
Werkzeug bereits während der bestehenden Arbeitstätigkeit; die gemeinsame
Produktionssperre ist vorbereitet. Neue Produktionslogik wird nicht hinzugefügt.

## Testen

1. SampleScene im Play-Modus starten und F5 öffnen. Bei Bauernhof, Mine und
   Schmelze stehen Werkzeugbestand sowie aktuelle/maximale Haltbarkeit.
2. Zur Arbeitszeit vorspulen: nur während Arbeit sinkt die Haltbarkeit.
3. Für einen kurzen Bruchtest vor Play die maximale Haltbarkeit auf 10 stellen.
   Bei Verbrauch des letzten Werkzeugs zeigt F5 die Produktionssperre.
4. Hans produziert dann keine weiteren Lebensmittel. Bereits eingelagerte
   Lebensmittel und laufende Lieferungen bleiben erhalten.
5. Mit zwei Startwerkzeugen lässt sich der automatische Wechsel zur Reserve testen.

Es gibt noch keinen Werkzeugtransport von der Schmiede zu diesen Arbeitsplätzen.
Nach dem Verbrauch des Startbestands bleiben sie deshalb ohne Werkzeug, bis
Werkzeuge über die vorhandene Lager-API hinzugefügt werden. Kein automatisches
Auffüllen, keine Reparaturen und kein Geldsystem.

EditMode-Tests: `WorkToolTests`, `WorkplaceToolsTests`. In dieser Umgebung sind
weder Unity noch ein C#-Compiler installiert; die Tests müssen in Unity ausgeführt werden.
