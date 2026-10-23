# PC-Impressz Windows Edition – architektúra és megvalósítási terv

Történeti induló terv, 2026. október 20-i állapot. A megvalósítás azóta elkészült alapjait, a saját SQLite-tárolást és a következő munkacsomagokat a [2026. október 23-án frissített fejlesztési ütemterv](FEJLESZTESI-UTEMTERV.md) tartalmazza. Az alábbi eredeti javaslatok nem mindenütt egyeznek a jelenlegi megvalósítással.

## 1. Vezetői döntési javaslat

A termék három külön rétegből álljon: stabil technikai **Core**, egységes Windows **Shell**, és külön telepíthető, külön licencelhető **üzleti modulok**. A Core ne tartalmazzon raktári vagy kereskedelmi üzleti logikát. A Shell üzleti modul nélkül is induljon el, és biztosítsa a beállításokat, modulkezelést, licencaktiválást, diagnosztikát, valamint az adatokhoz való alapvető hozzáférés és mentés keretét.

A licencmodell szervezethez és telepítéshez rendelt, digitálisan aláírt jogosultság legyen. A szériaszám az aktiválási folyamat belépője; az alkalmazás működését az aláírt licenc és a rövid élettartamú működési engedély határozza meg. A privát aláírókulcs kizárólag a kiadó infrastruktúráján legyen.

Javasolt technológiai alap új Windows-termékhez: **C# / .NET 10 LTS, WPF és MVVM**, külön ASP.NET Core alkalmazásszolgáltatással. Ez javaslat, nem a meglévő projektből kimutatott technológia. A .NET 10 támogatását a Microsoft 2028 novemberéig jelöli; az operációs rendszer pontos támogatási mátrixát a prototípus előtt kell rögzíteni. [Microsoft támogatási szabályzat](https://dotnet.microsoft.com/en-us/platform/support/policy), [WPF áttekintés](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/).

Az első kiadásnál az InvenTree maradjon az üzleti adatok hiteles forrása, a PC-Impressz egy elkülönített adapteren keresztül használja az API-ját. Saját adatmodellre költözés külön későbbi döntés legyen. Az üzleti cél a moduláris Windows-termék; a teljes adatbázis újraírása nem előfeltétele.

## 2. A jelenlegi állapot felmérése

### Közvetlenül ellenőrzött tények

- A vizsgált munkamappa: `C:\Users\Rock\Documents\ChatGPT\Pc-Impressz raktár windows edition`.
- A felmérés kezdetén a mappa **üres volt, rejtett fájlokat is beleértve**. Nem volt forráskód, Git-mappa, megoldásfájl, csomagleíró, teszt, telepítő vagy helyi `AGENTS.md`.
- A „Projektindítás és audit” beszélgetés csatolmányaként elérhető volt az `inventree-schema.sql`, 259 953 bájt méretben. A fejléc PostgreSQL 17.10 forrást és ugyanilyen `pg_dump` verziót jelez.
- A séma **115 táblát, 198 idegen kulcsot és 244 külön CREATE INDEX utasítást** tartalmaz. Az utóbbi nem a megszorításokkal együtt számított összes fizikai index száma.
- Az állományban nincs `COPY` adatblokk; ebből termék-, készlet- és felhasználói rekordszám nem állapítható meg.
- A vizsgált séma SHA-256 azonosítója: `944E633408F23EE555832EBAC3BAA27BF14A72018B9D9DF49003F1D3553CA784`.

A csatolmány ideiglenes helyről volt olvasható; a fenti azonosító segít később azonosítani az auditált példányt. A csatolmányt és az adatbázist nem módosítottam.

### A séma által igazolt funkcionális területek

