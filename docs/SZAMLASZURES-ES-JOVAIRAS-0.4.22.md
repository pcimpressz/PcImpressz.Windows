# Költségsorok, jóváírások és számlaszűrés – 0.4.22

## Készletre nem vett sorok

A szállítási költség, saját felhasználású kávé/cukor és más költség sorának **Készletre** jelölését kapcsold ki. A sort ne töröld a számlából. A teljes számla nettó, áfa- és bruttó összegében továbbra is szerepel, de készletmozgást nem hoz létre.

A NAV eredeti nettó, áfa- és bruttó sorösszegeit megőrizzük. Így a NAV által küldött kedvezményes összeg vagy nagyobb pontosságú egységár nem vész el az újraszámolás miatt. A számlaösszesítő ellenőrzése továbbra is működik: a ténylegesen hibás vagy hiányos számla nem véglegesíthető.

A bevételezett számlát a helyi listában egy kattintással megnyitva felül látszik a **Nem lett raktárra véve** sorfelsorolás. A tételtáblázat **Készlethatás** oszlopa egyenként is mutatja, mely sor került raktárra, melyik nem, és melyik csökkentette a készletet.

## Negatív mennyiségek

Kézi és NAV-os bevételezésben is használható negatív mennyiség. Például **−2 db × 1 000 Ft nettó egységár** két darabbal csökkenti a kijelölt raktár készletét. Az előjel a pénzügyi összesítésben is megmarad.

A NAV MODIFY és STORNO dokumentumai is megnyithatók; a küldött előjeles tételeket dolgozzuk fel. Nem fordítjuk meg önkényesen az előjeleket a dokumentum műveletkódja alapján. Az eredeti számlahivatkozást és műveletkódot tároljuk. A nulla mennyiségű, kizárólag pénzügyi korrekciós sor automatikusan nem készletes sor.

A készletet a **mennyiség előjele** módosítja. Egy negatív egységár önmagában nem jelent negatív készletmozgást. A készletellenőrzés megmarad: készletcsökkentéshez rendelkezésre álló készlet kell; sikertelen könyveléskor egyetlen sor készlete sem változik. A visszafordítás a korábbi mozgások ellenkezőjét könyveli, az ismételt NAV-import nem hoz létre második készletmozgást.

A korábbi HUF- és támogatott áfakezelési korlátok változatlanok. A készletmennyiségek legfeljebb négy tizedes pontosságúak; a NAV pénzügyi egységárának nagyobb pontosságát külön megőrizzük.

## Szűrés

- **Oszlopfejléc:** kattints például a Számlaszám, Szállító, Adószám, Kelt, Nettó, Bruttó vagy Állapot feliratára. A helyi listában az oszlop melletti nyíllal továbbra is rendezhetsz.
- **Szűrés gomb:** külön, lenyitható panel szállítónév/adószám, kelt dátum tól–ig, bruttó összeg tól–ig mezőkkel. Negatív összeghatárok is használhatók. A különböző feltételek együtt érvényesülnek.
- **Élő keresés:** Beállítások → **Számlák · megjelenítés → Bejövő számlák: élő szűrés gépelés közben**. Bekapcsolva rövid, 280 ms-os késleltetés után frissül a lista. Kikapcsolva az Alkalmazás/Enter, illetve a Szűrés alkalmazása gomb végzi a keresést.
- **Szűrők törlése:** visszaállítja az oszlop- és részletes szűréseket. A kiválasztott helyi státusznézet, például Bevételezve, külön állítható.

A helyi bejövő számlák szűrése a teljes céges adatállományra vonatkozik; a CSV-export ugyanazokat a feltételeket használja. A NAV-szűrés a kiválasztott, legfeljebb 35 napos lekérési időszak minden NAV-oldalát betölti, így nem csak az aktuális képernyőn látható sorokban keres. Biztonsági felső korlát: 200 NAV-oldal/20 000 számla, efelett kisebb időszakot kell választani. A NAV bruttó tartomány csak ismert bruttó összegű sorokat ad vissza; a hiányzó összeget nem találjuk ki.

## Ellenőrzés

200/200 üzleti/Core/Host teszt, wheel teszt és 70/70 WPF render sikeres. Új tesztek: eredeti kedvezményes NAV-sorösszeg és kihagyott fuvar; negatív kézi/NAV könyvelés és visszafordítás; elégtelen készletnél teljes visszagörgetés; kombinált, előjeles összeghatáros szűrés; élő és kézi alkalmazás; következő NAV-oldalon lévő találat; lezárt számla kihagyott sorának felirata.

Éles számlakiállítás és eredeti céges készletadatok módosítása nem történt. A NAV-mezők értelmezésének forrása a [NAV hivatalos v3.0 számlaadat-sémája](https://github.com/nav-gov-hu/Online-Invoice/blob/master/src/schemas/nav/gov/hu/OSA/invoiceData.xsd).

A futó régi alkalmazást és a külön indított hostot zárd be, majd indítsd a projekt **Start.cmd** fájlját.
