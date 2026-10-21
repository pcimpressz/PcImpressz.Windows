# PC-Impressz – közös munkanapló

Ez a projekt közös átadó- és állapotfájlja Cursor és ChatGPT/Codex számára. A bejegyzések bizonyítékot és következő teendőt is tartalmazzanak. A feladat felsorolása önmagában nem jelent javítási engedélyt vagy feladatvállalást.

## Aktuális átadás – v0.4, 2026-10-20

**STABIL-01 elkészült az első használható változat szintjén.** Felelős: ChatGPT/Codex. Kiinduló commit `96276b6`; új commit nem készült. A felhasználó engedélyezte a javítást, adatkezelést/mentést, készletet és eladást. Cursor a felhasználó közlése szerint nem dolgozott közben. Az alábbi történeti auditot a jelen szakasz helyezi új állapotba.

### Elkészült és ellenőrzött

- **H01–H06 javítva:** tényleges importáfa; HUF és támogatott áfakezelés ellenőrzése; forrásösszesítők megőrzése és egyeztetése; szállítóadóazonosító-egyezés; időszakszűrésnél azonos beszerzési összesítési szabály; explicit szolgáltatásjelölés; ismeretlen fizetettség helyes felirata; kibontott XLSX/CSV/XML korlátok; mentés előtti méretellenőrzés.
- **Adatkezelés:** Host által biztosított, cégenkénti SQLite-tranzakciók, sémaverzió, hiba esetén visszagörgetett egyszeri JSON-migráció. A régi fájlokat megőrizzük és már nem írjuk a Hostban. Az opcionális régi partnerdosszié is átkerül. A terméktörzs és a dosszié típusos lekérdezési adaptert használ a többi modul adatához.
- **Cégmentés:** ellenőrzött `.pcibak` csomag a Beállításokban; SHA-256 és adatbázis-integritásvizsgálat; visszaállítás új cégként, meglevő cég felülírása nélkül. Felülettől független Maintenance eszköz is készült. A hordozható mentés üzleti adatot tartalmaz, titkot/licencet/felületi beállítást nem.
- **Készlet:** külön aláírt/licencelt Inventory modul, raktárankénti egyenleg, nyitókészlet/korrekció, megőrzött mozgásnapló, ellenmozgásos kézi visszafordítás. Nincs negatív készlet vagy észrevétlen egységátváltás. Már mozgatott termék egysége nem írható át.
- **Bevételezés:** az új véglegesítés a számlaállapotot és a készletbevétet közös tranzakcióban rögzíti. A régi, csak besorolt számlákból nem rekonstruálunk automatikusan készletet. A tényleges kezdő egyenleget a felhasználó állapítja meg.
- **Eladás:** külön aláírt/licencelt Sales modul, kereshető termék/vevő választó, többtételes kosár, pontos áfaösszegzés, cégenként/évenként számozott helyi eladási bizonylat és atomi készletcsökkentés. Ismétlésbiztos véglegesítés és visszafordítás. Az eladhatóságot és a tényleges készletet a háttér ellenőrzi.
- **Felület:** az új nézetek a modul képességleírásából kapcsolódnak a kerethez. A dossziégomb is deklarált képesség. Az oszlopok testreszabhatók, a listák kereshetők; nagy válaszok helyett korlátozott választók és külön eladási tétel-lekérdezés készültek. A terméktörzs valós mennyiséget mutat, a partnerdosszié a helyi eladásokat is összesíti.
- **Függőségek és kiadás:** Microsoft.Data.Sqlite 10.0.13, rögzített lockfájlok; Contracts API **1.1**, termékverzió **0.4.0**. A közös Business DLL a modulok aláírt jegyzékének része, a natív SQLite-illesztő a Host függősége. Kikapcsolt függő modul mellett az üzleti írás is tiltott.

### Bizonyíték és átvétel

- Standard `dev.ps1 -Action Test`: Release build 0 hiba / 0 figyelmeztetés; **142/142** sikeres üzleti/Core/Host teszt; **wheel-ok**; **6/6** WPF render kötési hiba nélkül. Napló: `artifacts/final-verification.log`; képek: `artifacts/ui-checks`.
- A valódi Host-integráció aláírt csomagokon ellenőrizte a készletet, eladást, bevételezést, ismételt beküldést, visszafordítást, letiltott függőséget, lejárt licencet, készletoszlopot és mentés/visszaállítást. Külön próbák fedik a konkurens eladást, visszagörgetést, egységeltérést, forrásösszeg-eltérést és migrációt.
- A saját telepítésen ellenőrzött cégmentés készült: `F:\progs\PcImpressz.Backups\20261020-152356`. A teljes eredeti adatmappa megmaradt itt `local-before-upgrade` néven. Az új `.local` 28 fájlja ellenőrzőösszeggel egyezett a másoláskor; a költöztetés a korábban adminisztrátorként létrehozott fájlok frissítési jogosultságát rendezte. Telepítési/cégazonosítót és NAV-titkot nem változtattunk meg.
- A saját adatokból nem készült próbának új eladás vagy készletmozgás; az üzleti tesztek elkülönített mesterséges cégeket használtak. Valódi NAV/InvenTree lekérést nem futtattunk.
- Kiadás elkészült: `artifacts/desktop`, a karbantartó eszköz a `maintenance` alkönyvtárban. A kiadott Hosttal is 142/142 teszt sikeres (`artifacts/published-host-verification.log`); az aktuális saját telepítésen 5/5 aláírt modul aktív, licenc érvényes. Privát kiadói kulcs nem került a kiadásba.
- Kézi felhasználói átvétel még hátravan. Használati leírás: `docs/KESZLET-ELADAS-ES-MENTES.md`.

