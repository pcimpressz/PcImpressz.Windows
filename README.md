# PC-Impressz Windows Edition – Core + Windows Shell + üzleti modulok v0.4.12

A Windows-termék üzleti moduloktól független magja és testreszabható grafikus kerete. A projekt új helye: `F:\progs\PcImpressz.Windows`. A korábbi architektúraterv a `docs` könyvtárban található.

## Indítás

A `Start.cmd` lefordítja a programot, majd megnyitja a grafikus Windows-ablakot. A mag szükség esetén automatikusan elindul a háttérben. A felületet az ablak bezárásával állíthatod le; az általa indított háttérfolyamat is kilép. Egy már korábban, külön elindított maghoz is tud kapcsolódni, és annak életciklusát nem veszi át.

PowerShellből:

```powershell
.\dev.ps1 -Action Status   # Fordítás, állapotellenőrzés, kilépés
.\dev.ps1 -Action Test     # Fordítás és önálló ellenőrzőprogram
.\dev.ps1 -Action Run      # Grafikus Windows-felület
.\dev.ps1 -Action Core     # Csak a mag, konzolban (Ctrl+C)
.\dev.ps1 -Action Publish  # Kiadási állományok: artifacts/desktop
```

A fordításhoz .NET SDK 10.0.402 szükséges, vagy a `global.json` által elfogadott újabb javítókiadás. A helyi SDK a projekt `.tools/dotnet` könyvtárában van, a forrásoktól elkülönítve. Másik gépen telepíthető a [Microsoft .NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). A helyi SDK hivatalos kiadási metaadat szerinti SHA-512 ellenőrzéssel került letöltésre.

A Core a hivatalos Microsoft.Data.Sqlite 10.0.13 csomagot használja, rögzített NuGet-feloldással. Külön adatbázis-szerver, npm és Python nem szükséges a program futásához vagy fordításához. A NuGet-forrás a nuget.org; az első restore internetkapcsolatot igényel. Az ASP.NET Core megosztott keretrendszer konfigurációt, naplózást és folyamat-életciklust biztosít.

A `Publish` jelenleg keretrendszerfüggő kiadást ad: másik gépen .NET Desktop 10 és ASP.NET Core 10 futtatókörnyezet kell hozzá. A grafikus alkalmazás az `artifacts/desktop`, a mag annak `host` alkönyvtárába kerül. Ez még nem ügyféltelepítő és nem Windows-szolgáltatásként regisztrált alkalmazás.

Ellenőrzött állapot (2026-10-21): Release fordítás, üzleti/Core/Host regressziók, görgetési próba és WPF nézetrender. A pontos eredmény és a fennmaradó auditfeladatok: [Auditintegráció](docs/AUDIT-INTEGRACIO-2026-10-21.md). A teszteket a `Test` művelet futtatja, nem a `dotnet test`. A grafikus ablak külön, ugyanazon Windows-felhasználóra korlátozott helyi csatornán olvassa a mag aktuális állapotát; a diagnosztikai csatorna üzleti parancsot nem fogad. A cég- és modulműveletek külön, ugyanazon Windows-felhasználóra korlátozott helyi parancscsatornán futnak.

## A felület testreszabása

A **Megjelenés** oldalon azonnal válthatsz az Éjkék, Porcelán és Grafit témák között, megválaszthatod a kiemelőszínt, a betűméretet (100%, 110%, 125%), az oldalsáv szélességét és a kompakt térközöket. A választások és az ablak mérete automatikusan mentődnek a `.local/appearance.json` fájlba.

Saját skinhez használd a **Jelenlegi skin exportálása** gombot, vagy másold a `skins/sajat-skin.json` mintát. A fájl színeit `#RRGGBB` formában lehet szerkeszteni, majd a **Skin importálása** gombbal visszatölteni. A skin kizárólag ellenőrzött színadatokat tartalmazhat, futtatható kódot vagy XAML-t nem. A túl alacsony szövegkontrasztú palettát a program elutasítja.

