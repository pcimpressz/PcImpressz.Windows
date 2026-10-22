# Gyors NAV-előkészítés és bevételezés – 0.4.19

## Megjelenés és lista

A belső, második Számlák felirat megszűnt. Kompakt dátummező van naptárgombbal, alsó üres sáv nélkül. A NAV XML megnyitása a Bejövő számlák lekérése mellett található; az előnézetfrissítő gomb kikerült. A bejövő lista és NAV-lista 10, 20, 50 vagy 100 soros oldalakra állítható. A tartós alapérték a Beállítások → Számlák · megjelenítés csoportban menthető. A listán lévő választó az adott munkamenetben módosítja a méretet.

A NAV maga legfeljebb 100 tételes szerveroldalakat ad. Ezeken belül helyben lapozunk, majd a következő NAV-oldalt kérjük le; visszafelé az előző szerveroldal utolsó listaoldalára térünk. A meglévő, legfeljebb 35 napos lekérési időszak megmarad.

A számlamodul saját belső függőleges görgetői megszűntek, beleértve a méretezett panelek és tételadatlap korlátozott magasságát is. A főablak görgeti az oldalt. A széles táblázat vízszintes görgetése megmaradt. Egy tételre kattintva az adatlapja kinyílik; újabb kattintásra bezáródik. Az ármezőben gépelés és a készletjelölő használata nem csukja be az adatlapot. A szerkesztő a felső kis piros × gombbal zárható be.

## Automatikusan mentett előkészítés

Az ár, termékpárosítás, vonalkód, termékmezők, készletjelölés, megjegyzés és raktár rövid gépelési szünet után automatikusan a cég SQL-adatbázisába mentődik. Bezáráskor és másik NAV-számlára váltás előtt is befejezzük a mentést. Az állapotfelirat mutatja a sikert vagy hibát. Egyidejű módosításnál verzióütközés-védelem van; a másik dolgozó módosítását nem írjuk felül. Ilyenkor ne zárd el a még nem mentett munkát, hanem egyeztesd és nyisd meg újra a számlát.

Az előkészítés külön dokumentum: nem bevételezés, nem eladás, nem számlakiállítás. A NAV-számla újbóli megnyitásakor visszatöltődik, így az árak előző nap előkészíthetők. Az ismert termék érvényes ár- és törzsadat-módosításait a közös terméktörzsbe is mentjük. Ismeretlen terméknél az adatokat az előkészítés őrzi; a törzsbe felvételt az Új termék gomb indítja. Érvénytelen vagy közben más által módosított termékadat esetén a törzsmentés hibát jelez, az előkészítés megmarad.

A már lezárt, készletre könyvelt számla tételeit nem írjuk felül korábbi előkészítéssel. A készlet továbbra is csak a Bevételezés művelettel változik.

## Szállító és termékegyeztetés

A hiányzó NAV-szállító a hitelesített számla neve és adószáma alapján automatikusan bekerül aktív beszállítóként. A nem közölt címadatot nem találjuk ki; később a partnertörzsben tölthető ki. Ehhez partnerírási jogosultság is szükséges. A szállító mezőben névrészlet alapján kereshetsz a már rögzített beszállítók között. Az Új szállító gomb kikerült a számlaszerkesztőből.

Egyedi, pontos vonalkód-, cikkszám- vagy névegyezés automatikusan párosul. A név egyezése azonosító nélküli NAV-sornál is működik. Hasonló termékek ajánlott listában látszanak, de többértelmű egyezést nem választunk ki helyetted. Eltérő mértékegységnél nem végzünk néma átváltást. Az eredeti NAV-megnevezés párosításkor is megmarad, a saját cikkszám és vonalkód a törzsből érkezik.

## Védett NAV-adatok és kézi mód

NAV-számlán nem adható hozzá vagy törölhető tétel; a megnevezés, mennyiség, egység, nettó beszerzési ár és áfa védett. A védelem a háttérszolgáltatásban is érvényesül. A számlaszám és kelt a NAV-nézetben nem szerkeszthető. Teljes módosításhoz az Átváltás kézi bevételezésre gombot használd; másik szállító kiválasztása szintén kézi módot jelent.

Kézi módban minden számlaadat és tételsor szerkeszthető. Az eredeti NAV-számlaszám és szállítói adószám kapcsolata megmarad, ezért az eredeti NAV-számla újranyitása nem készít második bevételezést. A program nem írja át a NAV-nál lévő számlát.

## Készlet, ár, haszonkulcs és fedezet

A Készletre jelölő soronként állítható. A nem jelölt sor – például szállítás, kávé vagy cukor – része marad a számla összegének és költségeinek, de nem hoz létre készletmozgást; termékpárosítás nélkül is maradhat költségsor. A már készletes termék nem jelölt sora sem növeli a készletet.

