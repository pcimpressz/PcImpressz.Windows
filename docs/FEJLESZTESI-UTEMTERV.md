# Fejlesztési ütemterv – PC-Impressz Windows Edition

Frissítve: 2026. október 23. Aktuális változat: 0.5.1.

Ez az aktív fejlesztési sorrend. Az eredeti architektúraterv történeti kiindulópont; a tényleges forráskód és az alábbi állapot az irányadó. Az ütemezés függőségi sorrendet ad, naptári vállalást nem.

## Elkészült alapok

- Moduláris .NET 10 / WPF Windows-felület, külön helyi Host, céghez kötött bejelentkezés és műveleti jogosultságok, aláírt modulok és licencellenőrzés.
- Saját céges SQLite-tárolás, tranzakciók, migráció előtti ellenőrzött mentés és visszaállítás. A napi működéshez nincs InvenTree-szerverfüggés.
- Partner- és terméktörzs, készletnapló, bevételezés, eladás, pénztár, NAV-ból és kézi piszkozatból indított bevételezés. A részletes korlátokat a modulútmutatók tartalmazzák.
- Indexelt termék- és rendeléslista, FTS5 trigram keresés, SQL-lapozás és készletegyenleg-vetület. Közös decimal pénz- és áfaszámítás; a NAV eredeti sorösszegeinek megőrzése.
- Vevői rendelés, lejáró foglalás és hosszabbítás, részátadás eladási bizonylattal, lemondás, ismétlésvédelem. A más rendeléshez foglalt készletet a normál eladás is védi.
- 0.5.1: új vevő opcionális partnertörzsbe mentése; alapból vevőként mentés, beállítható alapértékkel. Új / Megrendelve / Beérkezett rendelési állapot; a részátadás, teljes átadás és lemondás a tényleges műveletből következik. Javított rendelési szűrő és közvetlen nagyker/e-mail/nyomtató beállításváltás.
- A helyi vizuális szerkesztő forrásainak verziókövetésbe vétele: ablakelemek, navigációs feliratok és oldalsávszélesség. A korábbi felületmódosítások megőrizve.

## 1. Következő nagy munkacsomag: ajánlatból teljesíthető rendelés

Python-forrás: `raktar/arajanlat.py`, `rendeleskonyv/server.py`, `rendeleskonyv/keszlet.py`, `raktar/bevetelezes.py`.

- Árajánlat mentése, verziózása, érvényessége és PDF-előnézete; elfogadott ajánlatból egyszer létrehozható vevői rendelés.
- Rendelési sorok összekötése a beszerzési igénnyel és a beérkező mennyiséggel; hiány, foglalás, részbeérkezés és részátadás követhető kapcsolata.
- Beérkeztetéskor a kapcsolt rendelés elérhető mennyiségének frissítése; állapotváltozás a valódi készletmozgással egy tranzakcióban.
- Előzmény és hivatkozás az ajánlat, rendelés, bevételezés és átadás között.

Átvételi feltétel: a részbeérkezés/részátadás, lemondás, ismételt kérés, párhuzamos szerkesztés és cégvédelem tesztelt; a meglévő partner, ár- és áfapillanatképek megmaradnak. A lista SQL-oldalon szűr és lapoz.

## 2. Pénztár és számlázási folyamat lezárása

Python-forrás: `raktar/elado.py`, `raktar/szamlazas.py`, `raktar/kimeno.py`, `raktar/bizonylatok.py` és kapcsolódó tesztjeik.

- Eladási bizonylat és adóügyi számla egyértelmű kapcsolata; előleg és végszámla, jóváírás és sztornó egymáshoz rendelve.
- Számlakimenet és nyomtatási előnézet, ismételt kérés és válaszvesztés kezelése; könyvelhető összegek és eredeti adatok megőrzése.
- A jelenlegi kézi/NAV bevételezés és a pénztári számlaigény teljes folyamatának közös ellenőrzése.

Átvételi feltétel: automatikus teszt külső API-szimulációval; tényleges számlakiállítás és e-mail-küldés külön felhasználói művelet. A jelenlegi számlaigény önmagában nem számlakiállítás.

## 3. Nagykereskedések és beszerzés

- Beszállítói ár- és készletfrissítés háttérben, megszakítható munkával és utolsó sikeres frissítés jelzésével.
- Beszállítói termékazonosítók, árösszevetés, soronkénti nagykerkosár és rendelési kapcsolatok.
- Mentett kosár és előnézet; küldés előtt a felhasználó ellenőrizhető összesítést kap.

Átvételi feltétel: hibás vagy elérhetetlen beszállító nem fagyasztja a felületet; régi árak és frissítési hibák láthatók; rendelésküldés nem ismétlődik meg válaszvesztéskor.

## 4. Leltár, garancia és szerviz

- Raktárankénti leltár, eltérés-előnézet és auditált véglegesítés, foglalások védelmével.
- Sorozatszám, garanciális előzmény, szervizmunkalap és a kapcsolódó alkatrészmozgások.
- A Python-munkafolyamatok átültetése külön modulhatárokkal és célzott regressziókkal.

## 5. Pénzügy és további modulok

Pénzügy/bank/könyvelési export és vezetői kezdőlap; ezek után gépösszeállító, kompatibilitás, tudástár és kellékkereső. Minden modul előtt a megfelelő aktív Python-forrás és tesztjeinek részletes működésvizsgálata szükséges.

## Minden munkacsomag közös követelménye

- A gyorsaság elsődleges: nincs teljes termék-, rendelés- vagy készletnapló betöltés a képernyőhöz. Keresés, rendezés és lapozás az indexelt tárolón történik.
- Háttérben futó I/O, megszakítható keresés, rövid tranzakciók és műveletenkénti ismétlésvédelem.
- Pénz és áfa decimal alapú; pénzösszeg nem számolható lebegőpontos típussal.
- Adatséma-változás előtt ellenőrzött mentés; meglévő dokumentumok, jogosultságok és felületbeállítások megőrzése.
- Módosítás után célzott üzleti és felületi ellenőrzés. Tárolási/keresési változásnál nagy adatos mérés is szükséges.
- Az importált Python-fájlok leltára nem azonos a teljes funkcióátvétellel. Csak a tesztelt, bekötött működés jelölhető késznek.

## Ellenőrzött kiinduló állapot

2026. október 23-án a helyi 0.5.1 forráson: Release fordítás 0 hibával és 0 figyelmeztetéssel; 225/225 működési teszt; sikeres görgetési próba; 73/73 WPF-render adatkapcsolási hiba nélkül. A célzott próba a három beállítási oldal közvetlen váltását és a rendelési lenyíló tényleges feliratait is ellenőrzi. Az ismételt fordítás sikeres.

A korábbi mérés 50 000 terméket és készletmozgást, illetve 20 000 rendelést vizsgált. Az eredmények gép- és tesztadatfüggő referenciák, nem minden munkafolyamatra vállalt válaszidők.

## Kapcsolódó dokumentáció

- [Dokumentáció tartalomjegyzéke](README.md)
- [Python-átvételi jegyzék](PYTHON-WINDOWS-ATVETEL.md) és [gépi fájlleltár](legacy-python-inventory.json)
- [Vevői rendelések, 0.5.0–0.5.1](VEVOI-RENDELESEK-0.5.0.md)
- [50 000 tételes mérés](performance-50000.json), [20 000 rendelés mérése](performance-orders-20000.json)
- [Eredeti architektúraterv](PC-Impressz-architektura-es-megvalositasi-terv.md)
