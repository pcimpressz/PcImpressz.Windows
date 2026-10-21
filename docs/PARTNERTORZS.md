# Partnertörzs és üres cégek – 0.3

**v0.4 frissítés:** a lenti v0.3 leírás a korábbi működés dokumentuma. A Host most cégenként SQLite-adatbázist használ; a régi JSON-fájlok migrációs források. A bevételezés már készletet mozgat, az eladás és a cégmentés elkészült. Az aktuális használat és korlátok: [Készlet, eladás és cégmentés](KESZLET-ELADAS-ES-MENTES.md).

## Használat

1. Indítás: a projekt gyökerében a Start.cmd.
2. Az oldalsáv AKTÍV CÉG listájában választhatsz céget.
3. Az Üres cég létrehozása gomb saját vagy demócéget készít, partner- és mintaadatok nélkül.
4. A Partnertörzs menü a cégeket mutatja, alatta a Kapcsolattartók és az Idővonal. Mentett cégnél a Dosszié gomb a lista helyén nyitja a dossziét. A Vissza a partnerekhez gomb hozza vissza a listát.
5. A CSV-export az aktuális keresésnek és szerepszűrőnek megfelelő partnereket menti.
6. Az archivált partnereket az Archiváltak szűrővel éred el; az Aktív mező visszakapcsolásával helyreállíthatók.

A partner lehet egyszerre vevő, szállító és gyártó. Egy aktív, azonos adószámú partner újbóli felvételét a program megakadályozza. Két ablak közül a régebbi adatlap nem írhatja felül észrevétlenül a frissebb módosítást.

## Cég, kapcsolattartó, idővonal

A cégadatlap a szervezet hivatalos adata: adószám, EU adószám, cégjegyzékszám, bankszámlák, székhely, számlázási és szállítási cím, telephelyek, iparág, ügyfél/beszállító/gyártó/érdeklődő/partner/konkurens, létszám, árbevétel-kategória, A/B/C besorolás, web, LinkedIn, központi elérhetőség, életciklus, felelős, adatforrás, hitelkeret, anyacég, címkék és `név=érték` egyedi mezők. Az anyacég nevét pontosan kell megadni; a lánc nem érhet körbe.

A kapcsolattartó külön lap. A cégek nevét soronként kell írni, így egy személy több céghez tartozhat. Az idővonal kézi előzmény: jegyzet, e-mail, hívás, megbeszélés, rendszeresemény, dokumentum. Ha a dátum üres, a mai nap kerül be. A dosszié ezeket a cég alatt mutatja, és jelzi, ha 60 napja nem volt kézi előzmény.

Az azonos aktív adószám és EU adószám mentése hibát ad. Az azonos e-mail, a hasonló név és az azonos cégjegyzékszám figyelmeztetés a mentés után, az adat így is megmarad. Az Elfeledtetés mentéskor jelölő a cég vagy a kapcsolattartó személyes adatait törli, az azonosítót megtartja, hogy a régi számla hivatkozása megmaradjon. Ha a kapcsolattartó más céghez is tartozik, csak a kapcsolat szakad meg.

Az eladó szerepkör a felelős nélküli és a saját felelősű cégeket látja. Az adminisztrátor, a raktáros és a megtekintő minden céget lát. Új cégnél az eladó automatikusan felelős lesz, ha a mező üres.

Még nincs bekötve: OPTEN vagy cégjegyzék kitöltés, levél- és naptárszinkron, hívásfelvétel. Ezekhez külső szolgáltatás kell. A forgalom, a nyitott számla és a beszerzés továbbra is a dossziéban jelenik meg, a meglévő eladás- és bevételezésadatokból.

## Dosszié

A listában kiválasztott, már mentett partner adatlapján a Dosszié gomb a partnertörzs oldalán, a lista helyén mutatja a dossziét. Felül az időszak szűrése van: kezdő nap, záró nap, Szűrés, illetve Mind, Idén, Tavaly és 3 hónap. A bejövő számla sorára kattintva lenyílnak a számlán lévő tételek. A fejléc a partner nevét, szerepét, adószámát és elérhetőségét mutatja, alatta a forgalom, a kimenő számla, a beszerzés, a bejövő számla, az árajánlat, a díjbekérő, a megrendelés és a munkalap helye.

A bejövő számla és a beszerzés a bevételezésből jön, ha ennél a szállítónál van számla. A beszerzés összege csak a bevételezett számlákat számolja. Kimenő forgalom, árajánlat, díjbekérő, megrendelés és munkalap továbbra is üres, amíg a hozzájuk tartozó modul nincs meg. A kötőjel azt jelenti, hogy az a forrás még nem tárol adatot.