Haszonkulcs módosításakor a bruttó kisker ár automatikusan számolódik: nettó beszerzés × (1 + felár / 100) × (1 + eladási áfa / 100), a korábbi program szerinti 5 Ft-os árjavaslati kerekítéssel. Bruttó kisker ár beírásakor a beszerzésre vetített haszonkulcs számolódik vissza. A Fedezet oszlop a nettó eladási árhoz viszonyított százalékot mutatja. A korábbi külön árszámító gomb megszűnt. A készpénzes végösszeg kerekítése ettől külön eladási szabály marad.

## Vonalkód és Bluetooth-címke

A Generálás gomb 20-as kezdetű belső EAN-13 javaslatot készít ellenőrző számjeggyel, a már ismert törzskódok ellenőrzésével. Ez belső üzleti kód, nem igényelt gyártói GS1-azonosító. A címke a megadott vonalkódot Code128 szimbólumként kódolja, a terméknévvel és olvasható kóddal.

Alapnyomtató: Niimbot D110-M Bluetooth, 12×40 mm-es szalag. Kapcsold be a gép Bluetooth-kapcsolatát és a nyomtatót, a telefon és a régi böngésző kapcsolatát bontsd. A Címke gomb első használatakor a program megkeresi a D110 eszközt; több találatnál választót ad. Ugyanabban a munkamenetben a választás megmarad, a fizikai GATT-kapcsolatot minden küldés után elengedjük. Küldési hibánál nincs automatikus újranyomtatás: előbb ellenőrizd a készüléket.

A kapcsolat közvetlenül a Windows Bluetooth API-ját használja, a régi programban használt D110-M parancssorrenddel. Windows-driveres nyomtató is megadható a Beállítások → Számlák · megjelenítés mezőben; üres mezőnél Windows-nyomtatóválasztó nyílik. A natív Bluetooth miatt a grafikus alkalmazás minimum Windows 10 2004 / build 19041 célverzióra épül.

**A címkekód, raszter és csomagkeretezés tesztelt; a felhasználó valódi Niimbotjával fizikai nyomtatási próba még szükséges.** A fejlesztés során eszközkeresés és nyomtatás nem történt.

## CompMarket API

Beállítások → Beszállítói API-k · CompMarket: a kiválasztott cég beállításainak betöltése után pid, hitelesítő kód, fix kimenő IP-t biztosító proxy és engedélyezés menthető. A kód DPAPI-val titkosítva kerül a cég modulmappájába; a felületen nem olvasható vissza. A mentés nem végez hálózati próbát. A régi raktárprogram éles hitelesítőit nem másoltuk át.

A 23126806 adószámtörzsű CompMarket-szállítót felismerjük. Engedélyezett kapcsolat esetén a konkrét számlaszámhoz csak olvasási XML-lekérés indul; a Szállítói adategyeztetés gombbal külön is kérhető. Egyértelmű sorazonosságnál vonalkód, nagyker cikkszám, gyártói cikkszám és szállítói kategória egészíthető ki. A kategóriamegnevezés a termék paraméterei közé kerül; a saját kategóriatörzs kódjához nem rendelünk automatikusan idegen kategóriát. A NAV árai, mennyiségei és összegei nem származhatnak a nagyker aktuális katalógusából.

Proxy nélkül nem kapcsolódunk közvetlenül. A számlaszámot, az XML méretét és szerkezetét ellenőrizzük; többértelmű sort nem párosítunk. A tesztek szintetikus HTTP-válasszal futottak, éles CompMarket-hívás nem történt. Valódi kapcsolati próba a céges adatok beállítása után szükséges.

## Ellenőrzött kiadás

194/194 üzleti/Core/Host teszt; egérgörgő-ellenőrzés és 70/70 WPF render, kötési hiba nélkül. Ellenőrzött: előkészítés visszatöltése, cég- és verzióvédelem, eredeti NAV-adatok védelme, vegyes költség/készlet-sorok és változatlan összesítés, kézi mód NAV-kapcsolatának megőrzése, automatikus szállító, névpárosítás, árszámítás, lapozás, sor nyitás/zárás, vonalkódkép és Bluetooth-csomagformátum. Éles számla, e-mail, külső író API, eredeti céges készletmódosítás vagy próbanyomtatás nem történt.

Indítás: a régi felület és esetleg külön indított háttérfolyamat bezárása után Start.cmd. A korábbi NAV-korlátok megmaradnak: normál HUF-számlák, 0/5/18/27% áfa; módosító, sztornó, devizás és különleges áfás számlák további fejlesztést igényelnek.

Technikai hivatkozások: [Windows Bluetooth GATT](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-client), [WPF DatePicker sablonrészek](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/DatePicker.cs). A CompMarket konkrét számla-XML-kérése és a D110-M protokoll a meglévő raktárprogram megfelelő megvalósítását követi.
