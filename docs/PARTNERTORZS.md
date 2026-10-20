# Partnertörzs és üres cégek – 0.3

## Használat

1. Indítás: a projekt gyökerében a Start.cmd.
2. Az oldalsáv AKTÍV CÉG listájában választhatsz céget.
3. Az Üres cég létrehozása gomb saját vagy demócéget készít, partner- és mintaadatok nélkül.
4. A Partnertörzs menüben kereshetsz, új partnert vihetsz fel, módosíthatsz és archiválhatsz.
5. A CSV-export az aktuális keresésnek és szerepszűrőnek megfelelő partnereket menti.
6. Az archivált partnereket az Archiváltak szűrővel éred el; az Aktív mező visszakapcsolásával helyreállíthatók.

A partner lehet egyszerre vevő, szállító és gyártó. Egy aktív, azonos adószámú partner újbóli felvételét a program megakadályozza. Két ablak közül a régebbi adatlap nem írhatja felül észrevétlenül a frissebb módosítást.

## InvenTree-import

Az Import InvenTree-ből ablak alapértelmezett címe https://raktar.bukacsalad.hu. A helyi címhez http://192.168.110.71 adható meg; HTTP esetén külön be kell kapcsolni a nem titkosított helyi kapcsolatot.

Az API-kulcsot kizárólag a program importablakában add meg. A kulcsot nem mentjük konfigurációba, partneradatba vagy naplóba. A lekérések GET műveletek; az InvenTree adatait nem módosítjuk. A szerver TLS-ellenőrzését nem kapcsoljuk ki, és idegen címre mutató átirányítást/lapozást nem követünk a kulccsal.

Az előnézet megmutatja az új, már meglévőnek felismert és hibás rekordok számát, valamint néhány mintaadatot. Helyi adat csak az Új partnerek importálása gomb után keletkezik. A már meglévő partnereket nem írjuk felül: külső azonosító, adószám, illetve azonos név/e-mail alapján felismerjük az egyezést. A forrást érdemes mindig ugyanazzal a címmel megadni.

A partner alapadatai, szerepei és elsődleges címe kerülnek átvételre; külön kapcsolattartói és többcímes nyilvántartás ebben a változatban nincs. Az előnézet 10 percig, az adott céghez és Windows-felhasználóhoz kötve használható fel egyszer. Ha közben a helyi partneradatok változnak, új előnézet kell. A felületen megjelenő hibás/kimaradt rekordokat ellenőrizni kell.

A valódi szerverrel való importot API-kulcs hiányában még nem hajtottuk végre; a lekérési, lapozási, előnézeti és mentési folyamatokat tesztválaszokkal ellenőriztük. [InvenTree vállalati API](https://docs.inventree.org/en/latest/api/schema/company/)

## Adattárolás és mentés

A cégjegyzék: .local/companies.json. Egy cég partneradatai: .local/companies/<cégazonosító>/modules/partners/partners.json.

Ez az első változat egygépes, egy Windows-felhasználóhoz tartozó adattárat használ, nincs szüksége külön adatbázis-szerverre. Az egyes cégek külön fájlokban vannak. A mentés atomi fájlcserével és módosítási naplóval történik; az előző változat partners.json.bak néven megmarad. Sérült főfájlt nem cserélünk le csendben üres adatokra.

Teljes mentéshez állítsd le az alkalmazást, és másold a teljes .local könyvtárat biztonságos helyre. Az előző mentésből történő kézi helyreállítást szintén leállított alkalmazással kell végezni, a sérült eredeti megőrzése mellett.

Jelenlegi keretek: legfeljebb 50 000 partner és 200 000 módosítási naplóbejegyzés cégenként; legfeljebb 64 MB partnerfájl; importálásnál legfeljebb 5000 InvenTree-partner és 10 000 cím egy előnézetben. Több Windows-felhasználó közös hálózati adatkezelése és finom cégenkénti szerepkörök későbbi fejlesztés.

## Modul, jogosultság és fejlesztői licenc

A partnertörzs külön szerelvény és aláírt modulcsomag. A Shell a modul által közölt mező- és oszlopleírásból készíti az adatlapot; nem hivatkozik a partnerüzleti kódra. Ez az adatleírás a későbbi elrendezés- és szövegszerkesztő egyik alapja. Drag & drop szerkesztőmód ebben a kiadásban még nincs.

Minden írás és import a központi parancsvégrehajtón, felhasználói jogosultság és licenc ellenőrzésével fut. Az olvasás és az alapexport lejárat után is elérhető a jogosult helyi felhasználónak. A kérés nem adhat meg saját felhasználói identitást vagy jogosultságlistát.

A fejlesztői Setup művelet külön modul- és licencaláíró kulcsot hoz létre a Windows-felhasználóhoz korlátozott .issuer mappában. Aláírja a csomagot, és 30 napos fejlesztői jogosultságot, illetve 7 napos működési engedélyt ad. A fejlesztői Start/Run ezt megújítja, ha a saját .issuer mappa jelen van. Ez nem az ügyfelek előfizetési szervere.

A .issuer könyvtár privát kiadói kulcsokat tartalmaz: nem adható át ügyfélnek, nem kerülhet forráskezelőbe vagy ügyféltelepítőbe. Ügyfélkiadásként a Publish által készített artifacts/desktop könyvtár használható, külön kiadott aláírt modulokkal, nyilvános kulcsokkal és érvényes licenccel. A kiadás nem tartalmaz tesztmodult vagy a fejlesztői kiadóeszközt.