A felület a valódi helyi állapotot, a modulokat, licencet és megjelenést kezeli. Üzleti modul a Partnertörzs, a Terméktörzs, a Bevételezés, a Készlet és az Eladás. A partnerek használata: docs/PARTNERTORZS.md. A terméktörzs kategóriát, mértékegységet, cikkszámot és alapparamétert tart, CSV-exporttal: docs/TERMEKTORZS.md. A bevételezés kézi számlát, NAV-számla XML-t és Excel/CSV importot fogad, majd besorolja: docs/BEVETELEZES.md. Az új készlet-, eladás- és cégmentési funkciók használata: [KESZLET-ELADAS-ES-MENTES.md](docs/KESZLET-ELADAS-ES-MENTES.md). A közös fejlesztési irány és a munkamegosztás: [KOZOS-MUNKANAPLO.md](KOZOS-MUNKANAPLO.md). Online előfizetési aktiválás és drag & drop szerkesztőmód még nincs benne.

## Mi működik?

- Külön `Contracts` szerződéscsomag, UI- és üzleti függőség nélkül.
- Modulok felderítése, külön kulcskészlettel ellenőrzött ES256 csomagaláírás és SHA-256 fájlellenőrzés.
- Modulonkénti betöltési környezet; verzió- és függőségellenőrzés; körkörös függések tiltása; részleges parancsregisztráció visszatartása sikertelen indításnál.
- Központi parancsvégrehajtás: pontos felhasználói jogosultság, modullicenc és a szükséges modulfüggőségek licencének ellenőrzése minden új parancs előtt.
- ES256-aláírt jogosultság és lease ellenőrzése: kibocsátó, termék, szervezet, telepítés, verzió, időablak, modulonkénti lejárat és képességek.
- Legfeljebb 7 napos lease, kis jövőbeli kibocsátási/óra-visszaállítási tolerancia; a lejáratnak nincs automatikus türelmi ideje.
- Korábban látott jogosultságverzió és idő helyi megőrzése, atomi állapotfájlcsere, sérült vagy nem menthető állapotnál az írási jog megtagadása.
- Kifejezetten adatmegtekintéshez/exporthoz regisztrált helyreállítási parancsok licenc nélkül is hívhatók, de csak megfelelő felhasználói jogosultsággal.
- Konfiguráció, konzolnapló és méretkorlátos JSON-napló; egy adatmappához egy futó Host.
- Elkülönített, automatikus kriptográfiai és modulbetöltési tesztek. A tesztkulcsok csak futás közben, memóriában keletkeznek.

A mag üzleti modul nélkül is fut. Nincs beépített raktár, terméktörzs, próbalicenc, tesztadat vagy fejlesztői feloldókód a futtatható alkalmazásban.

## Szerkezet

- `src/PcImpressz.Contracts`: a modulok és a mag közös szerződései.
- `src/PcImpressz.Core`: modulrendszer, parancsengedélyezés, aláírás- és licencellenőrzés, állapottárolás.
- `src/PcImpressz.ApplicationHost`: konfiguráció, összekapcsolás és futtatható folyamat.
- `src/PcImpressz.Shell.Windows`: natív WPF-ablak, oldalak, stílusok és a maghoz kapcsolódás.
- `src/PcImpressz.Shell.Customization`: felülettől független skin- és beállításkezelés, kontrasztellenőrzéssel.
- `tests/PcImpressz.Core.Tests`: külső tesztkeretrendszer nélküli tesztfuttató, hiba esetén nem nulla kilépési kóddal.
- `src/Modules/PcImpressz.Modules.Catalog`: terméktörzs, kategória és mértékegység. Nem készletmodul.
- `src/Modules/PcImpressz.Modules.Receiving`: bejövő számla, NAV XML és Excel/CSV; véglegesítéskor közös tranzakcióban készletbevét.
- `src/Modules/PcImpressz.Modules.Inventory`: nyitókészlet, korrekció, mozgásnapló és készletegyenleg.
- `src/Modules/PcImpressz.Modules.Sales`: eladási bizonylat, készletcsökkentés és visszafordítás.
- `src/PcImpressz.Business`: közös, pontos mennyiségekkel dolgozó készletnaplózási szabályok; a modulcsomagok aláírt függősége.
- `tools/PcImpressz.Maintenance`: felülettől független cégmentés és visszaállítás.
- `tests/PcImpressz.Shell.UiTests`: készlet és eladás renderelése mindhárom skinnel, kötési és oszlopszélességi ellenőrzéssel.
- `tests/PcImpressz.TestModule`: kizárólag teszteléshez fordított modulszerelvény; nem része a kiadásnak.

