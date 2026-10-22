# NAV bevételezés – 0.4.20

## Használat

1. Számlák → NAV-számlák lekérése → időszak → Bejövő számlák lekérése.
2. Kattints a számlára. Alatta megjelennek a tételek. A szállítót adószám alapján azonosítjuk; ha hiányzik, automatikusan felvesszük, és ezt kiírjuk. Ha már vevőként szerepel, ugyanazt a partnert tesszük szállítóvá is, a többi adatát megőrizve.
3. A „Szállító automatikus azonosítása / felvétele” kikapcsolásával megjelenik a kereshető partnerlista. NAV-számlához csak azonos adószámú partner választható. Sikertelen automatikus felvételnél a konkrét hibát kiírjuk, a tétellista továbbra is megnyitható.
4. A kategóriakód automatikusan a párosított terméktörzsből érkezik, külön oszlopban és a részletekben látható. Az eladási ár és haszonkulcs továbbra is egymást számolja. A „Terméktörzs tallózása…” külön kereshető listát nyit. A sor alatti javaslatokból is választhatsz. Új termék felvételéhez az „Új termék” gomb használható.
5. Csak a készletre kerülő sorokon hagyd bent a Készletre jelölést. Szállítás, saját felhasználású kávé/cukor és egyéb költség sora is a számlában marad, készletmozgás nélkül.
6. **Bevételezés**: a szerkesztő tetején és alján is elérhető. Ez menti a számlát és végrehajtja a készletre vételt. A készletre jelölt, párosítatlan sorokat előbb rendezni kell; a program felsorolja őket.
7. **Bevételezett számlák**: a felső eszközsoron található. A helyi adatbázisból listáz, NAV-lekérés nélkül; a bizonylat dupla kattintással nyitható. A NAV-listában a bevételezett számlák zöldek.

Az előkészítés automatikus mentése önmagában nem készletre vétel. A szerkesztő felső × gombja bezárja az előkészítést, és megőrzi a mentett adatokat.

## Rögzített NAV-adatok és kézi átváltás

A NAV-szállító, a számlaszám és a kelt nem szerkeszthető fejlécmező. A dátum, számlaazonosság, mennyiségek és beszerzési árak nem változnak a termékpárosítástól.

A NAV-lista sorának végén a **Kézire** gomb átvált a kiválasztott számla kézi bevételezésére. Ott a fejléc és a tételek szerkeszthetők. Az eredeti NAV-azonosságot továbbra is megőrizzük, hogy az ismételt lekérés ne hozzon létre második bevételezést. Lezárt számlát nem alakítunk át ezzel a művelettel.

## Segédadatok beolvasása

A sorvégi **Adatok beolvasása** menü:

- **XML-segédlet**: legfeljebb 1,5 MB, DTD nélkül. A dokumentumban az eredeti számlaszámnak (`invoiceNumber` vagy `szamlaszam`) és a szállítói adószámnak (`supplierTaxNumber` vagy `supplierTaxId`) is egyeznie kell. A `line`, `item` vagy `tetel` sorok egyértelmű megnevezésegyezése alapján olvasunk vonalkódot, cikkszámot és új, még nem párosított termékhez MPN-t/kategóriakódot. Ismétlődő vagy bizonytalan megnevezéseket kihagyunk. Árat és mennyiséget nem importálunk. Más XML-sémához külön illesztés szükséges lehet.
- **Szállítói API**: jelenleg a már beépített CompMarket olvasási kapcsolatot használja, ha az adott cégnél be van állítva. Más szállítóhoz külön integráció szükséges; a program ezt jelzi.
- **PDF-segédlet**: az aktuális előkészítéshez olvasható PDF-szöveget csatol, amely az automatikus előkészítésmentéssel megmarad. Nem hoz létre új számlát. Automatikus PDF-tételfelismerés és OCR még nincs; az eredeti fájlt külön is meg kell őrizni.

A segédletek a NAV-tényadatokat nem írják át. Éles kimenő számlakiállítás nem része ennek a javításnak.

## Ellenőrzés és indítás

194/194 üzleti/Core/Host teszt, wheel teszt és 70/70 WPF render sikeres, bindinghiba nélkül. A NAV felületi használati próba szintetikus adatokkal ellenőrzi az automatikus szállítót, a mentést, a fejlécvédelmet, a kategóriát, az XML azonosítását és a bevételezést. Valós számla kiállítása és fizikai címkenyomtatás nem történt.

A friss forrás indításához zárd be a régi alkalmazást és a külön indított hostot is, majd futtasd a projekt Start.cmd fájlját.