### Tudatos átmenet és következő feladatok

A SQLite most tranzakciós dokumentumtároló: a korábbi törzs- és bevételezési állapot snapshot, az új eladás és készletmozgás külön sor. Ez nem teljesen normalizált relációs végmodell. A snapshot-séma ismerete a közös típusos adapterbe került; ennek rekordokra bontását és SQL-szintű indexeit külön, mérhető migrációval végezzük, nagyvállalati teljesítményt még nem állítunk.

Következő prioritás: felhasználói kipróbálás üres demócégben → észlelt folyamat/UX hibák javítása → mért terhelési határok, rekordalapú tárolás és adatbázis-kényszerek → bevételezési helyesbítés/raktárközi átadás/árértékelés → nyomtatás, pénzügy és külön számlázási/NAV-integráció → üzemi aktiválás/telepítő → megjelenési drag-and-drop szerkesztő. Ezek közül új feladatot külön tulajdonossal és elfogadási feltétellel kezdjünk, a kész v0.4 működését megtartva.

Az eladás jelenleg helyi bizonylat, **nem adóügyi számla**. PDF-olvasás, élő NAV-lekérdezés, pénztár/kiegyenlítés, készletértékelés, raktárközi átadás és több gépes közös működés még nincs. A helyi fejlesztői licencek nem helyettesítenek egy üzemi előfizetési szolgáltatást.

## Elfogadott fejlesztési irány – 2026-10-20

A felhasználó engedélyezte a teljes sorrendet: hibajavítás → mentés/visszaállítás és adatkezelés → készlet → eladás. Közlése szerint Cursor befejezte a munkáját; jelenleg nincs ismert párhuzamos szerkesztés. Az alábbi elvek az új fejlesztések alapjai.

- **Moduláris monolit:** a .NET 10/WPF alap és az aláírt, külön licencelhető modulok maradnak. Nem építünk indokolatlan mikroszolgáltatásokat vagy saját adatbázismotort. A felület nem tartalmazhat üzleti véglegesítési vagy licencmegkerülési logikát.
- **Tranzakciós adattár:** cégenként helyi SQLite, hivatalos Microsoft.Data.Sqlite illesztővel, paraméterezett SQL-lel, idegen kulcsokkal, verziózott sémával. Pénz és mennyiség pontos decimal/skálázott egész ábrázolás, soha lebegőpontos pénzszámítás. A támogatott pénznem kezdetben HUF; más devizát nem címkézünk át forintnak.
- **Fokozatos migráció:** a régi JSON-adatokat ellenőrzéssel és tranzakcióban vesszük át. Az eredeti források megmaradnak, az új adattárra átállás után nem írjuk párhuzamosan a két formátumot. Ismeretlen jövőbeli séma és sérült forrás esetén megállunk, nem indulunk észrevétlenül üres céggel.
- **Modulhatárok:** modul nem olvashatja másik modul belső fájlját. A Host biztosít céges tranzakciós tárolási és lekérdezési szerződést; minden parancs a meglévő jogosultsági/licencellenőrzésen halad át. A már meglévő fájlkapcsolatokat migrációs adósságként, kijelölt átmeneti lépésekben szüntetjük meg.
- **Bizonylatok és készlet:** a véglegesítés ismételhető kérésre sem hajtható végre kétszer. Bizonylat és készletmozgás egy tranzakcióban készül. Véglegesített adatok nem szerkeszthetők; a visszafordítás ellenmozgás, megőrzött előzménnyel. Negatív készlet kezdetben tiltott. Régi „Bevételezve” jelölésből nem találunk ki automatikusan nyitókészletet.
- **Adatbiztonság:** visszaolvasással ellenőrzött, verziózott, integritásellenőrzött cégmentés; visszaállítás ellenőrzött új cégként, az eredeti adat felülírása nélkül. Titkok és licencek nem kerülnek hordozható cégmentésbe; a NAV-kapcsolat újra beállítandó.
- **Fejlesztési módszer:** kis, ellenőrizhető lépések; először reprodukáló regressziós teszt, utána javítás; integrációs teszt a valódi Hosttal és aláírt modulokkal, tranzakciós/idempotencia/hibás bemenet tesztek. Build + releváns tesztek nélkül nincs „kész”. Új függőség konkrét indokkal, rögzített verzióval és zárolt feloldással kerüljön be.
- **Felhasználói felület:** a jelenlegi skinrendszer marad; készlet és eladás ténylegesen használható képernyőt kap. Egyedi üzleti nézet megengedett, a közös keretben a kiválasztás képességleírás alapján történjen. A későbbi drag-and-drop szerkesztő csak megjelenést alakíthat.
- **Átadás:** a kész funkció, technikai alap, migrációs adósság és még nem tesztelt használat külön szerepeljen itt. Ne nevezzünk eladási bizonylatot hatósági számlának, amíg nincs kész az ehhez szükséges külön számlázási integráció.