- Terméktörzs, kategóriák, paraméterek és darabjegyzék: `part_*`; például `part_part` a 2429. sortól.
- Készlet, raktárhely, sorozatszám, tétel és mozgáskövetés: `stock_*`; `stock_stockitem` a 3260., `stock_stocklocation` a 3390. sortól.
- Beszállítók, vevők, gyártók, kapcsolatok: `company_*`.
- Beszerzés, értékesítés, visszáru, áthelyezési rendelések: `order_*`; `order_purchaseorder` az 1714. sortól.
- Gyártás és összeállítás: `build_*`; `build_build` a 242. sortól.
- Riportok, címkesablonok, csatolmányok, importálás, értesítések és webhookok: `report_*`, `common_*`, `importer_*`.
- Felhasználók, csoportok, API-tokenek, többtényezős hitelesítés: `auth_*`, `users_*`, `mfa_*`, `otp_*`.
- InvenTree-bővítménykonfiguráció: `plugin_pluginconfig`, `plugin_pluginsetting`, `plugin_pluginusersetting`.

A bővítménytáblák megléte nem bizonyít külön értékesíthető PC-Impressz-modulrendszert. A `users_apitoken` lejárati mezője API-hozzáféréshez tartozik, és nem helyettesít terméklicencet. A készlettáblák beszerzési, értékesítési és gyártási kapcsolatai miatt a modulhatárok nem képezhetők le egyszerűen táblacsoportok különválasztásával.

### Csak a korábbi beszélgetésben közölt állapot

A felhasználó által bemásolt kimenet InvenTree-szervert és workert, PostgreSQL 17-et, Redis 7-et és Caddy proxyt mutatott Dockerben. Az InvenTree képcímkéje `stable` volt; ez nem pontos alkalmazásverzió. A beszélgetés szerint készült egy körülbelül 792 KB-os adatbázismentés, amelynek objektumlistája olvasható volt.

Ezeket az élő szerveren nem ellenőriztem. Az objektumlista olvashatósága nem bizonyít sikeres visszaállíthatóságot. A teljes mentés, médiaállományok, konfiguráció, telepített bővítmények, pontos InvenTree-verzió és az előző asszisztensi válaszok érdemi szövege nem állt rendelkezésre.

**Következmény:** ez a dokumentum bizonyítékokra épülő induló architektúraterv; meglévő Windows-kód refaktorálási terve csak a források beérkezése után pontosítható.

## 3. A komponensek felelőssége

### Core – technikai mag

Feladata a modulok közös működési alapja: konfiguráció, naplózás, identitás- és jogosultságabsztrakció, tranzakciós keret, parancs- és eseményszerződések, időszolgáltatás, licencellenőrzés és modulbetöltési infrastruktúra. A Core nem ismerheti például a készletmozgás vagy a számlázás konkrét megvalósítását.

Az adat-hozzáférési absztrakció lehet közös, de az üzleti entitások és adattárak az adott modulhoz tartozzanak. Ne alakuljon ki minden üzleti adatot tartalmazó közös „Core adatmodell”. A termék-, partner- és raktártörzs is üzleti modul.

### Contracts – stabil bővítési szerződések

Külön, kis függőségű szerződéscsomag adja a modul-életciklust, képességek azonosítóit, parancsokat, eseményeket, adatátviteli objektumokat, valamint a Shell navigációs és nézetregisztrációs pontjait. Ez a Core nyilvános határfelülete, önálló verziózással.

Az általános szerződések UI-függetlenek. A WPF-hez szükséges nézetszerződések külön `Shell.Contracts` csomagba kerüljenek, hogy a háttérszolgáltatásnak ne legyen WPF-függősége.

### Shell – Windows-keret

Tartalmazza a főablakot, menüt, munkaterületet, keresés keretét, értesítési felületet, témát, nyelvet, bejelentkezést, modulkezelőt és licencképernyőt. A menüpontokat és nézeteket a modulok regisztrálják. A licencstátusz a felületen is megjelenik, de egy elrejtett menüpont önmagában nem jelent védelmet.

A Shell is használja a szolgáltatási műveletek jogosultságellenőrzését. Ne kerüljenek bele közvetlen adatbázis-lekérdezések vagy megkerülhető üzleti műveletek.

### Application Host – üzleti végrehajtás

Egy felülettől független folyamat futtatja a modulok üzleti műveleteit, háttérfeladatait és adaptereit. Egygépes telepítésnél helyi, automatikusan kezelt Windows-szolgáltatás; többgépes környezetben közös vállalati szolgáltatás. Az első megvalósítás egyetlen moduláris szolgáltatás legyen, ne modulonkénti mikroszolgáltatás.

