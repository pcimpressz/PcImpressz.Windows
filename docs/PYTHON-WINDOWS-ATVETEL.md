# Python → Windows átvételi jegyzék

Forrás: `F:\progs\pcimpressz-raktar`. A leltár minden talált Python-fájlt beolvasott és AST-vel feldolgozott; egyik modult sem importálta vagy futtatta. Éles konfiguráció és adatbázis nem került átvételre.

Összesen 87 fájl, 125664 sor, 2942 függvény és 698 route. 74 különböző SHA-256 tartalom. Szintaktikai hiba: 0. A route- és függvénydarabszám a történeti másolatokat is tartalmazza.

## Átvételi állapot

A teljes szerkezeti leltár elkészült. Ez nem azt jelenti, hogy mind a 2942 függvény viselkedése kézzel auditált vagy már átültetett. A részletes működésvizsgálat és a teljes funkcióátvétel folyamatban van. A géppel olvasható jegyzékben minden fájl lenyomata, minden függvény neve és sorszáma, a route-ok és az importok megvannak.

## Ebben a csomagban ténylegesen átvett viselkedés

- `raktar/bevetelezes.py:480–485`, `kerekit`: 1000 alatt 10, 10 000 alatt 50, fölötte 100 Ft-os árjavaslat, párosra kerekített félértékkel. Cél: `RetailPricing.Tiered`, a Beállítások / Eladás és készlet kapcsolójával. A jelenlegi 5 Ft-os árjavaslat az alapértelmezés; a választás mentett.
- Az árképzésben a 0% áfa érvényes marad; a Pythonban előforduló `érték or 27` visszaesést nem vettük át.
- A Windows meglévő beszerzési sorösszeg-megőrzését közös, decimal alapú áfaszámítás szolgálja ki. Az eredeti NAV nettó/áfa/bruttó összegek nem számolódnak át.

## 0.5.0: új rendelési munkafolyamat

- `raktar/bevetelezes.py:foglalas_uj`, `foglalas_lista`, `foglalas_hosszabbit`: tartós, alapból hétnapos foglalás, lejárat és hosszabbítás. A lejárt tétel előzményként megmarad, de szabad készletet nem foglal.
- `raktar/bevetelezes.py:megrendelt_atad`, `rendeleskonyv/server.py:gep_lezar`, `teszt_megrendeles_atadas.py`: rendelési sorhoz kapcsolt átadás, mennyiségi korlát és ismétlésvédelem. A Windows-változatban az eladás, a foglalás és a rendelési állapot közös SQL-tranzakció.
- Natív WPF rendelési oldal, szerkesztő, soronkénti állapot és részátadás; árpillanatkép, jogosultság-, cég- és verzióvédelem.
- Ez a kapcsolódó folyamatok részleges átültetése, nem a teljes `bevetelezes.py` vagy `server.py` portja. Az árajánlatból történő automatikus rendelésképzés, a munkalap/szerviz és a beszállítói rendeléskapcsolat hátravan.

## Függőségi sorrend

1. Indexelt tárolás, pontos pénzkezelés, nagy adatos mérés és migrációvédelem.
2. Pénztár és számlázás, előleg/végszámla, kapcsolt dokumentumok: `elado.py`, `szamlazas.py`, `kimeno.py`, `bizonylatok.py`, a hozzájuk tartozó tesztek.
3. Árajánlat → rendelés → foglalás → beszerzés → részátadás: `arajanlat.py`, `rendeleskonyv/server.py`, `keszlet.py`, `bevetelezes.py`.
4. Beszállítók, nagykerkosarak, frissítések és árösszevetés.
5. Leltár, garancia és szerviz; majd pénzügy, bank, könyvelés és vezetői kezdőlap.
6. Gépösszeállító, kompatibilitás, tudástár és kellékkereső.

A régi tesztek elvárt viselkedése lesz az új regressziók alapja. Külső API-kat szimulált válaszokkal kell ellenőrizni; éles számla vagy e-mail nem teszt. Az egykori InvenTree-függést a saját készletnapló és terméktörzs váltja ki.

## Fájlonkénti cél és státusz

### `_szerver_vissza/_rk/keszlet.py`
440 sor; 20 függvény; 9 route.
Történeti másolat. Eltérésvizsgálat szükséges az aktív forráshoz képest.

### `_szerver_vissza/raktar/arajanlat.py`
8019 sor; 139 függvény; 37 route.
Történeti másolat. Tartalma azonos: raktar/arajanlat.py.

### `_szerver_vissza/raktar/bank.py`
2628 sor; 72 függvény; 9 route.
Történeti másolat. Tartalma azonos: raktar/bank.py.