## Aktuális fejlesztés – ChatGPT/Codex

- **STABIL-01** – Első használható v0.4 elkészült és automatizáltan ellenőrzött; kézi átvétel még szükséges. Induló commit: `96276b6`. Tulajdonos: ChatGPT/Codex. Terület: Receiving/Partners javítások és tesztjeik; Contracts, Core tranzakciós tároló és mentés; új Inventory és Sales modulok; szükséges Host/Shell/kiadói integráció. A feladatlista engedélyezett, a befejezést teszteredményekkel külön rögzítjük.

## Korábbi audit helyzete (történeti állapot)

- Projekt: `F:\progs\PcImpressz.Windows`.
- Utolsó felmérés: 2026-10-20, 14:51, Europe/Budapest.
- Vizsgált Git-verzió: `96276b6bdb8bdd3d70b1c81b5263f7b3ef19c24c`.
- Az ellenőrzés kezdetén és a dokumentálás előtt a munkafa tiszta volt.
- A felhasználó szerint Cursor jelenleg javításon dolgozik. Ennek pontos tárgya és lefoglalt fájljai nem ismertek; nem feltételezzük, hogy az alábbi hibák javítását már vállalta.
- Ez a felmérés a fenti verzióra érvényes. Későbbi változtatásnál az érintett megállapítást újra kell ellenőrizni.
- ChatGPT/Codex ebben a körben alkalmazáskódot nem módosított; ezt az egy dokumentumot hozta létre. A hibák egyelőre nyitottak.

## Együttműködési szabályok

1. Munka előtt olvasd el ezt a fájlt, a Git-állapotot és az érintett legfrissebb változtatásokat.
2. Kezdéskor írj az „Aktív feladatok” részbe: azonosító, felelős, cél, érintett fájlok, induló commit, állapot. Más szerkesztő lefoglalt fájljait előbb egyeztessétek a felhasználón keresztül.
3. Más munkáját ne írd felül és ne állítsd vissza. Az átfedő dokumentumszerkesztések előtt is olvasd újra az aktuális fájlt.
4. Befejezéskor rögzítsd, mi változott, mi maradt hátra, milyen teszt futott és milyen eredménnyel. Commitazonosítót csak létező commitra írj.
5. A „kész”, „tesztelt”, „felhasználó által kipróbált” és „javasolt” állapot különbözik. A sikeres fordítás nem helyettesíti az üzleti ellenőrzést.
6. Titkos kulcsot, jelszót, API-tokent és ügyféladatot ide ne másolj. A `.issuer`, `.local`, `.tools`, `bin`, `obj` és `artifacts` nem közös forrásdokumentáció.
7. A felhasználó döntései elsőbbséget élveznek. A jelen audit nem adott felhatalmazást a Cursor aktuális munkájának megszakítására vagy kódjának módosítására.

## Aktív feladatok

- **CURSOR-AKTUALIS** – Felelős: Cursor. Állapot: a felhasználó legfrissebb közlése alapján befejezte; jelenleg nincs aktív feladat.
- **AUDIT-20261020** – Felelős: ChatGPT/Codex. Állapot: kész. Eredmény: forrásáttekintés, elkülönített fordítás és tesztek, három célzott hibareprodukció, ez a munkanapló. Vizsgált commit: `96276b6`. Alkalmazáskód-változtatás: nincs.

## Mi készült el?

### Ebben a ChatGPT-beszélgetésben korábban elkészült alapok

Ezeket a beszélgetés előzménye igazolja; a Git teljes szerzői eredetét nem állítjuk.

- Core, Contracts és ApplicationHost; digitálisan aláírt modulcsomagok, fájlellenőrzés, modulfüggőségek, ES256 licenc és időkorlátos működési engedély.
- Jogosultság- és licencellenőrzés a Host oldalán; lejárt licencnél olvasási/exportálási helyreállítási mód.
- WPF keret, témák, biztonságos JSON-skinek, betűméret, oldalsáv- és sűrűségbeállítások.
- Helyi, azonos Windows-felhasználóra korlátozott kommunikáció a keret és a Host között.
- Cégenként elkülönített adatok; új, teljesen üres saját vagy demó cég létrehozása InvenTree nélkül.
- Partnertörzs, szerkesztési verzióellenőrzés, archiválás, naplózás, CSV-export és opcionális, előnézetes InvenTree-import.
- Fejlesztői modulaláírás és fejlesztői licencek kezelése; a privát aláírókulcs nem része az ügyfélkiadásnak.
- Projekt áthelyezése az F: meghajtóra; a témázható felület kezdeti megjelenítési javításai.
- E korábbi átadáskor 82 teszt volt sikeres. Ez történeti adat, nem a mostani tesztszám.

### Azóta megfigyelt fejlesztések

A felhasználó szerint Cursor dolgozott a projekten. Az alábbi eredmények a forrásból és a Gitből ellenőrizhetők; egy commitüzenet önmagában nem bizonyítja, melyik asszisztens írta a kódot.