A Core nem hivatkozik a Hostra vagy a grafikus felületre. A Shell nem hivatkozik a Core-ra: a Contracts diagnosztikai szerződését használja. A modul a Contracts szerződéseire és szükség szerint a csomagjában aláírt közös Business könyvtárra hivatkozhat; a Core implementációjára nem. Új üzleti modul hozzáadásához a Core és Host forrását nem kell módosítani; a mező- és oszlopleírásokat a modul regisztrálja, a Shell ebből építi fel a képernyőt.

## Saját adatok és konfiguráció

A fejlesztői indító a `.local` mappát használja. Közvetlen indításnál megadható `--data-dir <mappa>`; enélkül a saját felhasználó helyi alkalmazásadat-mappájának `PcImpressz/WindowsEdition` alkönyvtára lesz az alapértelmezés.

Az adatmappában:

- `installation-id.txt`: első induláskor létrejövő telepítésazonosító.
- `settings.json`: opcionális felülírás, például `{"Product":{"Issuer":"sajat-kiado","OrganizationId":"ceg-azonosito"}}`.
- `license-public-keys.json`: licencaláírást ellenőrző kulcsok; JSON objektum, amelyben a kulcsazonosítóhoz PUBLIC KEY formátumú PEM szöveg tartozik.
- `module-public-keys.json`: ettől külön, ugyanilyen formátumú modulkiadói nyilvános kulcskészlet.
- `license.jws`, `lease.jws`: a kiadó által aláírt jogosultság és működési engedély.
- `license-checkpoint.json`: legnagyobb észlelt helyi idő és jogosultságverziók, nem titkos adat.
- `modules/<csomag>/manifest.jws` és a jegyzékben felsorolt DLL-ek: telepített modulok.
- `logs/core.jsonl`, legfeljebb két forgatott előzménnyel: működési napló.
- `companies/<cégazonosító>/company.db`: az adott cég SQLite-adatbázisa. Első használatkor a régi JSON-adatok tranzakcióban migrálódnak; a régi fájlok megmaradnak, de a Host már nem írja őket.
- `appearance.json`, `skins/*.json`: saját megjelenési beállítások és importált témák.

Az alapértelmezett szervezet üres, megbízható kulcs nincs. Így új telepítés nem kap üzleti írási jogot. A `pcimpressz-issuer` csak helyi konfigurációs alapérték, nem létező aktiválási végpont.

A licencfájlokat frissítéskor ideiglenes fájl és atomi átnevezés segítségével kell cserélni. A két dokumentum eltérő verziója átmenetileg elutasítást okoz, nem túlzott jogosultságot. A kulcskészlet és a szervezeti konfiguráció változtatása újraindítást igényel; a licencdokumentumokat a mag minden ellenőrzésnél újraolvassa.

## Modulcsomag v1

A csomag lapos könyvtár: aláírt `manifest.jws` és csak a jegyzékben felsorolt managed DLL-ek. A jegyzék típusa `pcimpressz-module+jws`. Tartalma a `ModuleManifest` osztály mezőit követi; verziói háromtagúak, például `1.0.0`, a Contracts API pontosan `1.1`.

