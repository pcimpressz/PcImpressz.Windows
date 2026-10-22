# 0.4.16 – kimenő számlák fejlesztési alapja

Kiindulás: GitHub main 0.4.15, `6bf69e3`. Átvett csomag: `pcimpressz-0.4.16-invoicing-dev.zip`, SHA-256 `FD683174E112FB254D0CB1D38BF244EF666A0DB827066D0B1106D05A607F3B14`.

## Működő részek

- A Számlák → Kimenő számlák elsődleges forrása a saját céges SQLite-adatbázis. Keresés, dátum, fizetési mód, NAV-állapot, lapozás és pénznemenként külön nettó/áfa/bruttó összesítés.
- A NAV-egyeztetés külön kérés, legfeljebb 35 napra. Nem állít ki, nem importál kimenő számlát, nem ír át összegeket és nem hoz létre készletmozgást. A hiányos, eltérő pénznemű, több műveletből álló vagy módosító találat nem kap automatikusan egyező állapotot.
- A NAV-válasz letöltése után a helyi egyeztetési állapot mentését a Host újraellenőrzött munkamenettel, jogosultsággal és licenccel indítja. A belső mentőparancs közvetlenül nem hívható.
- Beállítások → Számlázás: admin által, cégenként menthető szolgáltatóválasztás, legfeljebb 20 számlatömb, név, előtag, tervezett kezdősorszám és alapértelmezett tömb. Választható Bizonylat.hu; a Számlázz.hu és Billingo tervezett integrációként szerepel.
- A Bizonylat-kulcs egy helyen, céghez és Windows-felhasználóhoz kötött DPAPI-tárolóban marad. Nem jelenik meg újra és nem kerül a normál beállításrekordba.

## Fejlesztési adapter és következő lépés

A számlakiállító adapter szintetikus HTTP-válaszokkal tesztelt fejlesztési alap. A normál alkalmazási útvonalon a valódi kiállítás még tiltott; a pénztári Számla választás egyelőre a korábbi számlaigényt rögzíti. Nem állítjuk, hogy a csomag kész, éles számlázóintegráció.

A következő fázis végleges folyamata: eladás → kiválasztott szolgáltató API-ja → valódi szolgáltatói számlaszám és teljes számla mentése → megnyitás/PDF → későbbi NAV-egyeztetés. Az adapter akkor aktiválható, ha a teljes, fiókban elérhető API-referencia és egy ellenőrzött tesztcéges próba igazolta a mezőket, áfakategóriákat, végösszegeket, idempotencia és bizonytalan eredmény helyreállításának szabályait. Az ismeretlen eredmény kézi/szolgáltatói egyeztetése még külön munkafolyamatot igényel.

A tesztelt adapter a HTTP-kérés előtt tartós műveletjelölőt ment. Sikernél a meglévő számlát adja vissza; bizonytalan eredménynél új hívást nem indít. Az eredmény hiányzó vagy eltérő összesítőjét nem fogadja el. Még nem küld e-mailt. A szabályok az aktiválás előtt is megmaradnak.

A Bizonylat.hu szerint ugyanaz az API-végpont szolgálja ki az éles és a tesztcéget; a kulcs a szolgáltatónál kiválasztott céghez tartozik. A helyi tesztjelölés önmagában nem biztonsági garancia és nem alakítja át az éles kulcsot tesztkulccsá.

A tervezett kezdősorszám elmenthető, de sem a PC-Impressz, sem a fejlesztési adapter nem gyárt tényleges számlaszámot. A szolgáltatói sorszámozás érvényesítését integrációnként kell igazolni. A jelenlegi adapter nem küld kitalált kezdősorszám-mezőt; az 1-től eltérő kezdés aktiválása külön ellenőrzésig nem engedélyezett. Kiállított számlák mellett a régi tömb előtagja/kezdete már nem változtatható meg.

## Csomagellenőrzés és javítások

Az átadott apply script hibás PowerShell-idézőjelezését és a Windows/sortörés-eltéréseket az elkülönített tesztpéldányon javítottuk. A script eredeti mentése csak a felülmásolt öt fájlra terjedt ki; a végleges átvételnél minden változó meglévő forrásfájlról külön mentés készült. Az eredeti ZIP és céges adatok nem módosultak.

További javítások: modulindítás kompatibilitása; adminbeállítások védelme; hiányzó végösszegek elutasítása; szolgáltatói hibaüzenetek nem tükröznek vissza kulcsot; HTTP-átirányítás tiltása, válaszméret-korlát; helyi tartós ismétlésvédelem; kiállított adatok megváltoztatásának tiltása; pénznemenkénti összesítés; NAV-egyeztetés hiányos/eltérő találatainak kezelése; cégváltási és felületi állapotvédelem.

A csomag által említett `cursor/mukodesi-beallitasok-7ad2` teljes, más modulokat is érintő beállításágát nem egyesítettük. A számlázási beállításokhoz újabb kulcstároló helyett az itt megvalósított egyetlen DPAPI-tárolót kell használni. A többi működési beállítás külön összehangolási feladat maradt.

Ellenőrzés: 187/187 üzleti/Core/Host teszt, 66/66 WPF render, hibamentes Release fordítás. DPAPI cégkötés, szolgáltató- és tömbvalidálás, saját nyilvántartás, pénznemek, NAV-egyeztetés, visszatérő siker és hálózati bizonytalanság utáni újraküldés tiltása külön tesztelve. A kulcsok és API-válaszok mesterségesek. Élő számla, e-mail és éles NAV-kérés nem készült.

Hivatalos szolgáltatói tájékoztató: [Bizonylat API](https://bizonylat.hu/szamlazo-api), [webshop-integráció](https://bizonylat.hu/webshop-szamlazas-integracio).