- `3f6af25`: terméktörzs, kategóriák, mértékegységek, termékadatok, CSV és InvenTree-import.
- `85c22c2`: modulok szerint csoportosított menü, beállítások alatti oldalak.
- `384ae49`, `c4059e4`, `7b74131`: partnerdosszié, bejövő számlák, lenyitható tételsorok és időszakszűrés.
- `c6d8f08`: modulok kikapcsolása és demócégek adatainak ürítése.
- `e9157b3`: bejövő számlák kézi rögzítése, Excel/CSV/NAV XML import, termékpárosítás és besorolás.
- `8efe6f4`: cégenként tárolt NAV-technikai felhasználó; Windows-felhasználóhoz kötött titkosított titoktárolás.
- `6c0f91c`: beágyazott nézetek görgetésének javítása.
- `b90f1bf`: listarendezés, szűrés és oszlopok testreszabása.
- `96276b6`: címsor, oszlopfejlécek és dátumválasztó témázása.

### Ami még nincs kész

- Valódi készletmozgási napló és készletegyenleg. A „Bevételezve” jelenleg számlabesorolás, nem készletnövelés.
- Élő NAV Online Számla lekérdezés. A jelenlegi NAV-import helyi XML-t olvas; a kapcsolati adatok tárolása már elkészült.
- PDF-tételolvasás; eladás, pénzügyi kiegyenlítés, ajánlat, díjbekérő és rendeléskezelés.
- Programozás nélküli drag-and-drop felületszerkesztő. A skinek és oszlopbeállítások ennek csak részleges előzményei.
- Üzemi előfizetési/aktiválási kiszolgáló, automatikus engedélymegújítás, visszavonás és teljes kereskedelmi licencéletciklus.
- Teljes körű cégmentés/visszaállítás és telepítési/frissítési munkafolyamat.

## Ellenőrzési eredmények

A `96276b6` commitból külön ideiglenes forrásmásolat készült. A fordítás és a tesztek ebben futottak, saját mesterséges adatkönyvtárakkal. Az aktív projekt `bin`/`obj` fájljai és a valódi `.local` cégadatai nem voltak tesztcélpontok. A meglévő SDK-t használtuk.

- Release fordítás: sikeres.
- Core/modul/Host tesztfuttató: **124/124 sikeres**.
- WPF görgetési teszt: **wheel-ok**.
- Három külön auditpróba az eredeti modulokat hívta; ezek az alábbi H01–H03 hibákat reprodukálták. A próbaprogram csak az ideiglenes másolatban készült, nem került az alkalmazás forrásába.
- Az eredeti tesztfuttató sikeressége mellett is fennállnak a lent leírt hibák. Nem történt élő InvenTree- vagy NAV-hívás, teljes kézi felületi átvétel, illetve üzemi licenckiszolgáló-vizsgálat.
- A README-ben szereplő 82/82 tesztadat elavult.

## Nyitott hibák és javítási feltételek

P1: üzleti használat előtt javítandó. P2: stabilizálás során javítandó. Minden feladat felelőse egyelőre kiosztatlan, kivéve ha Cursor később itt vállalja.

### H01 – P1 – Az import minden sort 27%-os áfával ment

- Bizonyíték: `src/Modules/PcImpressz.Modules.Receiving/ReceivingRepository.cs:54`. Az `ImportedLine` nem hordoz áfakulcsot, a NAV-olvasó sem őrzi meg azt.
- Reprodukció: XML-ben nettó 100 és 5% áfa; tárolva 27% és bruttó 127, a bemeneti adatok szerinti 105 helyett.
- Következmény: hibás számlaösszeg és partnerösszesítés. A pénznem sincs megőrizve, a dosszié minden összeget Ft-ként jelenít meg.
- Javítás: az import tartsa meg a támogatott áfakezelést, mértékegységet és pénznemet. Nem támogatott értéknél egyértelműen utasítsa el a fájlt, ne találjon ki adatot. CSV-nél kérjen explicit értéket/ellenőrzést, ha hiányzik. Az importált eredeti összegek és az újraszámítás eltérése legyen ellenőrizhető.
- Elfogadás: 0/5/18/27% és eltérő soráfák tesztjei; nem támogatott adózási eset és deviza felismerése; ismert bruttó összeg ellenőrzése.

### H02 – P1 – Az időszakszűrés másképp számolja a beszerzést

- Bizonyíték: `src/Modules/PcImpressz.Modules.Partners/PartnerDossier.cs:70` és `:159`.
- A `FromReceipts` csak a „Bevételezve” számlákat összegzi beszerzésként; a `Within` az időszak összes számláját, állapottól függetlenül.
- Reprodukció: egy bevételezett számla bruttó 127, egy költségszámla 254 ugyanabban az időszakban. Szűrés nélkül beszerzés 127, ugyanazt az időszakot kijelölve 381.
- Javítás: közös, belső állapotkódokra épülő összesítési szabály; a listában szereplő számlák és a beszerzésbe beszámított számlák köre legyen külön kezelve. A gyakori termékekre ugyanez vonatkozik.
- Elfogadás: ugyanazt az időszakot lefedő szűrő nem változtatja meg az összeget; új/függő/költség/bevételezett vegyes teszt.

### H03 – P1 – A terméknév önmagában szolgáltatássá minősíthet készletes árut