Az egyes DLL-ekhez SHA-256 kivonat tartozik. A fő DLL és a függőségek az ellenőrzött memóriabeli bájtokból töltődnek be. A Contracts DLL nem csomagolható a modul mellé. Natív függőségek, alkönyvtárak, csatolt útvonalak, forró modulcsere és részleges csomagfrissítés nem támogatottak ebben a verzióban.

A modul paraméter nélküli belépési típusa az `IModule` szerződést valósítja meg. `StartAsync` alatt parancsokat regisztrálhat; az indítás befejezése után a regisztráció lezárul. Egy modulon belül minden parancs egyedi nevet és stabil jogosultságazonosítót kap. Az üzleti validáció és az atomi tranzakció az üzleti modul felelőssége.

Az `Active` állapot azt jelenti, hogy a megbízható modul technikailag betöltődött, és például megtekintési/exportálási felületet biztosíthat. Ez önmagában nem ad fizetős műveleti jogot. A hosszú háttérfolyamatoknak minden új biztonságos feldolgozási egységnél újra be kell lépniük az engedélyezési kapun.

## Licencformátum v1 és védelmi határok

A `LicenseDocuments.cs` rögzíti a dokumentummezőket. Mindkét dokumentum JWS Compact, kizárólag `ES256` algoritmussal és ismert `kid` kulcsazonosítóval. A típusok: `pcimpressz-license+jwt`, illetve `pcimpressz-lease+jwt`. Az `iat`, `nbf`, `exp` és `expiresAt` egész UTC Unix-másodperc. Az `nbf` befoglaló, az `exp` kizáró időhatár.

A jogosultság és a lease azonos licenchez, szervezethez és jogosultságverzióhoz tartozzon. A lease legfeljebb 7 napos és nem nyúlhat túl a teljes jogosultságdokumentum élettartamán. A modul tényleges határideje a modul-előfizetés, a jogosultságdokumentum és a lease lejárata közül a legkorábbi.

Ez jelenleg **helyi ellenőrzési alap**, nem kész kereskedelmi licencszolgáltatás. Még nincs aktiválószerver, szériaszám-kezelés, automatikus megújítás, fizetési kapcsolat, felhasználószám-korlát, online visszavonás, kulcsváltási terítés vagy offline kérés–válasz fájlkezelő. A telepítésazonosító nem hardverhez kötött gépkulcs; a TPM/DPAPI-alapú gépkötés későbbi feladat.

A helyi idő-/verzióellenőrzési fájl szerkeszthető, illetve korábbi állapotra visszaállítható helyi rendszergazdaként. Emiatt nem hiteles időforrás és nem teljes védelem gépklónozás vagy virtuálisgép-pillanatkép ellen. A leállított alkalmazás ideje sem bizonyítható offline. A modulkód ugyanabban a folyamatban fut; a külön betöltési környezet nem biztonsági homokozó. Csak a kiadó által ellenőrzött és aláírt saját modulokhoz használható.

A `CallerIdentity` egy későbbi megbízható hitelesítési réteg eredménye. Külső kérésből kapott felhasználói azonosító vagy jogosultságlista nem adható közvetlenül a parancsvégrehajtónak. Emiatt most nincs hálózati API vagy távolról elérhető parancsvégpont.

Élesítés előtt külön kulcskezelés, OS-hozzáférésvédelem, megbízható hitelesítés, támogatott frissítési/telepítési eljárás, helyreállítási próba és a licencellenőrzés független biztonsági felülvizsgálata szükséges.

## További lépések

A bevételezés a szállítót a partnertörzs beszállítói közül, a terméket a terméktörzsből választja. A besorolás megmarad: bevételezés, költség vagy függő. Készletmozgás, helyi eladás és ellenőrzött cégmentés már működik. Élő NAV Online Számla lekérdezés és PDF-olvasás még nincs. A kiadói aktiválás, gépkulcsos megújítás és a drag & drop szerkesztő külön későbbi fejlesztés.