### `_szerver_vissza/raktar/bevetelezes.py`
7386 sor; 149 függvény; 46 route.
Történeti másolat. Tartalma azonos: raktar/bevetelezes.py.

### `_szerver_vissza/raktar/compmarket.py`
4437 sor; 128 függvény; 15 route.
Történeti másolat. Tartalma azonos: raktar/compmarket.py.

### `_szerver_vissza/raktar/elado.py`
12071 sor; 149 függvény; 76 route.
Történeti másolat. Tartalma azonos: raktar/elado.py.

### `_szerver_vissza/raktar/ellenoriz.py`
132 sor; 1 függvény; 0 route.
Történeti másolat. Eltérésvizsgálat szükséges az aktív forráshoz képest.

### `_szerver_vissza/raktar/gep_kompatibilitas.py`
2864 sor; 102 függvény; 0 route.
Történeti másolat. Tartalma azonos: raktar/gep_kompatibilitas.py.

### `_szerver_vissza/raktar/gpu_modell.py`
41 sor; 4 függvény; 0 route.
Történeti másolat. Tartalma azonos: raktar/gpu_modell.py.

### `_szerver_vissza/raktar/karbantartas.py`
218 sor; 5 függvény; 2 route.
Történeti másolat. Eltérésvizsgálat szükséges az aktív forráshoz képest.

### `_szerver_vissza/raktar/kotelezettseg.py`
1099 sor; 43 függvény; 12 route.
Történeti másolat. Eltérésvizsgálat szükséges az aktív forráshoz képest.

### `_szerver_vissza/raktar/mester_frissites_v3.py`
123 sor; 3 függvény; 0 route.
Történeti másolat. Tartalma azonos: raktar/mester_frissites_v3.py.

### `_szerver_vissza/raktar/nav.py`
955 sor; 32 függvény; 8 route.
Történeti másolat. Eltérésvizsgálat szükséges az aktív forráshoz képest.

### `_szerver_vissza/raktar/nullazas.py`
162 sor; 5 függvény; 0 route.
Történeti másolat. Eltérésvizsgálat szükséges az aktív forráshoz képest.

### `_szerver_vissza/raktar/penzugy.py`
1962 sor; 1 függvény; 1 route.
Történeti másolat. Tartalma azonos: raktar/penzugy.py.

### `_szerver_vissza/raktar/raktar_server.py`
61 sor; 4 függvény; 5 route.
Történeti másolat. Eltérésvizsgálat szükséges az aktív forráshoz képest.

### `_szerver_vissza/raktar/ssd_kovetelmenyek.py`
108 sor; 5 függvény; 0 route.
Történeti másolat. Tartalma azonos: raktar/ssd_kovetelmenyek.py.

### `_szerver_vissza/raktar/ssd_xml_audit.py`
70 sor; 4 függvény; 0 route.
Történeti másolat. Tartalma azonos: raktar/ssd_xml_audit.py.

### `_szerver_vissza/raktar/torlokod.py`
995 sor; 24 függvény; 13 route.
Történeti másolat. Eltérésvizsgálat szükséges az aktív forráshoz képest.

### `_szerver_vissza/rendeleskonyv/keszlet.py`
440 sor; 20 függvény; 9 route.
Történeti másolat. Eltérésvizsgálat szükséges az aktív forráshoz képest.

### `ellenoriz.py`
3086 sor; 29 függvény; 0 route.
Diagnosztikai vagy történeti segédeszköz; szerepe az indítási gráf alapján tisztázandó.. Státusz: feltérképezve; a teljes átültetés hátravan.

### `eszkozok/bolti_nyomtato.py`
227 sor; 6 függvény; 0 route.
Külső nyomtatóügynök: külön Windows-integráció. Státusz: feltérképezve; a teljes átültetés hátravan.

### `eszkozok/duplikatum_takarito.py`
244 sor; 8 függvény; 0 route.
Import/takarítási segédeszköz; csak előnézetet és ellenőrzött migrációt adó Windows-eszközként átvehető. Automatikus futtatása tiltott.

### `eszkozok/inventree_import.py`
342 sor; 10 függvény; 0 route.
Import/takarítási segédeszköz; csak előnézetet és ellenőrzött migrációt adó Windows-eszközként átvehető. Automatikus futtatása tiltott.

### `eszkozok/naturasoft_frissites.py`
524 sor; 18 függvény; 0 route.
Import/takarítási segédeszköz; csak előnézetet és ellenőrzött migrációt adó Windows-eszközként átvehető. Automatikus futtatása tiltott.

### `eszkozok/naturasoft_import.py`
517 sor; 19 függvény; 0 route.
Import/takarítási segédeszköz; csak előnézetet és ellenőrzött migrációt adó Windows-eszközként átvehető. Automatikus futtatása tiltott.