- Bizonyíték: `src/Modules/PcImpressz.Modules.Receiving/ReceivingData.cs:250`, `:273`, `:218`.
- A részszavas névheurisztika a cikkszám-párosítás előtt fut; a szolgáltatásnak jelölt sor termékazonosító nélkül lezárható.
- Reprodukció: „Díjnyertes egér”, `MOUSE-1` cikkszám, vonalkód nélkül → `szolgaltatas`, termékazonosító nélkül is „Bevételezve” állapotba tehető.
- Javítás: a szöveg csak javasoljon szolgáltatást. Az állapotot a felhasználó explicit döntése vagy a törzs típusa adja. A meglévő azonosító szerinti találat ne vesszen el egy névrészlet miatt.
- Elfogadás: „díj”, „szállítás”, „csomagol” szórészt tartalmazó árumegnevezések ne kerülhessék meg észrevétlenül a termékpárosítást.

### H04 – P2 – Az ismeretlen fizetettség „nincs fizetve” lesz

- Forrásból igazolt: `src/Modules/PcImpressz.Modules.Partners/PartnerDossier.cs:156` minden beolvasott számlához `Paid=false` értéket ad, `:186` ezt „nincs fizetve” szöveggé alakítja.
- Pénzügyi adatforrás még nincs, ezért ez nem igazolt tartozás.
- Javítás/elfogadás: ismeretlen/részben fizetett/fizetett/nyitott állapot különböztethető meg; adatforrás hiányában „fizetettség nem ismert” vagy nincs ilyen kijelzés.

### H05 – P2 – Az XLSX méretkorlátja csak a tömörített fájlra érvényes

- Forrásból igazolt: `src/Modules/PcImpressz.Modules.Receiving/ReceivingImport.cs:149`, `:157`, `:173`. Az XML teljesen memóriába töltődik; nincs külön kibontott méret-, cella- vagy sorlimit. Terhelési próbát nem futtattunk.
- Javítás: feldolgozott bájtok, XML-karakterek, sorok, oszlopok és megosztott szövegek korlátozása. A felület a teljes fájl beolvasása előtt is ellenőrizze a méretet. Az időkorlát önmagában nem állítja le a szinkron XML-feldolgozást.
- Elfogadás: kis tömörített, de túl nagy kibontott fájl biztonságos elutasítása; egyértelmű hibaüzenet és működő Host a hiba után.

### H06 – P2 – A mentés átlépheti a következő olvasás méretkorlátját

- Forrásból igazolt: `ReceivingRepository.cs:145` 64 MiB fölött elutasítja az adattárat, de `:166` mentéskor csak darabszámkorlát van, szerializált bájtkorlát nincs. Nagy adattárral nem reprodukáltuk.
- Javítás/elfogadás: a kész ideiglenes fájl méretének ellenőrzése az érvényes fájl lecserélése előtt; elutasított mentés után a korábbi adat olvasható marad.

## Szerkezeti értékelés: mit érdemes megtartani, mit egyszerűsítsünk?

**Jól működő irány:** külön Core és keret, külön modulprojektek, cégelkülönítés, szerveroldali parancsellenőrzés, aláírások, verziózott mentés, meglévő tesztek. A Receiving függősége a Partners és Catalog modulokra már szerepel a fejlesztői csomagmanifestben. Nem indokolt mindent újraírni.

**A legfontosabb szerkezeti adósság a modulhatár.** A `ReceivingData.cs:145` a másik modul JSON-fájlját olvassa, a `PartnerDossier.cs:130` ugyanezt teszi a bevételezéssel. Így az egyik modul belső tárolási sémája a másik modul rejtett szerződése lesz. Egy sémaátalakítás több helyen okozhat hibát. Javaslat: néhány kicsi, verziózott lekérdezési szerződés a Contracts rétegben (szállítókeresés, termékkeresés, partner bejövő bizonylatai), Host által közvetített hívással, kifejezett cégazonosítóval. A hívási úton a meglévő jogosultság/licenc/helyreállítási szabályok is érvényesüljenek. Ehhez most nem kell általános eseménybuszt vagy külön hálózati szolgáltatásokat építeni.

**A Shell kezd üzleti modulokat ismerni.** `ShellViewModel.Workspace.cs:58` külön Receiving nézetet választ; `ShellViewModel.Nav.cs` Receiving parancsokat tartalmaz; az általános `EntityPageViewModel.cs:96` Partners-specifikus. A külön bizonylatszerkesztő indokolt, a sok különleges eset később nehezen bővíthető. A keretnek továbbra sincs modul-DLL referenciája, de tartalmilag már kapcsolódik hozzájuk. Javaslat: kevés, jól meghatározott oldaltípus és képességleírás, regisztrált nézetgyárakkal; a NAV-beállításokat is modulbeállításként kezeljük. Ne próbáljunk minden üzleti képernyőt egyetlen általános űrlapba kényszeríteni.

**A teljes JSON-állapot minden mentéskor újraíródik.** Ez kis demóhoz kezelhető, készletmozgáshoz, bizonylatnaplóhoz és több összetartozó íráshoz már rossz alap. A következő nagy lépés előtt tervezzünk cégenként tranzakciós helyi adattárat, például SQLite-ot, sémafrissítéssel és visszaállítási tervvel. Külön modulok őrizzék meg az adataik tulajdonjogát, akkor is, ha közös a tranzakciós tároló. Ne vezessünk be többfelhasználós szervert, amíg nincs rá tényleges követelmény.

