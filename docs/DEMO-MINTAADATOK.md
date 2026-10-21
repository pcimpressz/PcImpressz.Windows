# Demócég feltöltése mintaadatokkal – 0.4.6

## Használat

Válaszd ki a demócéget, jelentkezz be annak adminjaként, majd a **Beállítások → Demócég és mintaadatok → Feltöltés mintaadatokkal** gombot használd. Saját, nem demóként jelölt cégben a feltöltő nem jelenik meg; raktáros, eladó és megtekintő nem indíthatja. Előbb mentsd vagy vesd el a nyitott adatlapok módosításait. Mind az öt üzleti modul legyen aktív, bekapcsolva és írásra jogosult licenccel elérhető.

A feltöltés előzetes, ellenőrzött cégmentést készít a helyi `backups/demo-seed` mappába. A sikerüzenetben látható a mentés elérési útja. Ha a mentés nem sikerül, a feltöltés el sem indul. A meglévő adatok megmaradnak. A mintacsomag összes új adata és a befejezési jelző egyetlen SQLite-tranzakcióban mentődik: közbenső hiba esetén minden új mintaadat visszagörgetődik.

Ismételt kattintás nem hoz létre második csomagot, és nem írja felül a tesztelés közben átírt rekordokat. Korábbi, v1 mintacsomag megléte szintén ismételt feltöltésnek számít. Ha a DEMO- cikkszám/kategórianévtér már foglalt, de nincs befejezési jelző, a program elutasítja a feltöltést a meglévő adatok felülírása helyett. A demócég adatainak külön, megerősítést kérő törlése után ismét feltölthető a csomag. A törlés a kiválasztott demócég teljes üzleti tartalmát érinti, ezért feltöltéshez nincs rá szükség.

## A csomag tartalma

- 20 aktív, beszerezhető és eladható termék: perifériák, számítógép-alkatrészek, hálózati és irodai kiegészítők.
- Saját cikkszám: DEMO-001–DEMO-020; külön gyártói cikkszám: DEMO-MPN-001–DEMO-MPN-020; tesztvonalkód: DEMO-BC-001–DEMO-BC-020. A vonalkódok mesterséges tesztazonosítók, nem valódi EAN-számok.
- 5 szállító és 5 vevő, kitalált nevekkel és example.test elérhetőségekkel. A szállítók közül kettő gyártóként is megjelölt. Meglévő partnerek ezen felül megmaradnak.
- 4 kategória, darab és méter mértékegység. A szükséges mértékegységet meglévő aktív egység esetén újra felhasználjuk.
- Bolt és Központi raktár készlete, nyitómozgásokkal; alacsony készletű DEMO-019 és nulla készletű DEMO-020 termék. A méterre értékesített UTP-kábelen tört mennyiség is kipróbálható.
- 8 bevételezési bizonylat: 4 készletre könyvelt, 2 függő/párosításra váró, 1 költség és 1 új bizonylat.
- 10 eladás, közülük 2 visszafordítva; készletmozgások és a naplóban azonosítható feltöltő. Mintabizonylatok 27%-os és 5%-os áfával is szerepelnek.

Minden rekord egyértelműen DEMO jelölésű. Az árak mesterséges tesztárak; a termékparaméterek és a mintabizonylatok tartalmazzák őket. Ezek nem valódi számlák, partnerkapcsolatok vagy termékajánlatok, és semmilyen rendelés vagy NAV-beküldés nem történik.

## Javasolt próba

1. A Terméktörzsben keresd meg a DEMO-001 terméket, majd próbálj gyártói cikkszám és tesztvonalkód alapján is keresni.
2. Nézd meg a termék készletét és mozgásnaplóját, majd a DEMO-019 és DEMO-020 terméknél az alacsony/nulla készletet.
3. Bevételezésnél nyisd meg az új DEMO-BEV-008 bizonylatot, vagy a függő DEMO-BEV-005/006 tételpárosítását.
4. Eladóként rögzíts új eladást egy készleten lévő termékkel és mintavevővel; ellenőrizd a kosarat, készletcsökkenést és szükség esetén a visszafordítást. Raktárosként az eladási művelet nem érhető el.
5. Végül készíts adminisztrátorként cégmentést, és az új cégként történő visszaállítást is próbáld ki.

## Ellenőrzés és helyi feltöltés

162/162 automatizált Core/üzleti/Host teszt és 33/33 WPF-render sikeres. Célzott ellenőrzés: csomagméret és keresési azonosítók; alacsony/nulla és tört készlet; több bizonylatállapot; ismételt feltöltés változatlan adatokkal; közbenső eladási hiba teljes visszagörgetése; admin- és demócég-védelem; közvetlen parancsmegkerülés tiltása; hiányzó modul/licenc és mentéshiba; feltöltés előtti mentés tényleges visszaállítása; gomb megjelenése és tiltása mindhárom témában.

2026-10-21-én a helyi Demó cégbe ténylegesen bekerült a csomag: 20 új termék, 10 új partner, 8 bevételezés és 10 eladás. A korábbi partner megmaradt; az adminfiókok és jelszavak nem változtak. Előzetes ellenőrzött mentés készült. A programot a Start.cmd segítségével újraindítva érhető el az új gomb.
