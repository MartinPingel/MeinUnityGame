# Erz bis zum Händler: Kettentest ohne Haltbarkeit

Alle Warenbestände der Kette starten leer. Die vorherigen 20 Eisen in der
Schmelze sind entfernt. Die Startwerkzeuge in Bauernhof, Mine und Schmelze
sind entfernt; deren Werkzeugpflicht und Verschleiß sind deaktiviert.
Der vorhandene Haltbarkeitscode bleibt unbenutzt für einen späteren Schritt erhalten.

| Zuständig | Vorgang | Einstellbarer Testwert |
|---|---|---|
| Justus | Eisenerz abbauen | 6 pro Spiel-Arbeitsstunde |
| Justus | Mine → Schmelze liefern | 10 Eisenerz |
| Jan | Eisenerz → Eisen | 2 → 1 je 20 Arbeitsminuten |
| Peter | Schmelze → Schmiede abholen | 10 Eisen, sobald Schmiede weniger als 2 hat |
| Peter | Eisen → Werkzeuge | 2 → 1 je 30 Arbeitsminuten |
| Marcel | Schmiede → Marktstand abholen | 2 Werkzeuge |

Die vorhandenen Arbeitszeiten, Wege und Bedürfnisse gelten weiter. Produktion
zählt nur während tatsächlicher Arbeit. Fehlende Eingaben pausieren den
Produktionsfortschritt; nachgelieferte Ware erzeugt keine rückwirkende Produktion.
Die Mine ist zunächst eine unerschöpfliche Rohstoffquelle, produziert jedoch
ausschließlich durch Justus' Arbeitszeit. Ein begrenztes Erzvorkommen gibt es noch nicht.

Transport nutzt die gemeinsame NPC-Bewegung. Abholung entnimmt die gesamte
Charge aus dem Quelllager. Während des Weges bleibt sie als Ladung beim
Transportauftrag; erst Ankunft und erfolgreiche Einlagerung buchen sie ins Ziel.
Bedürfnispausen behalten die Ladung. Werkzeuge im Marktstand gehören dem Gebäude.
Es gibt noch keinen Verkauf und keine Verteilung an Arbeitsplätze.

## Test in Unity

SampleScene starten, F5 öffnen und mit F4 Justus, Jan, Peter und Marcel beobachten.
Anfangs müssen Mine, Schmelze, Schmiede und Marktstand bei den Kettenwaren leer sein.
Während Justus arbeitet, steigt Eisenerz. Ab 10 Einheiten geht er zur Schmelze.
Die Schmelze bekommt die 10 erst bei seiner Ankunft. Jan benötigt tatsächlich
Erz und Arbeitszeit. Erst sein Eisen ermöglicht Peters Abholung und Produktion.
Marcel liefert fertige Werkzeuge zum Marktstand; F5 zeigt auch Nullbestände.
Mit GameClock normal laufen lassen und jede Ankunft beobachten.

Für die Mengenprüfung bei den Standardrezepten gilt:

Abgebautes Eisenerz = alles gelagerte/getragene Eisenerz
+ 2 × alles gelagerte/getragene Eisen
+ 4 × alle gelagerten/getragenen Werkzeuge.

`ProductionChainTests` prüft fehlende Rohstoffe und diese Bilanz über sechs
Spieltage mit Bedürfnispausen. Die Testumgebung nutzt kostenlose Versorgung,
damit Lebensmittelmangel nicht den isolierten Kettentest stoppt.
Die Tests sind hier ohne Unity/C#-Compiler nicht ausgeführt worden.