A helyi végpont csak helyben legyen elérhető, OS-szinten korlátozott hozzáféréssel és hitelesítéssel; hálózati telepítésnél TLS és felhasználói hitelesítés kell. A szolgáltatás minden üzleti belépési ponton ellenőrizze a licencet és a felhasználói jogosultságot.

### Külön kiadói infrastruktúra

A licenckiszolgáló, az aktiválás, a fizetési integráció, a kiadói adminisztráció és az aláírás külön rendszer. Egy ügyfél telepítése sem tartalmazhat olyan kulcsot vagy adminisztrációs funkciót, amellyel saját licencet készíthet.

## 4. Függőségek és telepítési modell

Logikai működési lánc:

**Windows Shell → hitelesített Application Host → modulok → szerződések és Core-szolgáltatások → adatadapterek.**

Külön ág: **Application Host → licencellenőrző → kiadói licencszolgáltatás**. Az InvenTree-integráció a modulok és az InvenTree API közötti adapter, nem Shell-funkció.

Fordítási szabályok:

- A Core és a Contracts nem függhet a Shelltől vagy konkrét üzleti modultól.
- A modulok Core/Contracts felületeket használhatnak; más modul belső osztályait és tábláit nem érhetik el.
- Modulok között célzott nyilvános szerződés és esemény használható, körkörös függés nélkül.
- A Shell konkrét modulhivatkozások helyett regisztrációkat használjon. A modul UI-csomagja függhet a Shell.Contracts csomagtól.
- A külső rendszerek függőségeit adapterek rejtsék el a modulok elől.

Javasolt későbbi megoldásstruktúra: `src/Contracts`, `src/Core`, `src/Shell.Contracts`, `src/Shell.Windows`, `src/ApplicationHost`, `src/Modules/<modul>/Application`, `Domain`, `Infrastructure`, `Presentation.Wpf`, továbbá `src/Adapters/InvenTree` és `tests`. A kiadói licencszerver külön megoldásban vagy külön repóban legyen. Ezek tervezett útvonalak; nem jöttek létre.

Saját, ellenőrzött moduloknál külön `AssemblyLoadContext` és függőségfeloldás használható. Ez nem biztonsági homokozó: a Microsoft dokumentációja szerint nem nyújt biztonsági elkülönítést. Harmadik féltől származó, nem megbízható bővítmény külön, korlátozott OS-jogú folyamatot és szűk IPC-felületet igényel. [Microsoft: AssemblyLoadContext](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.loader.assemblyloadcontext).

## 5. Javasolt modulhatárok és értékesítési csomagok

Az alábbi felosztás javaslat. Az üzleti követelmények és a tényleges használat felmérése finomítja.

- **Catalog:** termékek, kategóriák, mértékegységek, alapparaméterek. Képességpélda: `catalog.write`.
- **Partners:** vevők, szállítók, gyártók és kapcsolattartók. Képességpélda: `partners.write`.
- **Inventory:** raktárhelyek, tételek, bevét, kiadás, áthelyezés, leltár; a Catalog függvénye. Képességpéldák: `inventory.receive`, `inventory.issue`.
- **Purchasing:** beszerzési rendelések és beérkeztetési folyamatok; Catalog, Partners és Inventory szerződésekre épül.
- **Sales:** értékesítési rendelések, foglalás és kiszállítás; Catalog, Partners és Inventory szükséges.
- **Manufacturing:** darabjegyzék, összeállítás és anyagfelhasználás; Catalog és Inventory szükséges.
- **Labels:** címketervezés, nyomtatás és eszközintegráció. A szükséges adatokat nyilvános lekérdezési szerződésen kapja.
- **Reporting.Pro:** összetett riportok és elemzések. A lejárat utáni alapadat-export nem kerülhet ebbe a fizetős modulba.
- **WebshopSync:** külső webshopadapterek és szinkronfolyamatok. A célrendszertől függően Catalog, Inventory vagy Sales képességeket igényel.