A fejlesztői mesterlicenc aláírt és telepítéshez kötött, 2099-12-31-ig használható; normál ügyféllicencnél a legfeljebb 7 napos működési engedély megmarad. Részletek: [Fejlesztői licenc](docs/DEVELOPER_MASTER_LICENSE.md). Az InvenTree-kapcsolat kivezetve; az alkalmazás saját adatbázissal működik.

## 0.4.1 – Terméktörzs és készlet

Gyártói cikkszám, többfeltételes/ékezetfüggetlen keresés, saját készletszűrők és a terméktörzsbe épített készletműveletek. Audit: alkalmazott keresés szerinti export és atomi CSV-írás. Ellenőrzés: 139/139 teszt, wheel-ok, 9/9 WPF-render és célzott UI-regressziók. Használat és a későbbi nagykerenkénti kosarak terve: [Terméktörzs, készlet és nagyker](docs/TERMEKTORZS-KESZLET-ES-NAGYKER.md).

## 0.4.2 – Széles terméklista és külön adatlap

A Terméklista teljes szélességet kapott. Új termék és kijelölt termék szerkesztése a külön Termékadatlap fülön nyílik; a listában a sor dupla kattintása is megnyitja. A terméknév és a többsoros mezők teljes sorosak, a többi mező szélesebb. A Készlet és mozgások a harmadik belső fül. A fülváltás megőrzi a szerkesztett adatot. Ellenőrzés: 139/139 teszt, wheel-ok, 12/12 WPF-render. Az adminhoz kötött drag-and-drop/feliratszerkesztő következő fejlesztés, még nincs kész: [Felületszerkesztő terve](docs/ADMIN-FELULETSZERKESZTO.md).

## 0.4.3 – Adminhoz kötött felirat- és elrendezésszerkesztő

A törzsadatlapok Feliratok és elrendezés – admin gombja cégenkénti bejelentkezést kér; első használatkor létrehozható az adminfiók. Húzással vagy Fel/Le gombbal átrendezhető mezők, átírható feliratok, fél-/teljes soros szélesség, előnézet, verziózott mentés, alap- és előző nézet visszaállítása. Kötelező mező nem rejthető el. A normál szerep a Hoston sem menthet elrendezést. A szerepkörök most a felületszerkesztésre vonatkoznak; általános üzleti szerepkörök és offline admin-helyreállítás még nincsenek. Ellenőrzés: 148/148 teszt, wheel-ok, 15/15 WPF-render. Használat és korlátok: [Admin felületszerkesztő](docs/ADMIN-FELULETSZERKESZTO.md).

## 0.4.4 – Bevételezés–eladás munkafolyamat

Készletbiztos bevételezés-visszafordítás, szerkeszthető eladási kosár, raktári készlet és gyártói cikkszám alapú keresés; szűrt exportok. A külön engedélyezett offline admin-helyreállítás is elkészült. Részletek: [munkafolyamat](docs/BEVETELEZES-ELADAS.md) és [admin-kezelés](docs/ADMIN-FELULETSZERKESZTO.md). Ellenőrzés: 155/155 teszt, 21/21 WPF-render.

## 0.4.5 – Céges bejelentkezés és dolgozói jogosultságok

Minden üzleti olvasás, írás és export céges bejelentkezést igényel. Adminisztrátor, raktáros, eladó és megtekintő szerepkör; admin felhasználókezelés, fióktiltás, szerepkörváltás, saját jelszócsere és visszavont munkamenetek. Az ellenőrzést a háttérszolgáltatás is elvégzi, a menük és szerkesztők alkalmazkodnak a szerephez. A korábbi adminfiókok megmaradnak; az első admin a bejelentkező képernyőn hozható létre. Újraindításkor a korábban külön indított régi háttérszolgáltatást is le kell állítani.

