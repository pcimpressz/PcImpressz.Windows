# NAV és kézi bevételezés – 0.4.21

## NAV-számlák

A NAV-nézet és a kézi bevételezés külön szerkesztőnézet. A NAV-számla kiválasztásakor a szállítót mindig a kiválasztott számla adószámából azonosítjuk. Az előző számla szállítója nem kerülhet át az új előnézetbe. Ha nincs egyezés, az automatikus szállítófelvétel létrehozza a partnert, és ezt a felület kiírja.

A NAV-szerkesztőben nincs Új tételsor gomb. A fejléc, a megnevezések, mennyiségek és beszerzési árak rögzítettek. A termékpárosítás, készletre jelölés, kategória és eladási ár előkészítése továbbra is lehetséges.

A sorvégi **Kézire** gomb az összes adatot a külön **Kézi bevételezés** nézetbe emeli át, és piszkozatként elmenti. Ha ehhez a NAV-számlához már van kézi piszkozat, azt folytatja. A NAV-listára visszatérve az eredeti NAV-tényadatok jelennek meg. Az eredeti NAV-azonosság a végleges mentés után is megmarad, így az ismételt lekérés nem hoz létre második készletre vételt.

A már kézi bizonylatként mentett NAV-számla eredeti nézete csak olvasható; a Kézire gomb a kapcsolódó, még nyitott kézi bizonylatot/piszkozatát nyitja meg. Lezárt bizonylatot nem nyitunk újra szerkeszthetőként.

## Kézi piszkozatok

- Az **Új kézi bevételezés** új kézi számlát indít.
- A módosításokat rövid késleltetéssel automatikusan mentjük a céges SQL-adatbázisba. Hiányzó szállító, üres számlaszám, még be nem írt vagy hibásan beírt mennyiség/ár sem akadálya a piszkozatmentésnek.
- NAV-, PDF-, import- vagy más számlanézetre váltáskor előbb mentjük a nyitott szerkesztést. Nem kérdezzük meg minden váltásnál, hogy eldobod-e.
- A felső **Kézi piszkozatok** gomb listázza a félbehagyott számlákat. **Folytatás**: visszatölti a fejlécet, szállítót, sorokat, termékpárosításokat, részletes termékadatokat, árakat és PDF-szöveget. **Törlés**: eltávolítja a kiválasztott piszkozatot.
- A piszkozat új alkalmazásindítás után is megmarad. Másik cég nem látja. Ütköző módosítás vagy elavult törlési kérés nem írhatja felül a frissebb mentést.
- A sikeres, végleges számlamentés eltávolítja a hozzá tartozó piszkozatot. Maga a piszkozatmentés nem hoz létre készletmozgást és bevételezett számlát.
- Mentési hiba esetén a szerkesztés megmarad, a váltást megállítjuk, és a hibát a felületen jelezzük. Ilyenkor nem állítjuk, hogy a piszkozat mentve van.

A **Bevételezés** gomb végzi a tényleges készletre vételt, a szükséges ellenőrzésekkel. A félbehagyott piszkozat önmagában nem bevételezés.

## Tételek és megjelenés

A táblázat megnevezése csak olvasható. Egy kattintás megnyitja a részleteket, ahol külön Megnevezés mezőben szerkeszthető a kézi tétel. A névmezőben történő kijelölés és gépelés nem csukja be a sort. NAV-nézetben ez a mező is csak olvasható.

A kategóriamező a terméktörzsben használt, aktív kategóriákat kínálja. Név vagy kód részletét beírva a találatok szűrhetők. Új kategória létrehozása továbbra is a Kategóriák modulban történik. A párosított termék kategóriája automatikusan betöltődik.

A vonalkód mellett két 34×34-es gomb van, a mezővel egy vonalban: generálás és címkenyomtatás. Az egérmutatót föléjük tartva megjelenik a funkció neve. A kereshető partner- és kategóriaválasztó a világos és sötét felület színeit követi.

## Ellenőrzés

195/195 üzleti/Core/Host teszt, wheel teszt és 70/70 WPF render sikeres, bindinghibák nélkül. Külön használati próba ellenőrzi az Orink→SMA szállítóváltást, az új NAV-sor tiltását, a kézi átemelést, a hiányos piszkozat új munkamenetben történő folytatását/törlését, a már kézire mentett számla elkülönítését, valamint a névmezőt és a vonalkódgombok igazítását.

Valós számlakiállítás, éles készletpróba és fizikai nyomtatás nem történt. A fejlesztés elkülönített tesztadatokat használt.

Az új forrás futtatásához zárd be a régi programot és a külön indított hostot is, majd indítsd a projekt **Start.cmd** fájlját.
