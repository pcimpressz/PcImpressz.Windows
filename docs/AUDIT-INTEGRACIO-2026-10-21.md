# 0.4 auditintegráció – 2026. október 21.

## Kiindulás és megőrzés

A helyi forrás `F:\progs\PcImpressz.Windows`, induló commit `96276b6`. A GitHub main 11 committal volt lemaradva, és jelentős 0.4-es fejlesztés volt a munkafában. A forrást, a helyi adatokat és a kiadói könyvtárat külön helyi munkamentés őrzi; a titkos kulcsok és a céges adatok nem kerülnek Gitbe. Integrációs ág: `audit/license-stabilization-2026-10-21`.

A megadott audit 8. fejezetét követjük. A régi licenc-ZIP teljes forrásával nem írtuk felül a 0.4-et: a LicenseEvaluator változása és négy teszt átkerült, a kiadóban csak a lejárati szabály változott, mind az öt modul megmaradt.

## Ebben a munkamenetben készült

- A Maintenance projekt elavult függőségzárának és Windows-platformellenőrzésének javítása. Az induló teljes fordítás ezek miatt nem volt sikeres.
- Aláírt, telepítéshez kötött fejlesztői licenc és lease 2099-12-31-ig. A kivétel külön fejlesztői kibocsátót, szervezetet és licencazonosítót kíván; a normál hét napos korlát, az óra/revízióvédelem és a moduljogok megmaradnak.
- InvenTree partner- és termékimport teljes futásidejű kivezetése: kliensek, parancsok, dialógus, gombok, URL/token DTO-k, használaton kívüli repository-importok és kizárólagos importtesztek eltávolítva. A bevételezés XML/CSV/XLSX importja megmarad. A rekordok Source/ExternalId történeti mezői megmaradnak.
- Mentés közben a törzsadatlap és a listakijelölés zárolt. A ViewModel az Új és a kijelölés műveletet is védi. Aktív munkalapművelet vagy mentés/visszaállítás alatt a bezárás nem engedett, a felület visszajelzést ad.
- A technikai fájlnapló IOException/hozzáférési hibája nem írhatja felül egy üzleti művelet eredményét; Trace fallback. Az üzleti audit továbbra is a meglévő adatbázis-tranzakció része.
- A Host-integrációs teszt a tényleges dotnet futtató elérési útját használja; dev.ps1 átadja azt.
- Új regresszió: kivezetett parancsok hiánya; történeti eredet megőrzése szerkesztéskor; túl nagy mentés elutasítása a korábbi fájl megőrzésével; WPF szerkesztési és kijelölési zár.

## A01–A16 újraellenőrzés

- **A01 – megoldott a meglévő 0.4-ben.** ModuleStorageScope.Write ellenőrzi a szerializált UTF-8 méretet bármely írás előtt. Új határátlépési regresszió igazolja a régi adat megőrzését.
- **A02 – részben megoldott a meglévő 0.4-ben.** CompanyBackupService SQLite Backup API-t, SHA-256-ot és integritásellenőrzést használ; külön új cégbe állít vissza. Az automatikus megőrzés, teljes helyi konfiguráció és jövőbeli csatolmányok mentése további munka. A mentés jelenleg adatbázist és manifestet tartalmaz; titkot nem.
- **A03 – javítva ebben a körben.** EntityPageView zárolja a mezőket és a listát, a ViewModel védi a kijelölést és az új adatlapot. WPF állapotregresszió sikeres. Teljes kézi, késleltetett válaszú használati átvétel még szükséges.
- **A04 – részben javítva.** CanClose figyeli a WorkspaceIsIdle állapotot; a megszüntetett InvenTree-import már nem hagyhat nyilvántartatlan műveletet. A meglévő készlet/eladás request ID-k tranzakciósak, teszteltek. Minden egyéb műveletre kiterjedő tartós eredménylekérdezés még nincs.
- **A05 – fennáll.** LocalCommandService egyetlen fogadóval sorban dolgozik; hosszú bevételezési import és mentés továbbra is foglalhatja. Következő feladat a korlátozott fogadói párhuzamosság és tartós háttérmunka, cégtranzakciókkal.
- **A06 – részben megoldott.** SQL és közös tranzakció már van, de a törzsek teljes JSON-pillanatképként élnek a documents táblában. Az indexelt rekordtáblák, auditmegőrzés és 1k/10k/50k mérés hiányzik; a 200 000 eseményes alkalmazáskorlát megmaradt.
- **A07 – fennáll.** A CSV egyetlen IPC-válasz, 8 MiB üzenetkorláttal; darabolt export és atomi célfájl-véglegesítés szükséges.
- **A08 – részben megoldott a meglévő 0.4-ben.** TypedSearch/ListLoader késleltet és generációt ellenőriz. Az export még a szerkesztett Search/Filter értéket használja; az utolsó sikeresen megjelenített lekérdezés külön tárolása szükséges.
- **A09 – részben megoldott a meglévő 0.4-ben.** ListGrid/ColumnList/ListLayoutStore rendezést és oszlopláthatóságot ad; a stabil másodlagos rendezés és teljes oszlopsorrend/szélesség-mentés külön ellenőrzendő.
- **A10 – részben megoldott.** Labels/Filters descriptor és külön Receiving/Operations nézet létezik. A Shellben még vannak partner-alapértékek; a közös mezőtípusok továbbra is text/multiline/boolean.
- **A11 – megoldott irány a meglévő 0.4-ben.** A Microsoft.Data.Sqlite a Host/Core infrastruktúrában van, a modul ICompanyDatabase szerződést kap; a natív modulbetöltés tiltása megmaradt. API 1.1.
- **A12 – tudatos pilotkorlát.** A Windows-felhasználó minden helyi cég tulajdonosa; céges szerepkörök még nincsenek.
- **A13 – javítva ebben a körben.** A technikai napló szűrt fájlhibakezelést és Trace fallbacket kapott. Tényleges megtelt lemezes átvétel nem történt.
- **A14 – javítva ebben a körben.** DOTNET_HOST_PATH, majd a futtató folyamat útvonala; a beégetett .tools út megszűnt. Tiszta, csak rendszer-SDK-s Windows-gépes próba még szükséges.
- **A15 – fennáll.** A helyi nyilvánoskulcs-készlet nem rögzített ügyfélkiadási bizalmi gyökér. A fejlesztői licenc nem helyettesíti az aktiválást, revokációt és ügyfélkiadás leválasztását.
- **A16 – részben ellenőrzött.** Három téma WPF-rendere és adatkapcsolási hibamentessége tesztelt. A teljes billentyűzetes, kontraszt- és 100/125/150% DPI átvétel hiányzik.

## Ellenőrzés és következő csomag

Release build: 0 hiba, 0 figyelmeztetés. Core/üzleti/valódi Host: **133/133**. Görgetés: **wheel-ok**. WPF: **6/6 render**, adatkapcsolási hiba nélkül, plusz a szerkesztési/kijelölési zár regressziója. Telepített állapot: **5/5 aktív 0.4.0 modul, Active licenc**. A régi 142 teszthez képest 16 megszüntetett importteszt, 4 átvett licencteszt és 3 új auditregresszió adja a 133-at.

Az SQL-alapot nem kell újra megírni. Következő csomag: A07 nagy export, A08 alkalmazott lekérdezési állapot és A05 hosszú feladatok; ezután indexelt SQL-törzsek/auditmegőrzés. Beszerzési rendelés, foglalás, sorozatszám és gyártás továbbra is külön üzleti fejlesztési szakasz. Nem történt teljes kézi Windows-átvétel, éles NAV-hívás vagy ügyfélkiadás tanúsítása.