Használat és pontos szerepkörök: [Céges belépés és jogosultságok](docs/BELEPES-ES-JOGOSULTSAGOK.md). Ellenőrzés: 158/158 teszt, 30/30 WPF-render; a helyi demócég ellenőrzött másolatán raktáros–eladó folyamatpróba, az eredeti adatbázis változatlan lenyomatával. A forrásban nulla termék és egy partner volt, a próba-termék kizárólag a másolatba került. Éles céges felhasználói átvétel még szükséges.
## 0.4.6 – Demócég feltöltése mintaadatokkal

A Beállításokban megjelent a **Feltöltés mintaadatokkal** gomb. Csak a kiválasztott demócég adminja indíthatja, előzetes ellenőrzött mentéssel és ismétléskor duplikáció nélkül. A csomag 20 terméket, 5 vevőt, 5 szállítót, kategóriákat, mértékegységeket, két raktár készletét, 8 bevételezést és 10 eladást tartalmaz. A helyi demócég tényleges feltöltése is megtörtént, a meglévő adatok megmaradtak. Útmutató: [Demó mintaadatok](docs/DEMO-MINTAADATOK.md). Ellenőrzés: 162/162 teszt, 33/33 WPF-render.
## 0.4.12 – Gyors pénztári eladás

Egy keresősor, automatikus név/cikkszám/vonalkód találatok, kattintásra vagy pontos vonalkódra kosárba helyezés. A kosárban közvetlen mennyiség-, bruttóár- és áfamódosítás, soronkénti törlés; véglegesítéskor készpénz/kártya, számlaigénynél élő partnerkereső, kézi vevőfelvétel vagy valódi NAV-adószámlekérdezés. Terméktörzsbeli alapárak, mentett fizetési mód és vevőadat-pillanatkép, bizonytalan küldés visszaellenőrzése. A számlaigény nyilvántartása még nem adóügyi számlakiállítás. Használat: [Pénztári eladás](docs/PENZTARI-ELADAS.md). 168/168 teszt és 39/39 WPF-render. A NAV-protokoll szimulált válasszal ellenőrzött; éles technikai felhasználóval próba még szükséges.
0.4.12: készpénzes végösszeg 5 Ft-ra kerekítve, külön kerekítés és fizetendő; kártyán nincs 5 Ft-os kerekítés. Beállításokban 0–4 megjelenített tizedes (alap 0), a számítások pontossága megmarad. NAV-mentés: a Windows DPAPI rendszerkönyvtárainak célzott betöltése javítva.

0.4.12: eladási nap szűrése, automatikus bizonylattételek, részleges visszavét az eredeti sorhoz kötve negatív kosárban. A visszavét véglegesítése állítja vissza a készletet. Normál és dev.ps1 fordítás egymás után ellenőrizve; minden obj/bin köztes mappa kizárva.

0.4.12: partnertörzs teljes szélességű listával és alatta szerkesztővel; alapadatok és lenyitható bővített adatok; új partner NAV-adószámlekérdezése az adatlap kitöltéséhez. Alapértelmezett Nem a nem mentett adatlap elvetésénél és kilépésnél, saját főablakhoz kötött kérdés, ismételt ablakbezárás elleni védelem.

0.4.12: partneradatlap első sora adószám és kompakt Lekérés gomb. NAV-rövidnév elsőbbséggel; kapcsolható nagybetűs név/cím formázás a Beállítások / Partnertörzs alatt. A beállítások általános, partner-, eladási/készlet-, NAV- és cégmentési csoportokban találhatók.

0.4.12: kompakt, legfeljebb 680 px széles általános adatlapok; a partnerlista alatt keskeny szerkesztő és külön mentettpartner-/dosszié-áttekintés. Keskeny ablakban egymás alá rendeződő panelek. Mentés/Elvetés kiemelve, ritkább műveletek külön lenyithatók. A termékadatlap hosszú mezői és a táblázatok megtartják a szükséges helyet.
