# NAV-bevételezés – 0.4.18

1. Számlák → Bejövő számlák → NAV-számlák lekérése; válassz legfeljebb 35 napot.
2. Kattints a számlára. Alatta automatikusan megjelennek a tételek. A szállító hiánya nem akadály: az Új szállító gomb előtölti a nevet és adószámot. Az előnézet nem rögzít számlát vagy készletmozgást.
3. A fejléces táblázatban megnevezés, mennyiség, egység, nettó beszerzési ár, beszerzési áfa, bruttó kisker ár, haszonkulcs, eladási áfa és párosítás látható. A sorra kattintva a termékkeresés, a szolgáltatásjelölés és a termékadatok is elérhetők.
4. A További termékadatok kitöltése rész a terméktörzs összes jelenlegi mezőjét használja: név, cikkszám, gyártói cikkszám, kategória, egység, revízió, vonalkód, leírás, paraméterek, beszerzési/eladási/aktív jelölések, megjegyzés. Az eladási ár és áfa az alapadatoknál szerkeszthető. A későbbi súly/méret mezőket előbb a közös törzshöz és annak mezőleírásához kell hozzáadni.
5. Az Ár számítása haszonkulccsal gomb alapból 30%-os beszerzési felárral számol, az eladási áfát is hozzáadva. Képlet: nettó beszerzés × (1 + haszonkulcs / 100) × (1 + eladási áfa / 100), a régi raktárprogramhoz hasonlóan a legközelebbi 5 Ft-ra kerekítve. Példa: 100 Ft nettó, 30% és 27% áfa → 165 Ft bruttó árjavaslat. Ez csak gombnyomásra történő javaslat, kézzel felülírható; a haszonkulcs beszerzési felár, nem árbevétel-arányos árrés.
6. A Termék és ár mentése azonnal a közös terméktörzsbe ment, meglévő terméknél verzióellenőrzéssel. A szállítói számlán szereplő eltérő megnevezés nem írja át automatikusan a törzsben tárolt nevet. Az új termékhez szükséges cikkszámot és mértékegységet is add meg; az egyedi egységnek már léteznie kell a mértékegységtörzsben (a DB létrehozható innen). A számla Mentés gombja a módosított termékadatokat is elmenti, majd a számlapiszkozatot rögzíti. Ezek külön mentési lépések: ha a számla hibás, egy korábban sikeresen mentett termék megmarad.
7. Mentés után válassz Bevételezés / Költség / Függőben besorolást. Készlet csak bevételezéskor változik. Az ismételten megnyitott számla a helyi példányt mutatja, nem új készletmozgást hoz létre.

## Állapotszínek

Zöld háttér és Bevételezve állapot csak a ténylegesen készletre könyvelt számlán szerepel. Új, függő, költségként kezelt vagy visszafordított számla alapból fehér marad. A kiválasztást a rendszer szokásos kijelölése is mutatja. A helyi állapot számlaszám és szállítói adószámtörzs alapján egyezik; más szállító azonos számlaszáma nem kap zöld jelzést.

Beállítások → Számlák · megjelenítés: mindkét háttér #RRGGBB alakban menthető (alapérték: #D9F2DC és #FFFFFF). A felirat világos/sötét színe automatikusan követi a hátteret. Ezek a gép megjelenítési beállításai, új adatbázismező és céges adatmigráció nélkül.

## Ellenőrzés és határok

190 üzleti/Core/Host teszt, egérgörgő-próba és 70 WPF megjelenítési ellenőrzés sikeres, kötési hiba nélkül. Szintetikus NAV-válaszokkal ellenőrzött előnézet hiányzó szállítóval, változatlan készlet, termékpárosítás, ármentés a meglévő név/leírás/gyártói cikkszám megőrzésével, előnézetből mentés, színjelölés és visszafordítás.

A támogatott NAV-kör változatlan: normál HUF-számlák, 0/5/18/27% áfa. Devizás, módosító, sztornó és különleges áfás számla automatikus bevételezése további fejlesztés. A funkció nem állít ki számlát; az élő számlázó API továbbra sincs aktiválva. A valódi céges NAV-kapcsolattal történő használati próba külön, a felhasználó gépén történhet.

Indítás: zárd be a régi programot és az esetleg külön futtatott háttérfolyamatot, majd indítsd a Start.cmd fájlt. A fejlesztői ablakrendező a NAV-listát és az alatta lévő szerkesztőt egy csoportként méretezi; ez megőrzi a listához tartozó tételsorok közvetlen elhelyezését.