**A NAV titkosított tárolása jó kezdet.** A cégazonosítóhoz és Windows-felhasználóhoz kötés megfelelő jelenlegi célhoz. Költözéskor a kapcsolat újrabeállítását is tervezzük meg: a céges adatok mentése önmagában nem teszi hordozhatóvá a titkot. Ez még nem élő NAV-integráció és nem teljes biztonsági audit.

## Javasolt folytatási sorrend

1. **Stabilizálás:** H01–H03 és H04; célzott regressziós tesztek, importelőnézet/összegellenőrzés. A felület mondja egyértelműen, hogy az eddigi besorolás még nem készletmozgás. Felelős: egyeztetendő a Cursor aktuális feladatával.
2. **Adatbiztonság és modulhatárok:** H05–H06, cégmentés/visszaállítás kipróbált munkafolyamata, titkok költöztetési szabálya, fájlolvasás helyett modulok közötti lekérdezések. Dokumentáció és valós tesztszám frissítése.
3. **Készletmodul:** cég és raktár szerinti mozgásnapló; nyitókészlet, bevét, kiadás, korrekció. Egy bizonylat ismételt véglegesítése ne növeljen kétszer készletet. Megszakadt véglegesítés és visszafordítás kezelése. A számla lezárása és a hozzá tartozó készletmozgás legyen összhangban, tranzakciós alapon.
4. **Eladás:** partner és termék kiválasztása, eladási bizonylat és készletcsökkentés. A kedvezmény, ár, pénznem, áfa és kerekítés közös szabályainak meghatározása. A jelenlegi partner- és terméktörzs erre jó alap.
5. **Élő NAV és további importok:** külön integrációs réteg, tesztkörnyezet, ismételhető lekérés duplikáció nélkül, hibakezelés; a beolvasott számla ne váljon automatikusan készletmozgássá. PDF-feldolgozás csak ellenőrizhető előnézettel.
6. **Kereskedelmi kiadás:** aktiválás és engedélymegújítás; frissítő/telepítő és adatmentés, fejlesztői kulcsok nélkül kipróbált ügyfélcsomag, lejárati és helyreállítási forgatókönyvek.
7. **Felületszerkesztő:** először címkék, menüsorrend és láthatóság; utána panelek drag-and-drop elrendezése, alaphelyzet-visszaállítással. A felületszerkesztő ne módosíthassa az üzleti és licencellenőrzést. A megjelenési réteg előkészítése a fenti munkákkal párhuzamosan is folytatható.

## Időrendi napló

### 2026-10-20 – ChatGPT/Codex – állapotfelmérés

- Kiinduló commit: `96276b6`; az alkalmazáskód változatlan maradt.
- Áttekintve: történet, Core/Host modulhatárok, partnerdosszié, terméktörzs fő szerkezete, bevételezés/import, NAV-titoktárolás, Shell kapcsolódási pontok, tesztek és dokumentáció.
- Ellenőrzés: elkülönített Release build; 124/124 teszt; görgetés `wheel-ok`; H01–H03 külön reprodukálva.
- Új fájl: `KOZOS-MUNKANAPLO.md`. Nem készült alkalmazásjavítás vagy új kiadás; Cursor nem kapott automatikus üzenetet.
- Következő átadás: Cursor egészítse ki az aktív feladatát, majd az ütközésmentesen vállalt javításokat és teszteredményeket.

### Következő bejegyzés sablonja

- Dátum és idő:
- Felelős: Cursor / ChatGPT/Codex / felhasználó
- Feladatazonosító, induló commit:
- Cél és érintett fájlok:
- Elkészült változás, eredménycommit, ha van:
- Ellenőrzés és eredmény:
- Nyitva maradt kérdés / következő lépés:

### 2026-10-20 – ChatGPT/Codex – v0.4 megvalósítás

- A felhasználó jóváhagyása alapján H01–H06, céges SQLite-tranzakciók/migráció, cégmentés és visszaállítás, készlet- és eladásmodul, felületi bekötés, regressziós és valódi Host-tesztek készültek.
- Közös irány rögzítve; az aktuális átadási rész felülírja a korábbi audit nyitott állapotait.
- Ellenőrzés: 142/142, wheel-ok, 6/6 WPF render; build 0 hiba/0 figyelmeztetés.
- Frissített útmutató: docs/KESZLET-ELADAS-ES-MENTES.md; a korábbi modulútmutatók történeti jelölést kaptak.
- Visszaállítható helyi biztonsági mentés és jogosultsági adatköltöztetés készült.
- Commit nem készült; a módosítások és új forrásfájlok a munkafában vannak.

### 2026-10-21 – Codex – audit- és licencintegráció

- Kiindulás: 96276b6, megőrzött 0.4 munkafa, 11 helyi commit a GitHub main előtt; külön integrációs ág.
- Licenccsomag szelektíven integrálva, öt modul megtartva; InvenTree futásidejű kapcsolat kivezetve.
- Maintenance restore/platform hiba, adatlapzár, bezárási védelem, technikai logger és teszt-host javítva.
- Ellenőrzés: 133/133, wheel-ok, 6/6 render, WPF zárolási regresszió; 5 aktív modul és Active licenc.
- Részletes A01–A16 állapot és következő csomag: docs/AUDIT-INTEGRACIO-2026-10-21.md.

### 2026-10-21 – Codex – termék és készlet munkaterület, 0.4.1