### `eszkozok/nullazas.py`
168 sor; 5 függvény; 0 route.
Import/takarítási segédeszköz; csak előnézetet és ellenőrzött migrációt adó Windows-eszközként átvehető. Automatikus futtatása tiltott.

### `eszkozok/partner_import.py`
317 sor; 10 függvény; 0 route.
Import/takarítási segédeszköz; csak előnézetet és ellenőrzött migrációt adó Windows-eszközként átvehető. Automatikus futtatása tiltott.

### `eszkozok/raktar_import.py`
607 sor; 15 függvény; 0 route.
Import/takarítási segédeszköz; csak előnézetet és ellenőrzött migrációt adó Windows-eszközként átvehető. Automatikus futtatása tiltott.

### `eszkozok/vonalkod_javito.py`
190 sor; 3 függvény; 0 route.
Import/takarítási segédeszköz; csak előnézetet és ellenőrzött migrációt adó Windows-eszközként átvehető. Automatikus futtatása tiltott.

### `patron.py`
1619 sor; 32 függvény; 14 route.
Nyomtatókellék- és kompatibilitáskereső. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/arajanlat.py`
8019 sor; 139 függvény; 37 route.
Árajánlat, elfogadás, rendelésátadás; új Quotes modul. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/arajanlat_pdf.py`
145 sor; 5 függvény; 0 route.
Árajánlat PDF és nyomtatási nézet. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/bank.py`
2628 sor; 72 függvény; 9 route.
Banki import, párosítás és fizetettség; új Finance. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/beallitasok.py`
1162 sor; 6 függvény; 5 route.
Kapcsolatok és működési beállítások; meglévő Settings. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/bevetelezes.py`
7386 sor; 149 függvény; 46 route.
NAV/kézi bevételezés, árképzés, foglalás, rendelésátadás; Receiving + új Orders. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/bizonylatok.py`
1128 sor; 21 függvény; 12 route.
Szállítólevél, teljesítési igazolás és kapcsolt dokumentumok. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/caseking.py`
586 sor; 36 függvény; 6 route.
Caseking beszállítói adapter. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/compmarket.py`
4437 sor; 128 függvény; 15 route.
Beszállítói katalógus, árazás, számlaegyeztetés; új Purchasing. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/dashboard.py`
773 sor; 10 függvény; 3 route.
Operatív kezdőlap és figyelmeztetések. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/elado.py`
12071 sor; 149 függvény; 76 route.
Pénztár, terméktörzs, készlet, zárás, leltár, garancia; Sales/Inventory + új Warranty. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/gep_kompatibilitas.py`
2864 sor; 102 függvény; 0 route.
Gépalkatrész-kompatibilitás szabályai. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/gep_piac.py`
586 sor; 21 függvény; 0 route.
Piaci összevetés. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/gep_trend.py`
477 sor; 22 függvény; 0 route.
Trendadatok és frissesség. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/gep_tudas.py`
504 sor; 27 függvény; 0 route.
Gépösszeállító tudástár. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/gpu_modell.py`
41 sor; 4 függvény; 0 route.
GPU-modellnormalizálás. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/hasznalt_nagyker.py`
1286 sor; 60 függvény; 5 route.
Használt termékek beszállítói adapterei. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/hrp.py`
404 sor; 23 függvény; 3 route.
HRP beszállítói adapter. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/innpro.py`
398 sor; 32 függvény; 6 route.
Innpro beszállítói adapter. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/javito_2_0.py`
143 sor; 2 függvény; 0 route.
Diagnosztikai vagy történeti segédeszköz; szerepe az indítási gráf alapján tisztázandó.. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/karbantartas.py`
218 sor; 5 függvény; 2 route.
Karbantartás és diagnosztika. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/kategoriazas.py`
2337 sor; 58 függvény; 8 route.
Ellenőrizhető kategóriajavaslat és csoportos jóváhagyás. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/kimeno.py`
2042 sor; 51 függvény; 17 route.
Kimenő bizonylatok, számlaállapot és fizetettség. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/konyveles.py`
2547 sor; 68 függvény; 18 route.
Könyvelői nyilvántartások és export. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/kotelezettseg.py`
1099 sor; 43 függvény; 12 route.
Esedékes kötelezettségek és ismétlődő tételek. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/menu.py`
646 sor; 7 függvény; 0 route.
Webes navigáció: natív Windows-navigációval kiváltandó. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/mester_frissites_v3.py`
123 sor; 3 függvény; 0 route.
Diagnosztikai vagy történeti segédeszköz; szerepe az indítási gráf alapján tisztázandó.. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/nagyker_halozat.py`
41 sor; 3 függvény; 0 route.
Beszállítói hálózati absztrakció. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/nagyker_kereso.py`
323 sor; 17 függvény; 0 route.
Beszállítói keresés. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/nagyker_utemezo.py`
88 sor; 7 függvény; 0 route.
Frissítések ütemezése. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/nav.py`
955 sor; 32 függvény; 8 route.
NAV lekérdezés és egyeztetés; Receiving. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/nyomtato.py`
768 sor; 34 függvény; 10 route.
Blokk- és címkenyomtatás; meglévő nyomtatási alap bővítése. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/octopus.py`
774 sor; 44 függvény; 7 route.
Octopus beszállítói adapter. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/patron.py`
1619 sor; 32 függvény; 14 route.
Nyomtatókellék- és kompatibilitáskereső. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/patron_old.py`
1829 sor; 40 függvény; 14 route.
Történeti kellékkereső: összevetés az aktív patron modullal. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/penzugy.py`
1962 sor; 1 függvény; 1 route.
Pénzügyi munkafelület és összesítések. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/raktar_server.py`
61 sor; 4 függvény; 5 route.
Webes indítás és regisztráció: WPF/Host életciklussal kiváltandó. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/ssd_kovetelmenyek.py`
108 sor; 5 függvény; 0 route.
SSD-követelmények. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/ssd_xml_audit.py`
70 sor; 4 függvény; 0 route.
SSD-adatellenőrzés. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/szamlazas.py`
4960 sor; 105 függvény; 37 route.
Számla, előleg, partnerdosszié, számlakapcsolatok; Receiving + új Billing. Státusz: feltérképezve; a teljes átültetés hátravan.

### `raktar/torlokod.py`
995 sor; 24 függvény; 13 route.
Törlési/takarítási munkafolyamatok: egyenként felülvizsgálandó. Státusz: feltérképezve; a teljes átültetés hátravan.

### `rendeleskonyv/keszlet.py`
440 sor; 20 függvény; 9 route.
Rendelési készletkapcsolat és foglalások; új Orders. Státusz: feltérképezve; a teljes átültetés hátravan.

### `rendeleskonyv/server.py`
2246 sor; 111 függvény; 44 route.
Rendelések, munkalapok, garancia, teendők; új Orders/Service. Státusz: feltérképezve; a teljes átültetés hátravan.

### `teszt_arajanlat_arak.py`
227 sor; 13 függvény; 0 route.
Viselkedési referencia; Windows-regresszióként átveendő, éles hívás nélkül.

### `teszt_arajanlat_pdf.py`
60 sor; 4 függvény; 0 route.
Viselkedési referencia; Windows-regresszióként átveendő, éles hívás nélkül.

### `teszt_cm_szamla_xml.py`
68 sor; 3 függvény; 0 route.
Viselkedési referencia; Windows-regresszióként átveendő, éles hívás nélkül.

### `teszt_garancia_xml.py`
138 sor; 10 függvény; 0 route.
Viselkedési referencia; Windows-regresszióként átveendő, éles hívás nélkül.

### `teszt_gep_szabad_ai.py`
162 sor; 14 függvény; 0 route.
Viselkedési referencia; Windows-regresszióként átveendő, éles hívás nélkül.

### `teszt_kintlevoseg_level.py`
226 sor; 11 függvény; 0 route.
Viselkedési referencia; Windows-regresszióként átveendő, éles hívás nélkül.

### `teszt_kozponti_email.py`
88 sor; 6 függvény; 0 route.
Viselkedési referencia; Windows-regresszióként átveendő, éles hívás nélkül.

### `teszt_megrendeles_atadas.py`
294 sor; 15 függvény; 0 route.
Viselkedési referencia; Windows-regresszióként átveendő, éles hívás nélkül.

### `teszt_nagyker_forras.py`
89 sor; 5 függvény; 0 route.
Viselkedési referencia; Windows-regresszióként átveendő, éles hívás nélkül.

### `teszt_naplo_eloleg.py`
120 sor; 8 függvény; 0 route.
Viselkedési referencia; Windows-regresszióként átveendő, éles hívás nélkül.

### `teszt_pdf_melleklet.py`
73 sor; 4 függvény; 0 route.
Viselkedési referencia; Windows-regresszióként átveendő, éles hívás nélkül.

### `teszt_rendeleskonyv_email.py`
241 sor; 20 függvény; 0 route.
Viselkedési referencia; Windows-regresszióként átveendő, éles hívás nélkül.

### `teszt_szamla_eloleg.py`
191 sor; 13 függvény; 0 route.
Viselkedési referencia; Windows-regresszióként átveendő, éles hívás nélkül.

### `teszt_szerviz_kovetes.py`
346 sor; 25 függvény; 0 route.
Viselkedési referencia; Windows-regresszióként átveendő, éles hívás nélkül.
