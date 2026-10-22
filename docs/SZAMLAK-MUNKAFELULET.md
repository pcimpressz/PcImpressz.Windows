A 0.4.16-tól a kimenő lista elsődleges forrása a saját SQL-nyilvántartás; a NAV külön egyeztet. A szolgáltató- és számlatömb-beállítások, valamint a még nem aktivált API-adapter állapota: [0.4.16](KIMENO-SZAMLA-0.4.16.md).

# Számlák munkafelület – 0.4.15

A Bevételezés menü átnevezve Számlákra. A belső receiving modulazonosító, tárolási hely, korábbi számlák és jogosultságok megmaradtak; nem szükséges adatmozgatás vagy új modul megvásárlása.

## Bejövő számlák

- A saját bejövő lista kereséssel, besorolási szűrővel, megnyitással és CSV-exporttal folyamatosan az oldalon marad.
- A NAV-számlák lekérése gomb a NAV-panelt nyitja/csukja ugyanazon az oldalon. A fejléc melletti nyíllal is lenyitható. Nem nyílik új ablak vagy külön oldal. Az időszak, lapozás és lista megmarad a számla betöltésekor.
- A panelben legfeljebb 35 nap kérhető le egyszerre; szállító adószám alapján választható, illetve létrehozható. A kiválasztott számla az alsó szerkesztőbe kerül.
- A NAV-panel szállítója külön választás: másik NAV-számla kijelölése nem írja át a megnyitott számla szállítóját. Másik számla betöltése előtt a nem mentett szerkesztésről dönteni kell.
- A Kézi bevételezés az alsó szerkesztőben nyit új piszkozatot. A tételek termékekkel egyeztethetők, új termék és szolgáltatássor rögzíthető.
- Az Excel / CSV / XML import továbbra is működik. Korábbi NAV XML a NAV-panel lenyitható részéből tölthető fel.
- A szerkesztőből menthető, bevételezhető, költségként vagy függőben rögzíthető a számla. A lista frissül; a szerkesztő külön bezárható. A letöltés és a piszkozat mentése még nem hoz létre készletmozgást.

A korábbi NAV-import korlátai megmaradtak: normál HUF-számlák; a módosító, sztornó, devizás és különleges áfakezelésű számlák automatikus bevételezése nem támogatott. Az ismételt NAV-import a meglévő számlát nyitja meg.

## PDF alapján rögzítés

A PDF alapján rögzítés gomb fájlválasztás után helyben kiolvassa a PDF szövegét, és új, nem mentett számlapiszkozatot nyit. A kiolvasott szöveg ugyanazon az oldalon olvasható és másolható; mellette/alatta ki kell tölteni és ellenőrizni a szállítót, számlaszámot, dátumokat és tételsorokat. Nem találunk ki áfát, árat vagy mennyiséget.

Ez ellenőrzést segítő PDF-import, nem általános automatikus tételfelismerés. Szkennelt/kép alapú számlához nincs OCR. A sérült, nem olvasható vagy üres PDF érthető hibát ad, és nem ment üzleti adatot. Korlátok: 1,5 MB, 30 oldal, 100 000 kiolvasott karakter. A PDF-fájlt nem archiválja a program: az eredetit külön kell megőrizni. A mentett számlán a PDF forrásjelölés és a fájlnév a megjegyzésben szerepel.

## Kimenő számlák

A cég meglévő NAV-kapcsolatával, OUTBOUND irányban kérhető le a saját kibocsátású számlák listája, dátumszűréssel és lapozással. Látható a számlaszám, vevő neve/adószáma (ha a NAV közli), kelt, nettó összeg, pénznem és a NAV műveletkódja. A kibocsátó adószáma ellenőrzésre kerül. Az eredeti számlák mellett módosító/érvénytelenítő adatszolgáltatások is megjelenhetnek; ezeket a műveletoszlop jelzi.

A lista nem állít ki új számlát, nem könyvel új eladást és nem változtat készletet. Az eladásmodul helyi bizonylatai és számlaigényei nem válnak ettől kiállított számlákká. A teljes számlakiállítás, kimenő számla tételeinek megnyitása és számla-PDF előállítása külön fejlesztés maradt. NAV-nál nem elérhető számlák itt nem jelennek meg.

## Ellenőrzött kiadás

180/180 üzleti/Core/Host teszt és 63/63 felületi render sikeres. Külön ellenőrzés: OUTBOUND kérés és kibocsátóazonosítás, PDF-szöveg és hibák, PDF-forrású piszkozat készletváltozás nélkül, NAV-panel megtartása, szállítóválasztás elkülönítése, nézet- és cégváltás. A PDF kiolvasása valódi, aláírt modulcsomagon és helyi háttérfolyamaton át is tesztelt.

A NAV-válaszok szintetikusak voltak. Élő céges NAV-lekérés nem történt, az eredeti céges adatbázist a tesztek nem módosították. A kiadási csomagban mind az öt 0.4.15 modul aktívan elindult.

PDF-olvasó: [PdfPig 0.1.16](https://github.com/UglyToad/PdfPig), Apache-2.0; a függőségek az aláírt modulcsomag részét képezik.
