# Készlet, eladás és cégmentés – v0.4

## Fejlesztési irány

A közös, érvényes irányelvek a projekt gyökerében a `KOZOS-MUNKANAPLO.md` fájlban találhatók. .NET 10, WPF, külön aláírt/licencelt modulok, Host oldali ellenőrzés, cégenként tranzakciós SQLite, pontos decimal számítás és automatizált üzleti/folyamat/felületi ellenőrzés az alap.

A SQLite-illesztő a hivatalos [Microsoft.Data.Sqlite 10.0.13](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.13). A mentéshez az illesztő [online mentési API-ját](https://learn.microsoft.com/dotnet/standard/data/sqlite/backup) használjuk; nem másolunk élő adatbázist egyszerű fájlmásolással.

## Első használat és adatátvétel

1. A projekt `Start.cmd` fájljával indulj. A fejlesztői kulcsok meglétekor az öt modul friss aláírást és fejlesztői jogosultságot kap.
2. A kiválasztott cég első adatlekérdezése ellenőrzi és egy tranzakcióban átveszi a korábbi partner-, termék- és bevételezési JSON-állapotot, valamint az opcionális dossziépillanatképet.
3. Sérült vagy újabb, ismeretlen formátumú adatnál a migráció megáll; nincs csendes üresre váltás. A régi fájlok változatlanok maradnak. Siker után a Host a `companies/<azonosító>/company.db` állományt használja, a régi JSON-fájlokat nem írja tovább.
4. A régen „Bevételezve” jelölt számlákból nem készül automatikusan múltbeli készletmozgás. A tényleges induló mennyiséget ellenőrzés után nyitókészletként add meg. A korábban importált számlák áfa- és összegadatait is ellenőrizni kell: a javítás nem találja ki visszamenőleg az eredeti forrásadatot.

Új cég továbbra is teljesen üresen indul, InvenTree-kapcsolat nélkül is. A cégjegyzékben a meglevő azonosítók és az installáció azonosítója megmaradnak.

## Készlet

- A Készlet oldalon terméknév/cikkszám alapján kereshetsz. A választó legfeljebb 200 találatot ad; a keresés után használd a Választók frissítése gombot.
- Válassz terméket, adj meg raktárat, előjeles mennyiséget és indoklást. Pozitív szám növeli, negatív csökkenti a mennyiséget. Nyitókészlet és korrekció is naplózott kézi mozgás.
- A mentés megerősítést kér. Negatív készletet nem engedünk. Legfeljebb négy tizedesnyi mennyiség támogatott, automatikus egységátváltás nincs.
- A készletegyenleg minden raktárat külön mutat; a mozgásnapló a legutóbbi 200 mozgást. A listakeresés termékre/raktárra szűkít; egyszerre legfeljebb 500 egyenlegsor jelenik meg, több találatnál a képernyő további szűkítést kér.
- A kijelölt kézi mozgás ellenmozgással visszafordítható, ha ettől sem lesz negatív készlet. A bevételezési és eladási mozgások nem fordíthatók vissza külön, a saját bizonylatuktól elszakítva.
- A terméktörzs Darabszám oszlopa az összes raktár összesített készletét mutatja, és erre rendezhető. A már mozgással rendelkező termék mértékegységét nem lehet utólag átírni.

## Bevételezés

A számla rögzítése/importálása és a készletre vétel két külön lépés. Az új számla még nem készlet. A Bevételezve véglegesítés a számlaállapotot és az összes készletes tétel bevétjét egyetlen adatbázis-tranzakcióban rögzíti. Hiba esetén egyik sem marad félkészen. Ismételt kérés nem növelheti kétszer a készletet.

A szolgáltatás jelölőnégyzetet a kezelő kapcsolja be. A névben levő „díj” vagy „szállítás” szó nem minősít automatikusan szolgáltatássá egy árut. A szolgáltatás nem mozgat készletet.

A kezdeti import HUF pénznemet és a 0/5/18/27 százalékos áfákat kezeli. A NAV XML-ből az eredeti áfakulcs és a rendelkezésre álló számlaösszesítő megmarad. Ha az eredeti nettó/áfa/bruttó összeg eltér a kiszámolt tételösszegtől, az import vagy véglegesítés elutasítást ad. A szállító ismert adószámának is egyeznie kell. Mértékegység-eltérést és négy tizedesnél pontosabb mennyiséget/egységárat nem váltunk át vagy kerekítünk el észrevétlenül.

CSV/XLSX minimális oszlopok: termék neve, mennyiség, nettó egységár, áfa. Opcionális: cikkszám, vonalkód, mértékegység, szolgáltatás (igen/nem). Az egyszerű táblázat HUF összegeket jelent; áfa nélkül nincs import. A fájl 1,5 MB, az XLSX kibontott tartalma 8 MiB, a táblázat 400 tétel/64 oszlop lehet. A PDF és az élő NAV-lekérdezés továbbra sincs elkészítve.

## Eladás

1. A terméktörzsben jelöld eladhatónak az értékesíteni kívánt terméket. Az Eladás választójában csak aktív, eladható termék szerepel.
2. Válassz terméket, mennyiséget, nettó egységárat, áfát, vevőt és raktárat. Partner nélküli eladásnál Alkalmi vevő szerepel. Termék- és vevőkeresés külön mezővel történik.
3. A Tétel hozzáadása gomb a kosárba teszi a megadott mennyiséget és árat. A kiválasztó mezőinek későbbi változtatása nem módosítja a már kosárba tett sort; töröld és add hozzá újra, ha javítani szeretnéd.
4. Az Eladás véglegesítése megerősítés után közös tranzakcióban hozza létre a helyi bizonylatot és csökkenti a készletet. Nincs elegendő készlet esetén sem részleges mozgás, sem félkész bizonylat nem marad.
5. A szám cégenként, évenként növekvő, például `EL-2026-000001`. A beküldés azonosítót kap; bizonytalan válasz után ugyanannak a kérésnek az ismétlése nem készít új eladást.
6. A listában a legutóbbi 200 eladás látszik; számra/vevőre kereshető. A Kijelölt eladás tételei gomb külön betölti a részleteket, a listaválasz nem tartalmazza minden bizonylat teljes tételsorát.
7. A visszafordítás megőrzi az eredeti bizonylatot és időpontot/felhasználót naplóz, a mennyiséget ellenmozgással visszaadja. A visszafordítás ismétlése sem adja vissza másodszor a készletet.

Ez **helyi eladási bizonylat**, nem adóügyi számla. Pénzügyi kiegyenlítés, pénztár, nyomtatott számla és NAV-adatszolgáltatás még nincs. A partnerdosszié a végleges helyi eladásokat forgalomként mutatja; a visszafordított eladás nem növeli ezt az összeget, és a kimenő számlák számába nem találunk ki bizonylatot.

## Cégmentés és visszaállítás

A Beállítások oldalon a kiválasztott cég menthető `.pcibak` fájlba. A mentés verziózott leírót és SQLite-adatbázist tartalmaz. Az ellenőrzés SHA-256 egyezést és SQLite-integritásellenőrzést végez, és vissza is olvassa az elkészült csomagot. Létező mentést nem ír felül.

A visszaállítás új névvel és új cégazonosítóval történik. Meglevő céget nem ír felül. A partner-, termék-, készlet- és bizonylatazonosítók a cégen belül megmaradnak. A fájl ellenőrzése megelőzi az új cégjegyzékbe írást. Másik gépen is használható cégadatot ad, de a NAV-kapcsolat újrabeállítandó.

A cégmentés nem tartalmaz NAV-jelszót/kulcsot, licencet, modulkiadói kulcsot, telepítési azonosítót, naplót vagy felületi skint. Üzleti adatokat tartalmaz, nem titkosított archívum. Jelenlegi mentési korlát: 256 MiB. Ez nem teljes gépmentés.

Ha a felület nem indul, leállított program mellett a karbantartó eszköz használható:

```powershell
.tools\dotnet\dotnet.exe tools\PcImpressz.Maintenance\bin\Release\net10.0\PcImpressz.Maintenance.dll backup-all .local F:\progs\PcImpressz.Backups
.tools\dotnet\dotnet.exe tools\PcImpressz.Maintenance\bin\Release\net10.0\PcImpressz.Maintenance.dll restore .local F:\mentes\ceg.pcibak "Visszaállított cég"
```

## Tárolási és fejlesztési határok

A SQLite `documents` táblája a korábbi törzsállapotot és a bevételezési állapotot verziózott dokumentumként tárolja; az új készletmozgás, eladás, műveletazonosító és számláló külön adatbázissor. Ez tranzakciós dokumentumtároló, **nem teljesen normalizált relációs végmodell**. A régi törzsállapotok rekordokra bontása és az SQL-szintű nagy adatmennyiségű lekérdezések következő, mérhető teljesítményhez kötött migrációk. A modulok a Host tranzakciós szerződését és típusos lekérdezési adapterét használják; az adapterben elkülönítve maradt az eredeti snapshot-séma ismerete.

Az egyenleg most pontos decimal összeadással a mozgásokból vezetődik le. Nagy terhelésre még nincs méretezési bizonyíték. Több gépes közös adatbázis, foglalások, leltárkampány, raktárközi átvezetés, beszerzési ár/értékelés, kedvezmények, áruvisszavételi bizonylatok és bevételezési helyesbítés további üzleti lépések. A mostani verzió negatív készletet nem enged és egységet/devizát nem vált át.

## Ellenőrzés

`dev.ps1 -Action Test`: 142/142 üzleti/Core/Host teszt, `wheel-ok`, 6/6 WPF render. A valódi Host-teszt aláírt modulcsomagokat, cégelkülönítést, licenclejáratot, kikapcsolt függőséget, bevételezést, eladást, ismétlést, visszafordítást, mentést/visszaállítást és a készletoszlopot is ellenőrzi. A WPF-próba képi kimenete az `artifacts/ui-checks` könyvtárban van. Ezek mellett a saját munkafolyamat kézi felhasználói kipróbálása még külön átvételi lépés.
