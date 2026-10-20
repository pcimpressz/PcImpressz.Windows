# Bevételezés – 0.3

A bevételezés bejövő számlát tart. A lista elején lehet választani, honnan jön a számla. Ami bekerül, új állapotba kerül, és a besorolás dönti el a sorsát: bevételezés, költség vagy függő. A lezárt számla nem írható felül.

## Használat

1. Indítás: a projekt gyökerében a Start.cmd. Ha van `.issuer` mappa, a fejlesztői telepítés a partnertörzs és a terméktörzs mellett a bevételezést is aláírja.
2. A Bevételezés menüben három indulás van: kézi számla, lekérés a NAV-tól, import PDF / Excel.
3. A szállító a partnertörzs aktív beszállítója. A listából választható, vagy az Új szállító gombbal ugyaninnen felvehető.
4. A tétel a terméktörzsből kereshető. Ha nincs bent, az Új termék gomb Darab mértékegységgel felveszi, és a sorhoz köti.
5. A szállítási díj és a hasonló sor szolgáltatás: termék nélkül is lezárható. Készletes sor bevételezés előtt párosítandó, vagy új termékként felveendő.
6. A Mentés után a számla besorolható. A költség és a függő nem kíván termékpárost. A függő később még bevételezhető vagy költségként lezárható.

## NAV és import

A NAV-gomb ebben a körben a már letöltött NAV-számla XML-t olvassa. Az élő Online Számla lekérdezés a Beállításokban mentett technikai felhasználót használja majd. A kapcsolat cégenként külön van.

A Beállítások oldalon adható meg az adószám, a technikai felhasználó, a jelszó, az aláírókulcs, a cserekulcs és a teszt mód. A jelszó és a két kulcs a Windows-felhasználóhoz kötött titkosítással kerül a cég `modules/receiving/nav-connection.bin` fájljába. A fájl nem tartalmazza olvashatóan a titkot, másik felhasználó vagy másik gép nem nyitja meg. A képernyő a mentés után nem írja vissza a jelszót és a kulcsokat. Üresen hagyva a már mentett titok marad. A demó cég adatainak törlése ezt a fájlt is törli. A PDF tételsorainak kiolvasása szintén a következő kör. Most Excel (xlsx) és CSV olvasható: termék neve, mennyiség, nettó egységár. A cikkszám és a vonalkód a párosítást segíti.

Az import és a NAV-számla is új állapotba kerül. A besorolás ugyanaz, mint a kézi számlánál.

## Tárolás

Egy cég számlái: `.local/companies/<cégazonosító>/modules/receiving/receipts.json`. Az előző változat `receipts.json.bak`. A szállító és a termék nem másolódik ide törzsként: a számla a partnertörzs és a terméktörzs azonosítójára hivatkozik.

A bevételezett számla megjelenik a szállító dossziéjában. A beszerzés összege csak a bevételezett számlákat számolja. Készletet ez a modul még nem mozgat.

Korlát: 20 000 számla, számlánként 400 tétel, 200 000 naplóbejegyzés, 64 MB.
