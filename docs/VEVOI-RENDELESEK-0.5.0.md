# 0.5.0 – Vevői rendelések, foglalás és részátadás

A következő nagy fejlesztés a régi Python-program `raktar/bevetelezes.py` foglalási/átadási útvonalaira, a `rendeleskonyv/server.py` soronkénti átadási elvére és a kapcsolódó `teszt_megrendeles_atadas.py` viselkedési követelményeire épül. Az üzleti műveletek most a Windows-program saját céges adatbázisában, közös tranzakcióban történnek.

## Használat

1. Jelentkezz be adminisztrátorként vagy eladóként. Az **Eladás → Vevői rendelések** oldalon indíts új rendelést.
2. A keresőmezőkkel keress vevőt és terméket, majd nyomd meg a **Termékek / vevők keresése** gombot. Válassz aktív vevőt, raktárt és opcionális határidőt. A határidő formátuma ÉÉÉÉ-HH-NN.
3. A terméklistában látszik a más rendelések foglalása után szabad mennyiség. Adj tételeket a rendeléshez; a táblázatban a rendelt mennyiség, a nettó egységár és az áfa módosítható. A mentés ár- és termékadat-pillanatképet tárol, de még nem módosítja vagy foglalja a készletet.
4. A **napra foglalás / hosszabbítás** gomb legfeljebb a szabad készletből foglal. Az alap hét nap; 1–365 nap választható. Hiány esetén a rendelkezésre álló rész lefoglalódik, a maradék a Hiányzik oszlopban látható. A normál pénztár és a készletcsökkentő korrekció sem használhatja fel más rendelés foglalását.
5. A **Most átad** oszlopban add meg az átadandó mennyiséget, válassz készpénzes/kártyás fizetést, és szükség esetén jelöld a számlaigényt. Az átadás véglegesítése helyi eladási bizonylatot készít, csökkenti a készletet és a foglalást, majd növeli a rendelési sor átadott mennyiségét. Ezek egyetlen céges tranzakcióban történnek.
6. Részátadás után a maradék továbbra is követhető. Az összes sor átadása lezárja a rendelést; a kapcsolt eladási bizonylatok száma megjelenik az adatlap alján és az Eladás előzményeiben is kereshető.

## Biztonságos állapotok

- A foglalás lejáratkor törlés nélkül megszűnik beleszámítani a szabad készletbe. Lejárt foglalásból nem lehet átadni; ismételt foglalás a pillanatnyi szabad készlet alapján lehetséges.
- Foglalt rendelés szerkesztése előtt a foglalást fel kell oldani. Részben átadott rendelés tételei már nem módosíthatók. A maradék lemondható; a korábbi eladások megmaradnak.
- Dupla kattintás vagy elveszett válasz után ugyanaz a műveletazonosító nem hoz létre még egy eladást. Bizonytalan válasznál a képernyő zárol, az **Újraellenőrzés** ugyanazzal a kéréssel folytatja. Másik cégre váltás és kilépés az egyeztetésig nem engedett.
- A program a verzióütközést a háttérszolgáltatásban is ellenőrzi. A két párhuzamos foglalás nem oszthatja ki ugyanazt az utolsó darabot.
- A részátadások külön eladási bizonylatok, saját soronkénti kerekítéssel. A rendelés összege tervezett összeg; a tényleges eladási/fizetendő összegek az egyes bizonylatokon szerepelnek.
- A számlaigény rögzítése továbbra sem adóügyi számlakiállítás. Az új oldal nem küld e-mailt vagy külső számlázási kérést.

## Tárolás és migráció

A céges adatbázis sémája 3-as verziójú. A terméktörzs rekordonként indexelt SQL-tárolást, trigram keresőindexet, SQL-lapozást és külön auditnaplót kapott. A készletegyenleg a mozgásnaplóval azonos tranzakcióban frissül. A foglalások és a rendeléslisták külön indexeket használnak. A pénzszámítás C# `decimal`, az indexelt pénz- és készletmennyiségek rögzített skálájú egész számok; nem lebegőpontos pénzértékek.

Az első régi adatbázis megnyitásakor a program a `.migration-backups` almappában ellenőrzött, migráció előtti adatbázismentést készít. Ha ez nem sikerül, az átállás nem indul el. A sémaváltás tranzakciós. A régi JSON-fájlokból történő kezdeti migráció az eredeti fájlokat megtartja. A visszaállításhoz új céget használó meglévő mentési eljárás marad érvényben. Régi alkalmazásverzió az új sémát elutasítja.