A régi `szamlazas.py` partnerdossziéja (`/api/ugyfel/<id>/torzs`) ezekből állt össze:

| Dossziéhely | Régi forrás | Következő Windows-modul |
| --- | --- | --- |
| Kimenő számlák és forgalom | `eladas`, `kimeno_szamla`, `kimeno_tetel` | Eladás és kimenő számla |
| Bejövő számlák és beszerzés | `szamla`, `szamla_tetel` | Bevételezés |
| Árajánlatok | `arajanlat` | Árajánlat |
| Díjbekérők | `bizonylat_dokumentum`, típus `dijbekero` | Bizonylat |
| Megrendelés, munkalap, garancia, teendő | rendeléskönyv `/api/gep/partner-kapcsolodo` | Rendeléskönyv |

A párosítás a régiben adószám, azon belül az első nyolc számjegy, adószám nélkül a név. A Windows-dosszié a bevételezésben tárolt szállító-azonosítót használja. A beszállítói ranglista későbbi összesítés.

A `dossier` parancs `partners.read` jogosultságot kér, lejárt licenc mellett is olvasható. Bemenete a cég és a partner azonosítója. Kimenete a fejléc, a nyolc összesítő hely és a hat hiányzó forrás leírása. Új partnernél, mentés előtt a gomb nem nyílik meg. Ha a partner nem található, a parancs hibát ad, üres dossziét nem gyárt.

Ha a cég bevételezésében van számla ehhez a szállítóhoz, a dosszié onnan tölti a listát. A `dossiers.json` csak akkor számít, ha a bevételezésben ennél a partnernél nincs számla. A fájl helyi adat, nem kerül a forráskezelőbe.

A következő fejlesztési lépés a kimenő számla ugyanezzel a partnerkapoccsal, a forgalom kártyához. Amíg az a modul nincs, a szakasz állapota `waiting` marad.

## Korábbi külső kapcsolat

Az InvenTree-kapcsolat 2026-10-21-én kivezetve. Nincs importgomb, API-kliens vagy tokenkezelés. A meglévő rekordok eredetadatai megmaradnak.

## Adattárolás és mentés

A cégjegyzék: .local/companies.json. Egy cég partneradatai: .local/companies/<cégazonosító>/modules/partners/partners.json.

Ez az első változat egygépes, egy Windows-felhasználóhoz tartozó adattárat használ, nincs szüksége külön adatbázis-szerverre. Az egyes cégek külön fájlokban vannak. A mentés atomi fájlcserével és módosítási naplóval történik; az előző változat partners.json.bak néven megmarad. Sérült főfájlt nem cserélünk le csendben üres adatokra.

Teljes mentéshez állítsd le az alkalmazást, és másold a teljes .local könyvtárat biztonságos helyre. Az előző mentésből történő kézi helyreállítást szintén leállított alkalmazással kell végezni, a sérült eredeti megőrzése mellett.

Jelenlegi keretek: legfeljebb 50 000 partner és 200 000 módosítási naplóbejegyzés cégenként; legfeljebb 64 MB partnerfájl;  Több Windows-felhasználó közös hálózati adatkezelése és finom cégenkénti szerepkörök későbbi fejlesztés.

## Modul, jogosultság és fejlesztői licenc

A partnertörzs külön szerelvény és aláírt modulcsomag. A Shell a modul által közölt mező- és oszlopleírásból készíti az adatlapot; nem hivatkozik a partnerüzleti kódra. Ez az adatleírás a későbbi elrendezés- és szövegszerkesztő egyik alapja. Drag & drop szerkesztőmód ebben a kiadásban még nincs.

Minden írás és import a központi parancsvégrehajtón, felhasználói jogosultság és licenc ellenőrzésével fut. Az olvasás és az alapexport lejárat után is elérhető a jogosult helyi felhasználónak. A kérés nem adhat meg saját felhasználói identitást vagy jogosultságlistát.

A fejlesztői Setup művelet külön modul- és licencaláíró kulcsot hoz létre a Windows-felhasználóhoz korlátozott .issuer mappában. Aláírja a csomagot, és 30 napos fejlesztői jogosultságot, illetve 7 napos működési engedélyt ad. A fejlesztői Start/Run ezt megújítja, ha a saját .issuer mappa jelen van. Ez nem az ügyfelek előfizetési szervere.

A .issuer könyvtár privát kiadói kulcsokat tartalmaz: nem adható át ügyfélnek, nem kerülhet forráskezelőbe vagy ügyféltelepítőbe. Ügyfélkiadásként a Publish által készített artifacts/desktop könyvtár használható, külön kiadott aláírt modulokkal, nyilvános kulcsokkal és érvényes licenccel. A kiadás nem tartalmaz tesztmodult vagy a fejlesztői kiadóeszközt.
