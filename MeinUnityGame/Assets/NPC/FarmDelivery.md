# Hans: Bauernhof → Marktstand

Nur Hans hat in SampleScene einen `FarmDeliveryJob` zugewiesen. Die Konfiguration
liegt am bestehenden Bauernhaus, zusammen mit dessen eigenem `BuildingWarehouse`.
Der Marktstand verwendet sein vorhandenes, separates Warenlager. Beide starten leer.

- Produktion: 6 Lebensmittel je tatsächlich gearbeiteter Spielstunde, also eine
  Einheit je 10 Arbeitsminuten. Angefangene Produktionszeit bleibt bei Pausen erhalten.
- Liefermenge: 10 Einheiten. Beide Werte stehen im Inspector am Bauernhaus unter
  `FarmDeliveryJob / Settings` und werden vor dem Play-Start eingestellt.
- Hans arbeitet weiterhin an seinem bisherigen Feld-Arbeitspunkt, 08:00–17:00 Uhr.
- Sobald eine Charge bereitliegt, geht er zum bestehenden Bauernhof-Zugangspunkt.
  Erst dort werden genau 10 Einheiten entnommen und als mitgeführte Lieferung geführt.
- Hans läuft über `RoadRouter` zum vorhandenen Marktstand-Arbeitspunkt. Erst nach
  Ankunft werden alle 10 Einheiten im Marktstand eingelagert und die mitgeführte
  Lieferung gelöscht. Bei abgewiesener Einlagerung behält er seine Ladung.
- Hunger, Durst und Energie haben weiterhin Vorrang. Eine Unterbrechung erhält die
  Ladung; nach der Versorgung bzw. dem Schlaf setzt Hans die Lieferung fort.
  Anschließend richtet er sich nach der aktuellen Arbeitszeit. Kein Produzieren
  während Wegen, Essen, Trinken, Schlaf oder Freizeit.
- Normale Zeitschritte und Vorspulen verwenden dieselben Produktions-, Ankunfts-
  und Bedürfnisgrenzen. Zyklus-Abkürzungen sind bei aktivem Warenfluss abgeschaltet.

## Prüfen in Unity

1. SampleScene im Play-Modus starten. F5 zeigt Lebensmittel bei Marktstand und
   Bauernhof (auch bei Bestand 0) sowie den bisherigen Tavernenbestand.
2. Hans mit der vorhandenen F4-Beobachtung verfolgen. Während der Arbeit steigt
   der Bauernhofbestand alle 10 gearbeiteten Spielminuten um 1.
3. Bei 10 Einheiten läuft Hans zum Bauernhoflager: dort sinkt es um 10. Während
   des Transports bleibt der Marktstandbestand gleich. Die NPC-Anzeige nennt die Ladung.
4. Am Marktstand steigt dessen Bestand um genau 10. Hans kehrt zur zeitlich
   passenden Tätigkeit zurück. Bis zur nächsten Arbeitsankunft gibt es keine Produktion.
5. Auch stunden-/tageweise vorspulen. Die neuen EditMode-Tests `NpcDeliveryTests`
   prüfen Warenbilanz, tatsächliche Abholung/Ankunft, Bedürfnis-Unterbrechungen,
   abgewiesene Einlagerung und große gegenüber kleinen Zeitschritten.

Warenbilanz ohne sonstige Zugriffe: produziert = Bauernhof + unterwegs + Marktstand.
Keine Händlerlieferung zur Taverne und keine Zahlungs-/Handelslogik. Waren und
Ladung gelten wie das bestehende Lagersystem für die laufende Sitzung, nicht als Savegame.