A WAL mód `synchronous=FULL` beállítással működik; a gyorsítás nem a tartós mentés gyengítéséből ered. A tényleges csomagban SQLite 3.53.3 fut, külön regresszió ellenőrzi a WAL-reset javítást. Hivatalos háttér: [SQLite WAL](https://www.sqlite.org/wal.html), [decimal tárolási korlátok](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/types).

## A Python-átvétel folytatása

A teljes fájlleltár és a még hátralévő modulok a `PYTHON-WINDOWS-ATVETEL.md` és `legacy-python-inventory.json` fájlban vannak. Ebben a kiadásban a rendelés/foglalás/részátadás és a választható lépcsős árjavaslat készült el. Az automatikus árajánlat-import, a szabad szöveges rendelési tétel, a nagykerkosár, a beszerzési rendelés és az automatikus bevételezés–vevői rendelés összerendelés külön következő munkacsomag. A hiányzó árut most a meglévő bevételezésben lehet rögzíteni, majd a rendelés foglalását frissíteni.

## Ellenőrzött teljesítmény – 0.5.0

A `performance-50000.json` és `performance-orders-20000.json` a tesztfuttató gépi kimenete. 10 helyi, szintetikus mérés p95 értékei; azonos gépen, 16 logikai processzorral, .NET 10.0.13 és SQLite 3.53.3 mellett. A régi termékkeresés összehasonlítása ugyanannak a szintetikus JSON-állapotnak a deszerializálását és memóriabeli keresését méri. Az új érték az adatbázis-lekérést méri; egyik sem tartalmazza a teljes WPF/IPC útvonalat. A rendelési teszt az adatbázisban 20 001 rendelést tartalmaz (egy kiinduló rekord + 20 000 generált).

A mért keresési értékek: termék p95 42,05 ms; első termékoldal 5,61 ms; rendelés p95 8,18 ms; első rendelésoldal 7,57 ms. Ezek tesztadatokkal mért eredmények, nem minden hardverre és adatállományra szóló garanciák. Futtatás: `PCIMPRESSZ_BENCHMARK=1` környezeti változóval a Core tesztfuttatóban.

## 0.5.1 – Vevőkezelés és rendelési állapotok

A rendeléslista lenyílója olvasható szűrőket mutat. A választás közvetlenül frissíti a listát: Új, Megrendelve, Beérkezett, Részben átadva, Átadva, Lemondva, illetve az összes nyitott vagy minden rendelés. A rendelési állapot és a készlet/foglalás állapota külön oszlop.

A rendelés adatlapján az **Új / Megrendelve / Beérkezett** állapot kézzel menthető. Ettől önmagában nem változik készlet. Teljes foglaláskor a rendelés Beérkezett állapotra vált; az Átadva állapotot továbbra is a tényleges átadás zárja le. A Lemondva állapot a lemondási művelethez kötődik, hogy a foglalás is felszabaduljon.

Ha a vevő még nincs a partnertörzsben, jelöld az **Új vevő** mezőt. A név kötelező, telefon és e-mail megadható; az adószám és a számlázási cím lenyitható. A **Mentés a partnertörzsbe vevőként** pipa alapból be van kapcsolva. Kikapcsolva a vevő nem jelenik meg a partnertörzsben: az adatai a rendelésben maradnak, és átadáskor a helyi eladási bizonylat megőrzi a nevét. Számlaigényhez ilyenkor is teljes számlázási cím kell. A rendelési vevő mentésének alapértelmezése a Beállítások / Eladás és készlet részen változtatható.

A vevőfelvétel és a rendelés mentése közös tranzakció. Hibás rendelés nem hagy maga után új partnert; ugyanazzal a műveletazonosítóval megismételt mentés nem vesz fel még egy vevőt. Meglévő adószámnál az egyértelműen azonosítható aktív partner vevői szerepe megőrzően bővíthető; több találatnál a felhasználónak kell a meglévő vevőt kiválasztania.

A Nagykereskedések, E-mail és Nyomtatók beállításai között most közvetlenül is frissül a teljes űrlap. Az oldalváltás a kiválasztott beállításcsoporthoz kötődik, nem a három oldal közös beállításobjektumának cseréjéhez.

Az adatbázisséma 4-es verziójú; a korábbi rendeléseknél a hiányzó munkafolyamat-állapot Új értéket kap az olvasáskor. Az átállás előtt a meglévő ellenőrzött migrációs mentés készül. A WPF köztes fordítási könyvtára verziónként különül el a régi gyorsítótárak jogosultsági ütközéseinek elkerülésére.
