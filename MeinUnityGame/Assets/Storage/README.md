# Gebäude-Warenlager

`BuildingWarehouse` sitzt am Gebäude/Lagerort. In SampleScene liegt es direkt auf
`Marktstand`, ohne Referenz auf Marcel oder andere NPCs. Der Anfangsbestand ist leer.
Weitere Gebäude können dieselbe Komponente unverändert verwenden; jedes Lager
verwaltet seinen eigenen Bestand. Ein NPC-Wechsel ändert das Lager nicht.

## API

Warentypen sind stabile, frei wählbare Kennungen (Groß-/Kleinschreibung wird
unterschieden), Mengen sind ganze Einheiten. Es gibt noch keinen Warenkatalog.

```csharp
using Village.Storage;

BuildingWarehouse warehouse = marketStand.GetComponent<BuildingWarehouse>();
warehouse.Add("getreide", 10);
int quantity = warehouse.GetQuantity("getreide");
bool available = warehouse.Has("getreide", 3);
bool removed = warehouse.TryRemove("getreide", 3);
StockRecord[] snapshot = warehouse.GetSnapshot();
```

Hinzufügen/Entnehmen erfordert positive Mengen. Ungültige Kennungen/Mengen und
Überläufe werden abgewiesen. Bei unzureichendem Bestand liefert `TryRemove` false,
ohne Teilentnahme. Nicht vorhandene Waren haben Menge 0; leere Einträge werden
entfernt. Snapshots können den Lagerbestand nicht verändern.

## Test in Unity

1. SampleScene starten: oben rechts erscheint `Warenlager – Marktstand` mit
   `Leer – keine Waren vorhanden.`. F5 blendet das Panel aus/ein.
2. Für einen manuellen Test vor Play auf Marktstand in `BuildingWarehouse` unter
   `Initial Stock` Einträge mit Kennung und positiver Menge konfigurieren. Die
   Anzeige listet diese beim nächsten Start auf. Testwerte danach zurücknehmen.
3. EditMode-Tests `WarehouseStockTests` prüfen Bestände, unabhängige Lager,
   fehlgeschlagene Entnahmen, ungültige Mengen, Überläufe und Snapshot-Isolation.

Die Anzeige liest laufend den aktuellen Gebäudebestand und ist ausschließlich
im Editor und in Development Builds verfügbar. Sie kann auch als Komponente
deaktiviert werden und verändert weder NPC-Beobachtung noch Uhr/WaitMenu.

Laufzeitänderungen bleiben innerhalb der laufenden Sitzung erhalten, auch beim
Deaktivieren/Aktivieren des Gebäudes. Speichern über Spielneustarts ist noch nicht
implementiert. Keine Produktion, Warenbewegung, NPC-Inventare oder Handelslogik.
