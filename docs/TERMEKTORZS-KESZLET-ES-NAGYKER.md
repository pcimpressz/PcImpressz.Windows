# Terméktörzs, készlet és a nagykeres beszerzés iránya – 0.4.1

## Ami most használható

A Terméktörzs egy munkaterületen két belső fület ad: **Terméktörzs**, valamint **Készlet és mozgások**. A külön felső szintű Készlet menüpont megszűnik, amikor a terméktörzs elérhető. Ha a catalog modult kikapcsoljuk, az önálló készletnézet továbbra is elérhető marad. A két üzleti modul jogosultság- és licencellenőrzése változatlan.

Jelölj ki egy mentett terméket, majd válaszd a **Kijelölt termék készlete és mozgásai** gombot. A raktári egyenleg, mozgásnapló, termékválasztó és készlet-CSV erre a termékre szűkül. A **Minden termék készlete** gomb visszaállítja az összes termék nézetét. Mentetlen készletművelet vagy aktív kérés közben a nézetváltás nem dobja el a szerkesztett adatot.

A készlet saját, fizikailag nyilvántartott raktári mennyiséget jelent. A nyitókészlet a tényleges induló mennyiség, a korrekció az indokolt eltérés javítása. A bevételezés és az eladás a saját bizonylatán keresztül változtat készletet; ezeket nem szabad ugyanarra az eseményre kézi korrekcióval ismét lekönyvelni. Az előzmény megmarad, hibát ellenmozgással javítunk.

## Termékazonosítók és keresés

A saját cikkszám, a **gyártói cikkszám** és a vonalkód külön mező. A gyártói cikkszám legfeljebb 100 karakter, megőrzi a gyártó formátumát; nem kényszerítjük a saját SKU alakjára. A régebbi termékeknél üres, nem szükséges adatvesztő migráció. Az oszlop rendezhető és a termék-CSV-ben is szerepel.

A kereső a szavakat külön kezeli, mindegyiket megköveteli, és nem érzékeny a kis/nagybetűre vagy a magyar ékezetekre. Példák:

- `eger vezetek` – a két szó eltérő sorrendben vagy mezőben is szerepelhet.
- `nev:"vezeték nélküli"` – összefüggő kifejezés a névben.
- `sku:=MOUSE-1` – a teljes saját cikkszám pontos egyezése.
- `gyartoi:=ABC-123` vagy `mpn:=ABC-123` – a teljes gyártói cikkszám.
- `vonalkod:599` vagy `ean:599` – részlet a vonalkódban.
- `kategoria:eger gyartoi:ABC` – kombinált feltételek.

Alapértelmezett sorrendben a pontos azonosítóegyezés előre kerül; oszloprendezéskor a választott rendezés érvényesül. Azonos értékeknél a rekordazonosító biztosít stabil sorrendet. A **Van saját készleten** és **Nincs saját készleten** szűrők a saját, minden raktárra összesített készletet vizsgálják; a készletmozgással még nem rendelkező termékek is megjelennek az utóbbiban.

A lista alatt látszik az utolsó sikeresen megjelenített keresés és szűrő. Az export ezt használja akkor is, ha a keresőbe már új szöveget írtunk. A CSV-célfájl csak a teljes sikeres írás után cserélődik le. Az export adatai a lekéréskori aktuális adatok, nem a régi képernyő adatpillanatképe.

## Következő lépés: nagykerajánlatok és kosarak

Ez még tervezett funkció, nincs élő nagykerlekérdezés és rendelésküldés. A mostani gyártói cikkszám a későbbi összerendelés alapja.

Egy saját termékhez több beszállítói ajánlat tartozzon. Az ajánlat őrizze a beszállító partnerazonosítóját, a nagyker saját cikkszámát, a gyártói cikkszámot, a vonalkódot, az árat/devizát, a mennyiséget vagy elérhetőségi állapotot és a frissítés időpontját. Az ismeretlen vagy régi adat nem jelent biztos készletet. A saját készletet ezek az adatok nem módosítják.

Automatikus összerendelésnél a vonalkód, a gyártói cikkszám és a beszállítói megfeleltetés erős azonosító; a név csak ellenőrizhető javaslat. Hasonló név önmagában nem bizonyít azonos terméket. Eltérő gyártók azonos cikkszáma vagy eltérő kiszerelés külön felülvizsgálatot kíván.

A termék adatlapján nagykerenként jelenjen meg az ajánlat, az ismert készlet és az adat kora. A felhasználó kiválasztja az ajánlatot, megad például 2 darabot, és kosárba teszi. A piszkozatkosár cégenként és nagykerenként külön tárolódjon SQL-ben. Egy sor őrizze a saját termék és a beszállítói ajánlat azonosítóját, a mennyiséget és az ajánlat árpillanatképét. Ez nem foglal saját készletet és nem küld rendelést.

Véglegesítés előtt ár- és elérhetőség-ellenőrzés, minimumrendelés/kiszerelés és szállítási díj kezelése kell. A kosárból beszerzési rendelés készül, a későbbi részbevételezés ennek egységárát veszi át. A rendelés tényleges nagykerhez továbbítása csak az adott kapcsolat támogatott, ellenőrzött módján történhet, ismétlésvédelemmel.

## Auditállapot

A08 alkalmazott keresési állapot javítva; késleltetett valódi IPC-válaszos UI-regresszió ellenőrzi. A09 stabil másodlagos rendezés a partner- és terméklistán javítva; a teljes oszlopsorrend/szélesség-mentés még hátravan. A07 célfájl véglegesítése javítva, de az IPC 8 MiB korlátjához szükséges darabolt nagy export még nyitott. A10 gyártói cikkszám és összetett termék–készlet munkaterület elkészült; további mezőtípusok később.

A partnertörzs már céges SQLite-tranzakcióban tárolódik, de továbbra is JSON-pillanatkép a documents táblában. A06 indexelt partnertáblák és külön audit még nincsenek készen. Az új termékkeresés jelenleg memóriában szűr; 50 000 termékes teljesítménycélt nem állítunk mérés nélkül. A05 párhuzamos IPC/háttérfeladatok, A12 szerepkörök és A15 rögzített ügyfélkiadási bizalmi gyökér továbbra is külön munkacsomag.

Ellenőrzés: 139/139 üzleti/Core/valódi Host teszt, wheel-ok, 9/9 WPF render, alkalmazott keresési állapot késleltetett válasszal, megszakított export célfájl-védelme és mentetlen készletművelet nézetváltás elleni védelme. A futó alkalmazás fájlzárolása miatt elkülönített forrásmásolatból futottak; a nyitott programot nem állítottuk le.
