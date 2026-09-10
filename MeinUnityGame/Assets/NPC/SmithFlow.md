# Schmelze → Schmiede: Peter

Nur Peter erhält den neuen `SmithDeliveryJob` am bestehenden Schmiede-Gebäude.
Hans verwendet weiterhin seinen bisherigen `FarmDeliveryJob`. Beide verwenden
dieselbe NPC-Bewegung, Ladungsverwaltung, Zeitsimulation und Bedürfnispriorität.

## Testwerte (vor Play im Inspector einstellbar)

- Schmelze / BuildingWarehouse / Initial Stock: einmalig **20 Eisen**.
  Dies ist Testbestand, keine Eisenproduktion und kein automatischer Nachschub.
- Schmiede / SmithDeliveryJob / Delivery Quantity: **10 Eisen pro Abholung**.
- Iron Per Cycle: **2**; Tools Per Cycle: **1**.
- Work Minutes Per Cycle: **30 Spielminuten tatsächlich geleisteter Arbeit**.
- Peters bestehende Arbeitszeit bleibt **08:00–18:00 Uhr**.

Peter verarbeitet vorhandenes Eisen zuerst. Fehlt genug Eisen für einen Zyklus
und liegt mindestens eine vollständige Liefermenge in der Schmelze, holt er
während der Arbeitszeit Nachschub am vorhandenen Schmelzen-Zugang ab.
Erst bei Abholung werden 10 Eisen aus dem Schmelzenlager entnommen. Die Ladung
bleibt unterwegs in seiner Transportaufgabe erhalten und wird erst bei Ankunft
am bestehenden Schmiede-Arbeitspunkt vollständig eingelagert.

Durst, Hunger und Schlaf dürfen den Weg oder die Verarbeitung unterbrechen.
Die Ladung und bereits geleistete Produktionszeit bleiben erhalten. Wege,
Versorgung, Schlaf, Freizeit und Zeiten ohne genügend Eisen zählen nicht als
Produktionszeit. Nach erledigter Lieferung gilt die aktuelle Arbeitszeit.
Eine zu Feierabend bereits aufgenommene Ladung wird noch abgeliefert, sofern
kein vorrangiges Bedürfnis dies unterbricht; anschließend geht Peter nach Hause.

Am Ende eines Arbeitszyklus führt `WarehouseStock.TryConvert` Verbrauch und
Zugang gemeinsam durch: **−2 Eisen, +1 Werkzeug**. Fehlender Input oder ein voller
Outputbestand verändert nichts. Andere Lageroperationen bleiben unverändert.

## Prüfen

1. SampleScene starten, F5 öffnen: oben stehen Schmelze/Eisen sowie
   Schmiede/Eisen und Schmiede/Werkzeuge, einschließlich Nullbeständen.
2. Peter folgen: Schmelze sinkt bei seiner Abholung von 20 auf 10, Schmiede
   bleibt zunächst bei 0. Erst nach Rückkehr steigt sie auf 10.
3. Nach 30 echten Arbeits-Spielminuten in der Schmiede: Eisen 8, Werkzeuge 1.
   Während Transport oder Bedürfnispausen findet keine Verarbeitung statt.
4. Nach Verarbeitung des Testbestands sind insgesamt 10 Werkzeuge möglich.
   Warenbilanz: Schmelze-Eisen + Schmiede-Eisen + Ladung + 2 × Werkzeuge = 20.
5. `SmithFlowTests` und `WarehouseRecipeTests` im EditMode-Test Runner ausführen.
   Sie prüfen Wege/Ankunft, Ladeunterbrechungen, Arbeitszeit, Materialmangel,
   Überlauf, atomare Rezepte, Warenbilanz und Vorspulen gegenüber kleinen Schritten.

Keine Verkäufe, Preise, Geld, Händlertransporte oder zusätzliche Produktion.
Bestände und Ladung gehören wie bisher zur laufenden Sitzung, nicht zu einem Savegame.
