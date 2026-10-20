# Terméktörzs – 0.3

A terméktörzs a Partnertörzs mintájára készült helyi modul. Kategóriát, mértékegységet és terméket tart. Készlet, beszerzési rendelés, eladás és gyártás nincs benne.

## Használat

1. Indítás: a projekt gyökerében a Start.cmd. Ha van `.issuer` mappa, a fejlesztői telepítés a partnermodul mellett a terméktörzset is aláírja, és a licencbe bekerül a `catalog` modul.
2. Előbb vegyél fel mértékegységet (például név: Darab, kód: DB), utána terméket. A kategória nem kötelező.
3. A termék cikkszáma az aktív termékek között egyedi. A kategória és a mértékegység kódja ugyanígy.
4. Az alapparaméterek soronként `név=érték` formában kerülnek be, legfeljebb 30 sor.
5. Az archiválás inaktívvá teszi a rekordot. Használt kategória vagy mértékegység nem archiválható, amíg aktív termék vagy alkategória hivatkozik rá.
6. A CSV-export az aktuális keresést és szűrőt menti.

## InvenTree

A Terméktörzs oldal Import InvenTree-ből gombja a `part/category/` és `part/` végpontokat olvassa. Készletet, árat, beszállítót és darabjegyzéket nem kér le. A kulcs nem kerül mentésre. A már meglévő rekordot külső azonosító, kód, cikkszám vagy név alapján kihagyja, nem írja felül.

## Tárolás

Egy cég terméktörzse: `.local/companies/<cégazonosító>/modules/catalog/catalog.json`. A három lista egy fájlban van, hogy a hivatkozás és az import egy írásban maradjon. Az előző változat `catalog.json.bak`.

Korlát: 50 000 termék, 10 000 kategória, 500 mértékegység, 200 000 naplóbejegyzés, 64 MB.

## Eltérés a tervtől

Az architektúraterv első üzleti lépése a Catalog és az Inventory együtt, InvenTree-adapteren, az InvenTree-t véve hiteles forrásnak. Itt csak a terméktörzs készült el, a Partnertörzzsel azonos helyi JSON-tárolóval. Az InvenTree csak olvasott törzsimport, nem a készlet hiteles forrása. A Shell mezői szöveg, többsoros szöveg és jelölőnégyzet; ezért a kategória és a mértékegység kódként jelenik meg, nem legördülő listaként.
