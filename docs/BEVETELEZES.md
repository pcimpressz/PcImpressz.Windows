# Bevételezés – 0.3

**v0.4 frissítés:** a lenti v0.3 leírás a korábbi működés dokumentuma. A Host most cégenként SQLite-adatbázist használ; a régi JSON-fájlok migrációs források. A bevételezés már készletet mozgat, az eladás és a cégmentés elkészült. Az aktuális használat és korlátok: [Készlet, eladás és cégmentés](KESZLET-ELADAS-ES-MENTES.md).

A bevételezés bejövő számlát tart. A lista elején lehet választani, honnan jön a számla. Ami bekerül, új állapotba kerül, és a besorolás dönti el a sorsát: bevételezés, költség vagy függő. A lezárt számla nem írható felül.

## Használat

1. Indítás: a projekt gyökerében a Start.cmd. Ha van `.issuer` mappa, a fejlesztői telepítés a partnertörzs és a terméktörzs mellett a bevételezést is aláírja.
2. A Bevételezés menüben három indulás van: kézi számla, lekérés a NAV-tól, import PDF / Excel.
3. A szállító a partnertörzs aktív beszállítója. A listából választható, vagy az Új szállító gombbal ugyaninnen felvehető.
4. A tétel a terméktörzsből kereshető. Ha nincs bent, az Új termék gomb Darab mértékegységgel felveszi, és a sorhoz köti.
5. A szállítási díj és a hasonló sor szolgáltatás: termék nélkül is lezárható. Készletes sor bevételezés előtt párosítandó, vagy új termékként felveendő.
6. A Mentés után a számla besorolható. A költség és a függő nem kíván termékpárost. A függő később még bevételezhető vagy költségként lezárható.

## NAV és import

A NAV-indítás a Beállításokban mentett Online Számla kapcsolattal kéri le a bejövő számlákat. Az alap a legutóbbi 30 nap, legfeljebb egy év, és a lista húsz oldalnál megáll. A beérkezett számla új állapotba kerül; a besorolás dönti el, hogy bevételezés, költség vagy függő. Már ismert számlaszám nem íródik felül. Ha a szállító nincs a partnertörzsben, a számla kimarad, és a program felsorolja: vedd fel a szállítót, majd kérd le újra. A letöltött NAV-számla XML továbbra is megnyitható fájlból. A kapcsolat cégenként külön van.

Ha a szállító egyedi mezője `xml-szamla`, a program felismeri az XML-kapcsolatot. `xml-szamla=https://...` esetén megpróbálja lekérni a számlát, és csak a hiányzó cikkszámot vagy vonalkódot pótolja. `xml-szamla=igen` azt jelzi, hogy a kapcsolat később lesz bekötve; a NAV-számla ettől még bekerül.

A megnyitott, még nem lezárt számlán a hiányzó adat külön XML-ből vagy PDF-ből pótolható. A PDF-ből csak a beágyazott számla-XML olvasható; a képi tételsorok olvasása nincs kész. Az e-mailes számlafogadás későbbi kör.

A Beállítások oldalon adható meg az adószám, a technikai felhasználó, a jelszó, az aláírókulcs, a cserekulcs és a teszt mód. A jelszó és a két kulcs a Windows-felhasználóhoz kötött titkosítással kerül a cég `modules/receiving/nav-connection.bin` fájljába. A fájl nem tartalmazza olvashatóan a titkot, másik felhasználó vagy másik gép nem nyitja meg. A képernyő a mentés után nem írja vissza a jelszót és a kulcsokat. Üresen hagyva a már mentett titok marad. A demó cég adatainak törlése ezt a fájlt is törli. Excel (xlsx) és CSV olvasható: termék neve, mennyiség, nettó egységár és áfa. A cikkszám és a vonalkód a párosítást segíti.

Az import és a NAV-számla is új állapotba kerül. A besorolás ugyanaz, mint a kézi számlánál.

## Tárolás

Egy cég számlái: `.local/companies/<cégazonosító>/modules/receiving/receipts.json`. Az előző változat `receipts.json.bak`. A szállító és a termék nem másolódik ide törzsként: a számla a partnertörzs és a terméktörzs azonosítójára hivatkozik.

A bevételezett számla megjelenik a szállító dossziéjában. A beszerzés összege csak a bevételezett számlákat számolja. Készletet ez a modul még nem mozgat.

Korlát: 20 000 számla, számlánként 400 tétel, 200 000 naplóbejegyzés, 64 MB.