- Kiinduló commit d59396c. A08 keresési állapot/export, A09 stabil termék- és partnerrendezés, A07 atomi célfájl-részfeladat javítva.
- MPN mező, kereső, saját készletszűrők; készletkezelés a terméktörzsben, kijelölt termékre szűrhető műveletek.
- A nagykeres kosár későbbi funkció; a mező- és adatkapcsolati terv dokumentálva, élő kapcsolat és rendelésküldés nincs.
- Ellenőrzés elkülönített forrásmásolatból: 139/139, wheel-ok, 9/9 WPF és késleltetett keresés/export/mentetlen művelet regresszió.
- Útmutató: docs/TERMEKTORZS-KESZLET-ES-NAGYKER.md.

### 2026-10-21 – Codex – külön terméklista és adatlap, 0.4.2

- Teljes szélességű terméklista, külön Termékadatlap fül; Új/szerkesztés/dupla kattintás, széles mezők és oszlopok.
- A fülváltás megőrzi a szerkesztést. A készlet a harmadik belső fül; a munkaterület példánya megmarad navigációkor.
- Az admin szerepkör és drag-and-drop/feliratszerkesztő még nincs kész. Következő munkacsomag: docs/ADMIN-FELULETSZERKESZTO.md.
- Ellenőrzés elkülönített másolatból: 139/139, wheel-ok, 12/12 WPF; listasélesség, mezőszélesség és mentetlen adat fülváltása tesztelve.

### 2026-10-21 – Codex – admin feliratszerkesztő, 0.4.3

- Cégenkénti első admin, admin/user fiókok, PBKDF2 jelszólenyomat, lejáró cég/SID-kötött session; szerkesztési jogosultság ellenőrzése a Hostban.
- Adatlapmezők húzása/Fel-Le sorrendje, felirat, fél-/teljes sor és opcionális láthatóság, előnézet. Verziózott SQL-mentés és audit, utolsó 10 előzmény, alap-/előző nézet visszaállítása.
- A szerepek egyelőre a felületszerkesztésre vonatkoznak. Offline admin-helyreállítás nincs a forrásban; külön jóváhagyásra vár.
- Ellenőrzés: 148/148, wheel-ok, 15/15 WPF; valódi IPC-s szerepkör/cégvédelem, mentés és hitelesítési adatok elkülönítése, UI-előnézet és adatmegőrzés.

## 2026-10-21 – 0.4.4
Bevételezés-visszafordítás és eladási kosár/készlet/export javítások; külön konkrét felhasználói engedéllyel offline admin-helyreállítás. 155/155 teszt, 21/21 WPF-render. Lásd docs/BEVETELEZES-ELADAS.md. Teljes dolgozói műveleti jogosultság és élő NAV/PDF továbbra is nyitott feladat.

## 2026-10-21 – 0.4.5
Céges alkalmazásbelépés minden üzleti művelethez; admin/raktáros/eladó/megtekintő; közös SID- és céghez kötött munkamenet; felhasználókezelés, tiltás, szerepkörváltás, saját jelszócsere; utolsó aktív admin védelme; üzleti szereplő a bejelentkezett névvel. A régi Host használatát az új felület elutasítja. A demóadat-törlés az aktuális demócégre szűkül. 158/158 teszt, 30/30 WPF-render. Helyi mentés visszaállított másolatán raktáros bevételezés → eladó eladás → készletvédelem → mindkét visszafordítás sikeres. A mentésben 0 termék és 1 partner volt; csak a másolatban létrehozott próba-termékkel ellenőriztük. Az eredeti adatbázis SHA-256 változatlan. Saját éles céges adat és felhasználói átvétel még szükséges. Útmutató: docs/BELEPES-ES-JOGOSULTSAGOK.md.

## 2026-10-21 – 0.4.6
Beállítások / Feltöltés mintaadatokkal: adminhoz és demócéghez kötött művelet, teljes modul- és licencellenőrzés, előzetes ellenőrzött mentés, egyetlen céges tranzakció, idempotens v2 jelző és v1 kompatibilitás. 20 termék gyártói cikkszámmal és tesztvonalkóddal; 5 vevő, 5 szállító; 4 kategória; 2 egység; 2 raktár; 8 bevételezés; 10 eladás. Az aláírt bevételezésmodul tartalmazza a feltöltő üzleti műveletét; a Host továbbra sem hivatkozik üzleti modulokra. A helyi demó feltöltve, meglévő partner megőrizve, előzetes mentéssel. 162/162 teszt és 33/33 WPF-render. Útmutató: docs/DEMO-MINTAADATOK.md.

## 2026-10-21 – 0.4.7
Eladás pénztári újratervezése: egysoros élő keresés, vonalkód automatikus kosár, közvetlen sorár/mennyiség/áfa és soronkénti törlés; készpénz/kártya; számlaigényhez partnerkereső és korlátozott eladói vevőfelvétel, NAV queryTaxpayer aláírt lekérdezéssel és automatikus mentéssel. Termék alapár/áfa, fizetési mód és eladáskori vevőadatok tárolása; régi adatok kompatibilitása; válaszvesztés olvasásos visszaellenőrzése és változatlan újrapróbálás; régi Host/modul nem fogadhat metaadat nélküli pénztári mentést. 168/168 teszt, 39/39 WPF-render. NAV élő kulcsos próba és adóügyi számlakiállítás további feladat; a felhasználó adatbázisában nem rögzítettünk valódi eladást. Útmutató: docs/PENZTARI-ELADAS.md.