A „Raktár alapcsomag” például Catalog + Inventory lehet, de ettől a két modul technikailag külön marad. A licencszerver a csomagot konkrét modul- és képességjogokra bontsa. Egy szükséges függőség nem kaphat automatikusan ingyenes jogosultságot: a csomag tartalmazza azt, vagy az aktiválás jelezze a hiányt.

A licenc a termék használati jogát, a szerepkör pedig a felhasználó műveleti jogát adja. A végső engedély: **érvényes modullicenc ÉS megfelelő felhasználói jogosultság ÉS üzleti feltételek teljesülése**.

## 6. Modulcsomag és életciklus

Egy modulnak stabil azonosítója, verziója, belépési pontja, támogatott Contracts-verziótartománya, függőséglistája, igényelt képességei, migrációs verziója és fájlkivonat-listája legyen. A csomagjegyzék és a fájlok integritását kiadói aláírás védje. A csomagaláírás kulcsa különbözzön a licencaláírás kulcsától.

Indulási sorrend: csomagellenőrzés → kompatibilitás- és függőségvizsgálat → licencértékelés → mentéshez kötött szükséges migráció → szolgáltatásregisztráció → UI-regisztráció → aktív állapot. Hibás csomag ne hajthasson végre inicializáló kódot.

Állapotok: telepítve, inkompatibilis, hiányzó függőség, licencre vár, aktív, korlátozott, letiltott, hibás. A modulkezelő emberi nyelven mutassa az okot és a teendőt.

Első verzióban modultelepítés és verzióváltás után szabályozott újraindítás legyen. A futás közbeni általános DLL-csere és eltávolítás elhagyható. A licenc lejárata viszont újraindítás nélkül érvényesüljön: új üzleti parancs már ne induljon, a megkezdett rövid adatbázis-tranzakció pedig atomi módon fejeződjön be. Hosszú importok és háttérfeladatok biztonságos ellenőrzési pontokon álljanak meg.

## 7. Digitálisan aláírt licenc

### Formátum és kulcskezelés

