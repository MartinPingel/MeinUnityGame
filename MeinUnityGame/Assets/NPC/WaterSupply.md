# Gunnars Wassernachschub

`WaterSupplyJob` auf Gunnar nutzt die gemeinsame NPC-Transportlogik. Die Ware
bleibt technisch `Getränke`; F5 bezeichnet sie als `Wasser (Getränke)`.
Alle NPCs verbrauchen weiterhin eine Einheit je abgeschlossenem Trinken.
Es gibt keinen zweiten Wasserbestand und keine Getränkeherstellung.

Im Inspector vor Play einstellbar: `Minimum Stock` = 20 (Nachschub strikt darunter),
`Delivery Quantity` = 40. Der vorhandene Tavernen-Startbestand von 100 bleibt erhalten.
Neue Gänge beginnen während Gunnars unveränderter Arbeitszeit 10–23 Uhr,
von der Taverne aus. Erst am Brunnen entsteht eine transportierte Ladung;
erst bei Ankunft an der Taverne wird sie ins Gebäudelager eingelagert.
Der Brunnen ist vorerst eine unerschöpfliche Quelle ohne eigenes Lager.

Bedürfnisse unterbrechen den Gang; aufgenommene Ladung bleibt erhalten.
Eine geladene Lieferung darf nach Feierabend fertiggestellt werden.
Bei einer Bedürfnis-Ankunft an der Taverne wird Wasser vor dem Essen/Trinken
eingelagert. Wenn Gunnar selbst durstig ist und das Lager leer ist, holt er
während seiner Schicht erst Wasser; Essen und Schlafen können auch diesen
Gang unterbrechen. Außerhalb seiner Schicht wartet er zuhause. Kein Trinken
füllt bei leerem Lager Flüssigkeit auf. Andere Berufe verwenden diese
Sonderbehandlung nicht.

## Test in Unity

1. SampleScene öffnen. Für einen schnellen Test vor Play den Getränke-Startbestand
   der Taverne auf 19 setzen (für einen Leertest den Getränke-Eintrag entfernen).
2. Play starten, F5 öffnen, zur Arbeitszeit warten und Gunnar beobachten.
3. Auf dem Hinweg bleibt der Bestand gleich; am Brunnen bekommt Gunnar 40 Wasser
   als Ladung. Auf dem Rückweg gibt es noch keinen Lagerzugang.
4. Erst bei Ankunft steigt der Bestand um 40. Eigenes oder fremdes Trinken
   verbraucht anschließend jeweils 1. Oberhalb der Schwelle bleibt Gunnar arbeiten.
5. Hunger-/Schlafpausen und mehrtägiges Vorspulen prüfen. Testwerte nach Play
   wieder zurücksetzen. F4 zeigt Gunnars bestehende NPC-Anzeige samt Ladung.

EditMode-Tests: `WaterSupplyTests` (Ankunft, Schwelle, Durst bei leerem Lager,
Hunger, Schlaf, fehlgeschlagene Einlagerung, Feierabend, Vorspulen/Mengenbilanz).
Diese Tests wurden in der Bearbeitungsumgebung ohne Unity nicht ausgeführt.