2026-10-21 · 0.4.8: készpénzes végösszeg kerekítése (5 Ft), eltérés/fizetendő tárolása és exportja; 0–4 megjelenített pénztizedes, alap 0. NAV-mentési napló: NotSupportedException, a modulbetöltő blokkolta a DPAPI natív hívását. Csak crypt32.dll/kernel32.dll engedélyezve abszolút Windows rendszermappából. Aláírt modulon NAV-mentés/visszaolvasás/üres titkok megtartása tesztelve szintetikus adatokkal. Éles NAV-hitelesítés nem történt. 169 üzleti teszt és 39 felületi render.

2026-10-21 · 0.4.9: Cursor d4a9151 partner/kapcsolattartó/aktivitás fejlesztése megtartva, teljes tesztkészlettel ellenőrizve. Normál és codex-build köztes fájlok kizárása projektszinten is. Eladási előzmények: budapesti nap szerinti szűrés (CSV is), automatikus aszinkron tételbetöltés, részleges visszavét eredeti sorhoz kötve. Negatív kosár, változatlan eredeti ár/áfa, mennyiségi korlát, kumulált fillérpontos nettó/áfa-visszatérítés. Visszavét és új eladás külön kosárban, a készlet csak véglegesítéskor nő, ugyanazon SQL-tranzakcióban. Ismétlésvédelem; részleges visszavét után a régi teljes reverse tiltva. 172 üzleti teszt és 39 WPF-render sikeres; valódi aláírt modulon eladói visszavét és ismétlés is ellenőrizve. A tesztek elkülönített cégadatokkal futottak; a fejlesztés nem írt az eredeti cégadatbázisokba.

2026-10-21 · 0.4.10: a Cursor bdf09a3 fordítási javítása megmaradt. Partnerlista teljes szélességgel, alatta szerkesztő; alapmezők és lenyitható bővített mezők. partners.taxpayer csak partners.write jogosultsággal és modullicencekkel, csak olvasó NAV-kérés, munkamenet újbóli ellenőrzés a válasz után; draftkitöltés, nincs automatikus partnermentés. Nem válasz adatlapváltáskor és kilépéskor megőrzi az adatokat; tulajdonosablakos alapértelmezett Nem és reentráns Closing védelem. A korábbi kilépés pontos oka nem bizonyított. 172 üzleti/Host teszt és 45 WPF-render, célzott NAV-draft-, partnerkiválasztás-, új adatlap-, elvetés- és kilépési tesztek. A tesztek elkülönített adatokon futottak.

2026-10-21 · 0.4.11: adószám első helyen, mellette kompakt Lekérés; rövid NAV-cégnév elsőbbsége, teljes név visszaesés. Helyben tárolt, alapból bekapcsolt Név formázás: csak új partner NAV-ból érkezett csupa nagybetűs neve/települése/címe, vegyes írásmód megőrzése. Beállítások öt modulcsoportban. 173 üzleti teszt, 48 WPF-render; rövid/hiányzó/üres NAV-név, formázás és kikapcsolás/persistencia, lekérősor-geometria és tényleges beállítási kapcsoló ellenőrizve. Valódi NAV-hitelesítés továbbra sem történt.

2026-10-21 · 0.4.12: kompakt általános adatlapok, partnerlista alatt kétpaneles szerkesztő/áttekintő, 1000 egység alatt egymás alá rendezve. Adminnak automatikus, legfeljebb négykártyás dossziéösszesítő; mentett partneradatok külön a drafttól. Késői válasz és jogosultságvesztés védelme. Elsődleges Mentés/Elvetés, lenyitható további műveletek. Terméktörzs széles mezői és meglévő mentetlenadat-védelem megtartva. 173 üzleti teszt, 54 WPF-render sikeres. Tesztek elkülönített adatokkal.

2026-10-21 · 0.4.13: felhasználó kifejezett kérésére helyi, SID-hez kötött és visszavonható fejlesztői adminmunkamenet, automatikus UI-belépés; alapértelmezésben nincs engedélyezve, a kapcsoló helyi .local fájl, nincs fiók/jelszómódosítás. Terméktörzs egységes lista/adatlap/összesítő, lenyitható készletpanel; adminvédett, tisztán olvasó termékforgalmi összesítés. 174 üzleti teszt és 54 WPF-render: fejlesztői mód alapértelmezett tiltása/engedélyezése/visszavonása, Windows- és cégkötés, UI automatikus belépés, termékösszesítő jogosultság és visszavét utáni készlet/forgalom egyezés ellenőrizve.

## 2026-10-21 – 0.4.14 NAV bejövő számlák
Dátumszűrt, lapozható NAV-számlalista és kiválasztott számla biztonságos letöltése; szállítóegyeztetés, meglévő termékegyeztetés és besorolás. Ismételt import készletduplázás nélkül. Hálózati művelet után ismételt jogosultság- és licencellenőrzés. 177/177 üzleti és 57/57 UI ellenőrzés sikeres. Élő céges NAV-próba még nem történt. Korlátok és használat: docs/NAV-SZAMLALEKERES.md.