Javaslat: szabványos **JWS Compact**, **ES256** algoritmussal, ellenőrzött könyvtárral. Az alkalmazás rögzített engedélyezett algoritmust, kibocsátót, célközönséget és tokentípust ellenőrizzen; `none` és algoritmuscsere nem fogadható el. A `kid` kizárólag előre megbízhatónak tekintett kulcsot választhat ki; a token által megadott tetszőleges URL-ről nem tölthető le kulcs. [JWS szabvány](https://www.rfc-editor.org/rfc/rfc7515.html), [JWT biztonsági gyakorlat](https://www.rfc-editor.org/rfc/rfc8725.html).

A JWS aláír, nem titkosít. A licencbe személyes vagy üzleti titok ne kerüljön. A kliens a pontos aláírt bájtsorozatot ellenőrizze; ne újraszerializált JSON-on dolgozzon. Ismeretlen kritikus mező vagy nem támogatott formátumverzió elutasítandó.

A privát kulcs KMS/HSM vagy elkülönített aláírószolgáltatás mögött legyen, hozzáférésnaplóval és mentési eljárással. A kliensbe csak nyilvános kulcsok kerüljenek. Kulcsváltásnál átmenetileg a régi és új kulcs is megbízható lehet; kompromittált kulcs tiltása a következő hiteles frissítéskor érvényesülhet.

### Két külön aláírt dokumentum

**Jogosultságlicenc:** `license_id`, `schema_version`, `iss`, `aud`, `iat`, `nbf`, szervezeti azonosító, termékazonosító, licenctípus, modulonkénti képességek, modulonkénti előfizetési lejárat, felhasználó-/telepítési korlátok, offline szabályzat, monoton növekvő jogosultságverzió. A dokumentum `exp` mezője a dokumentum élettartamát korlátozza; nem helyettesíti az egyes modulok lejáratát.

**Működési engedély, azaz lease:** rövid életű aláírt dokumentum, amely az adott licencazonosítóhoz és jogosultságverzióhoz, ügyfélszervezethez, telepítéshez és – ha alkalmazható – regisztrált gépkulcshoz kötődik. Tartalmazza a szerver által hitelesített időt, a kibocsátást, lejáratot és az engedélyezett modulok időkorlátait. Másik licenc lease-e vagy korábbi jogosultságverzió nem használható új jogok igazolására.

A felhasználók száma kezdetben szervezethez rendelt név szerinti aktív fiókokra vonatkozzon. A telepítések száma a regisztrált Application Host példányokat jelentse, ne a Shell ablakait. Globális, egyidejű felhasználószám-korlát csak központi online koordinációval vezethető be megbízhatóan; az első kiadásban ne keveredjen a név szerinti modellel.

### Aktiválás és megújítás

1. A vevő megadja a szériaszámot vagy aktiválókódot. A szerver sebességkorlátozással, naplózva ellenőrzi a kódot és a telepítési keretet.
2. A Host létrehoz egy telepítési azonosítót és gépkulcspárt; a szerverhez regisztrálja a nyilvános kulcsot. A titkos kulcs Windows által védett tárolóban maradjon, lehetőség szerint TPM-támogatással.
3. A szerver visszaadja a jogosultságlicencet, a lease-t és visszavonható megújítási hitelesítő adatot. Az aktiválókód önmagában ne legyen tartós megújítási titok.
4. A kliens helyben ellenőrzi az aláírásokat és a kötéseket, majd időszakosan, szórt időzítéssel megújítja a lease-t. A licencszerver nem kell minden készletművelethez.
5. A szerver csak ellenőrzött, deduplikált fizetési esemény után hosszabbíthat. A szolgáltatói előfizetési állapot és a licencjogosultság külön, összekapcsolt nyilvántartás legyen.

A manuális offline aktiválás kérésfájl–válaszfájl folyamattal történjen. A válasz a kérés egyszer használatos azonosítójához, gépkulcsához és előre megadott időkorláthoz kötött, aláírt engedély legyen. Egy közös jelszóval vagy szerkeszthető dátumfájllal történő feloldás nem megfelelő.

## 8. Időkorlát, internetkimaradás és lejárat

Kezdeti, jóváhagyandó üzleti paraméterek: megújítás 12 óránként, legfeljebb 7 napos offline lease, 14 napos próba. A próba is kiadói aláírású és telepítési nyilvántartáshoz kötött; újratelepítéssel ne automatikusan induljon újra. A visszaélések teljes kizárása itt sem garantálható.

Egy modul írhatóságának időbeli felső határa a **modul előfizetési lejárata és a lease lejárata közül a korábbi időpont**, minden egyéb ellenőrzés teljesülése mellett. A megújítások nem tolhatják túl a lease-t a megfizetett időszakon. Nincs külön, automatikus 7 napos hosszabbítás a lejárt előfizetés végén. Fizetési türelmi idő csak külön, szerver által aláírt engedélyként adható.

- **Aktív, online:** a jogosított műveletek elérhetők.
- **Aktív, átmenetileg offline:** az aláírt lease határidejéig működik, jól látható következő ellenőrzési határidővel.
- **Lease lejárt, előfizetés még élhet:** új fizetős írási művelet nem indul; megújítás vagy offline engedély szükséges.
- **Modul előfizetése lejárt:** az adott modul írása és automatizmusai leállnak, más érvényes modulok tovább működnek.
- **Hibás aláírás, rossz gépkötés vagy észlelt visszavonás:** nem adható új üzleti írási jog; a helyreállítás, diagnosztika és alapadat-export elérhető marad jogosult felhasználónak.
- **Óra-visszaállítás gyanúja:** új online időellenőrzés vagy aláírt offline feloldás szükséges a fizetős íráshoz.

A lejárat minden esetben adatmegőrző legyen: meglévő adatok megtekintése és szabványos exportja a megfelelő felhasználói jogosultsággal maradjon elérhető. Ezt a modulok kötelező, minimális, lejárat után is engedélyezett adatolvasási/exportálási felülete biztosítsa; a fizetős speciális riport és automatizmus nem tartozik ide. Modul eltávolítása előtt teljes export/mentés kell, mert a Shell önmagában nem ismeri az üzleti adatformátumokat.

Minden időpont UTC-ben tárolandó; a felület Budapest szerinti helyi időt mutathat. Időablak: `nbf` befoglaló, lejárat kizáró határ. Az óraeltérés tűrése legyen kicsi és dokumentált, például 2 perc; ne rejtett előfizetés-hosszabbítás.

Utolsó szerveridő, legnagyobb korábban látott idő és jogosultságverzió helyi védett tárolóban tartható; folyamaton belül monoton időmérés használható. Windows DPAPI alkalmas helyi titkok védelmére, de nem bizonyítja az idő hitelességét. [Microsoft DPAPI útmutató](https://learn.microsoft.com/en-us/dotnet/standard/security/how-to-use-data-protection).

**Védelmi határ:** helyi rendszergazda, módosított bináris vagy visszaállított virtuálisgép-pillanatkép ellen a teljesen offline, feltörhetetlen lejárat nem garantálható. Az azonnali visszavonás offline szintén nem garantálható; a lehetséges késleltetést a még érvényes lease határozza meg. Rövidebb lease erősebb központi kontrollt, de nagyobb internetfüggőséget ad. Az aláírt időkorlát és a tényleges manipuláció elleni védelem eltérő tulajdonság.

## 9. Licencszerver, visszavonás és üzemeltetés

A kiadói szolgáltatás nyilvántartásai: ügyfélszervezet, termék/modulkatalógus, előfizetés, jogosultságverzió, telepítés és gépkulcs, aktiválókód, kiadott lease, visszavonási esemény és adminisztrációs audit. A megújítási titkokat ne nyílt szövegben tárolja.

Fő műveletek: aktiválás, hitelesített lease-megújítás, deaktiválás/gépcsere, offline engedély kiadása, valamint külön adminisztrációs jogosultsággal licencmódosítás és visszavonás. A gépcsere a régi telepítést vonja vissza, és ismert offline késleltetéssel működjön. A szerver kérésismétlésnél ne foglaljon többször telepítési helyet.

A hálózati hiba nem lehet azonos az aláírt visszavonással. Már hitelesen megismert visszavonás után egy régi fájl visszamásolása nem oldhatja fel a tiltást; az ismert legmagasabb verziót és visszavonást meg kell őrizni. Helyi tároló teljes visszaállításánál érvényesek az offline védelem korlátai.

Kell monitorozás az aktiválásra, megújítási hibákra, aláírási hibákra és fizetési események feldolgozására. A licencszerver mentési, kulcsváltási és kiesési eljárását kiadás előtt próbálni kell. A naplókban ne szerepeljen teljes aktiválókód, megújítási titok vagy InvenTree API-token.

## 10. Adatkezelés és InvenTree-integráció

Első fázisban a Windows-rendszer az InvenTree dokumentált REST API-ján dolgozzon. Az adapter a tényleges szerver verziójához és képességeihez igazodjon; a mindenkori online `stable` dokumentáció önmagában nem bizonyít kompatibilitást az ügyfél példányával. [InvenTree API](https://docs.inventree.org/en/stable/api/).

Ne írjunk közvetlenül az InvenTree PostgreSQL-tábláiba: a készlet-, hierarchia-, előzmény- és rendelési szabályok megkerülése adatellentmondást okozhat. A séma itt a felmérés és későbbi migráció bizonyítéka. A tokenek legkisebb szükséges jogot kapjanak, és az Application Host tárolja őket védetten.

Fontos üzleti határ: az önállóan elérhető InvenTree webfelület/API nem kerül automatikusan PC-Impressz-licencvédelem alá. A PC-Impressz a saját moduljainak használatát korlátozza. Ha minden írási csatornát központilag kell licencelni, ahhoz külön backend-integráció és hozzáférési szabályzat szükséges. Ezt a kereskedelmi ígéret előtt tisztázni kell.

Az első kiadás üzleti offline írása ne legyen cél: a licenc offline működőképessége nem jelent InvenTree nélküli készletkezelést. Helyi üzleti adatbázis és kétirányú szinkron külön projekt, konfliktuskezeléssel és készletfoglalási szabályokkal.

Ha később saját adatbázis készül, a modulok saját sémát és migrációkat birtokoljanak, kezdetben egy PostgreSQL-példányban. Keresztmodulos írás helyett szerződés, többmodulos folyamathoz koordinált tranzakció vagy tartós esemény/outbox kell. A készletmozgás, audit és eseményrögzítés atomi legyen; pénz és mennyiség decimális típus, ismételt kérés idempotens, párhuzamos módosítás ellenőrzött.

Migrációs sorrend: törzsek → partnerek és helyek → készlet és kapcsolatok → rendelések és gyártás → előzmények → média és sablonok. Megőrzendők az eredeti azonosítókhoz tartozó megfeleltetések, mértékegységek, pénznemek és sorozatszámok. A jelszóhash-ek nem másolandók automatikusan új hitelesítési rendszerbe.

## 11. Frissítés, mentés és helyreállítás

A Shell/Core/Host egy ellenőrzött alapkiadást alkosson; a modulcsomagok verziózott kompatibilitási tartományokkal érkezzenek. Frissítés előtt csomagaláírás-ellenőrzés, függőségvizsgálat és adatmentés kell. A telepítési állományok ne legyenek normál felhasználó által írhatók.

A licenc lejárata, modul letiltása vagy eltávolítása nem törölheti az üzleti adatokat. Destruktív migráció csak külön tervezett kiadás részeként történhet. Adatbázis-verzióváltás után a bináris visszaléptetés önmagában nem helyreállítás: kompatibilis séma vagy igazolt teljes mentés-visszaállítás szükséges.

InvenTree esetében a mentési körbe az adatbázis mellett a média, konfiguráció és a pontos képfájl-/bővítményverziók is tartoznak. Éles adatokkal csak sikeres, elkülönített visszaállítási próba és egyeztetett átállási eljárás után szabad továbblépni.

## 12. Megvalósítási szakaszok és elfogadási kapuk

### 0. Bizonyítékok és követelmények teljessé tétele

Feladat: Windows-források vagy meglévő telepítő beszerzése, pontos InvenTree-verzió és API-séma rögzítése, bővítménylista, teljes mentés és médiák felmérése. Tesztkörnyezetben visszaállítási próba; valós használati folyamatok és eladandó modulok rangsorolása.

Kimenet: tényleges rendszerleltár, adat- és integrációs térkép, üzleti követelmények. Elfogadás: ismert, mi őrizhető meg, mi épül újonnan, és visszaállítható-e a jelenlegi rendszer. Jelenleg ez a kapu még nem teljesült.

### 1. Architektúradöntések és szerződések

Feladat: a Core/Shell/Host határainak, adatstratégiának, modulazonosítóknak, képességeknek és licencszabályzatnak véglegesítése; támogatott Windows-verziók és egy-/többgépes modell rögzítése.

Kimenet: rövid architektúradöntési feljegyzések, modul- és parancsszerződések, fenyegetési modell, licencformátum. Elfogadás: nincs körkörös modulfüggőség, minden fizetős művelethez hozzárendelhető képesség és végrehajtási ellenőrzési pont.

### 2. Core és Shell alap

Feladat: minimális futó Shell és Host, naplózás, beállítások, hitelesítés, modulfelderítés, kompatibilitásvizsgálat és hibás modul elkülönítése.

Elfogadás: üzleti modulok nélkül is indul; hibás, aláíratlan vagy inkompatibilis csomag nem fut; a Shell nem függ konkrét üzleti modultól. Külső rendszer éles írása még nem szükséges.

### 3. Licencelés végponttól végpontig

Feladat: külön tesztkiadói szolgáltatás, kulcskezelés, aktiválás, aláírásellenőrzés, lease, megújítás, visszavonás, gépcsere és offline fájlfolyamat. Tesztóra és determinisztikus időtesztek.

Elfogadás: módosított token, rossz kulcs, hibás algoritmus, rossz célközönség, másik telepítés és régi jogosultság nem adhat engedélyt. Lejárat közvetlen API-hívással sem kerülhető meg; internetkimaradás az engedélyezett határig működik. A fizetési esemény ismétlése nem hosszabbít kétszer.

### 4. Első üzleti folyamat: Catalog + Inventory

Feladat: terméklista, raktárhely, bevételezés, kiadás és leltár az InvenTree-adapteren keresztül. A szerver verziójához kötött szerződéstesztek, auditálhatóság és hálózati hibakezelés.

Elfogadás: ugyanaz a bevét nem keletkezik kétszer válaszkiesés és újrapróbálás miatt; párhuzamos kiadás nem okoz hibás készletet; licenclejárat közben nincs félbehagyott adatbázis-művelet. Ha az InvenTree végpontja nem ad idempotenciagaranciát, az adapternek egyeztetést vagy külön kiegészítést kell biztosítania, nem vakon ismételnie.

### 5. Valódi modulfüggetlenség igazolása

Feladat: egy második, külön csomagolt modul – javasolt Labels – bevezetése. Eltérő lejáratok és hiányzó függőségek kezelése.

Elfogadás: a második modul hozzáadásához nem kell Core- vagy Shell-üzleti kódot módosítani; lejárata nem tiltja le az érvényes Inventory modult; eltávolítása nem sérti a készletadatokat. Ezzel bizonyítható, hogy a modularitás működési tulajdonság.

### 6. Telepítő és korlátozott ügyfélpilot

Feladat: aláírt alaptelepítő és modulcsomagok, frissítés, mentés-visszaállítás, licencszerver-kiesési próba és felhasználói útmutató. Reprezentatív adatokkal teljesítmény- és használhatósági próba.

Elfogadás: tiszta gépen telepíthető; frissítésből és hibából helyreállítható; offline működés és lejárat érthető; az ügyfél jogosultként az adatait lejárat után is exportálhatja.

### 7. További modulok és opcionális adatköltözés

Beszerzés, értékesítés, gyártás, webshop és fejlett riportok üzleti prioritás szerint következzenek. Saját adatbázisra migráció csak külön jóváhagyott terv, rekord- és készletegyeztetés, üzleti folyamatpróba és visszaállási kapu után induljon.

Az idő- és költségbecslést a 0–1. szakasz lezárása után lehet megalapozni. A fázisok sorrendje vállalható most; pontos határidő nem vezethető le az üres Windows-mappából és az adatbázissémából.

## 13. Kiadást blokkoló ellenőrzések

- Lejárat előtti és utáni pillanat, különböző modullejáratok, nyári/téli időváltás, óra-visszaállítás és újraindítás.
- Aláíráshamisítás, ismeretlen kulcs, algoritmuscsere, más licenchez tartozó lease, régi licenc visszajátszása és gépkötés.
- Licencszerver-kiesés, DNS/TLS-hiba, duplikált aktiválás, deaktiválás és megújítás párhuzamos kérésekkel.
- UI-t megkerülő szolgáltatáshívás, háttérfeladat és több felhasználó egyidejű műveletei.
- Hiányzó függőség, inkompatibilis modul, migrációs hiba és frissítés közbeni megszakítás.
- Lejárat után olvasás/export, jogosultságok megtartása, adatok megőrzése és tesztelt visszaállítás.

Ezek a megvalósítás későbbi elfogadási követelményei. Ebben a tervezési feladatban nem futottak alkalmazástesztek, és nem készült működő licencrendszer.

## 14. Jóváhagyásra előkészített döntések

1. Új Windows-alap: .NET 10 + WPF; külön, helyben vagy központilag futó Application Host.
2. Core és Shell mint közös alap; minden üzleti funkció külön modul, először Catalog + Inventory.
3. Első adatstratégia: InvenTree API, közvetlen adatbázis-írás nélkül; üzleti offline írás későbbi önálló fejlesztés.
4. Szervezeti licenc, regisztrált Host-telepítések, név szerinti felhasználói keret, modulonkénti lejárat.
5. ES256-aláírt jogosultság + rövid lease; 12 órás megújítás és legfeljebb 7 napos offline idő mint induló paraméter.
6. Lejárat után üzleti írás leállítása, jogosult olvasás és alapadat-export megőrzése.
7. A következő műszaki lépés a hiányzó bizonyítékok pótlása és a szerződések véglegesítése; implementáció csak külön jóváhagyás után.

**Elvégzett változtatás:** kizárólag ez a tervdokumentum jött létre. Alkalmazáskód, konfiguráció, adatbázis, telepítés és licencbeállítás nem változott.
