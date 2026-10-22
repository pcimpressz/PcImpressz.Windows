# Admin panelrendező – 0.4.17

A fő munkafelületek panelcsoportjai egérrel átrendezhetők és méretezhetők. A beállítás cégenként, adatlap és nézet szerint a céges SQLite-adatbázisban tárolódik; az adott cég felhasználói a mentett nézetet kapják meg. Menteni csak az aktuálisan hitelesített admin tud. A meglévő felirat- és mezőszerkesztő továbbra is külön használható.

## Használat

1. Adminként nyisd meg az oldalt, és kattints a **Nézet rendezése – admin** gombra.
2. A megjelenő fejlécet húzd egy másik panelre a sorrend módosításához. A fel/le gombokkal is rendezhetsz.
3. A jobb alsó sarkot húzva állítsd a szélességet és magasságot. Dupla kattintás a sarokra visszaállítja az automatikus magasságot.
4. A **Nézet mentése** rögzíti a nézetet. A **Mégse** a korábbi mentést állítja vissza.

Az **Alapnézet** és az **Előző mentés** előnézetet alkalmaz; a megtartásához utána ugyanúgy a Nézet mentése kell. A rendszer az előző tíz mentést őrzi meg. Mentés nélküli elnavigálás után a korábban mentett nézet töltődik vissza.

A panelrendezés alatt a panel üzleti tartalma nem szerkeszthető. A már beírt, még nem mentett adatokat a rendezés, méretezés, nézetmentés és mégse nem írja át. Adatbetöltés vagy a jogosultság elvesztése lezárja az elrendezés előnézetét. Másik admin párhuzamos mentését a verzióellenőrzés megvédi a felülírástól; ilyenkor az oldal új megnyitásával tölthető be a frissebb nézet.

## Elérhető fő panelcsoportok

- **Számlák / Bejövő:** számlalista, NAV-lekérés/import, számlabevételezés. A kimenő nézet külön menthető.
- **Partnertörzs és terméktörzs:** keresés/műveletek, meglévő lista–adatlap munkafelület, további adatok. A lista és adatlap eddigi belső együttműködése megmarad.
- **Eladás:** termékkeresés és kosár, véglegesítés, korábbi eladások.
- **Készlet:** műveletek, szerkesztő, lista és további adatok.

Ez a fő panelcsoportok szerkesztése, nem tetszőleges új ablakok vagy üzleti mezők létrehozása. A rendszer sorokba tördeli a paneleket; nem engedi, hogy azok egymást eltakarják. Keskeny ablakban a mentett szélesség a rendelkezésre álló helyhez igazodik, majd nagyobb ablakban újra érvényesül. A rögzített magasságú panel tartalma belül görgethető; a belső listák és a külső oldal görgetése összehangolt.

## Tömörebb bevételezés

A számlaszám és keltezés két oszlopba került. A fizetési határidő, raktár és megjegyzés lenyitható részben található. A betöltött tételsorok alaphelyzetben rövid áttekintést mutatnak névvel, mennyiséggel, nettó egységárral és párosítási állapottal. A sor lenyitásával minden korábbi termékkeresési, párosítási és szerkesztési lehetőség elérhető. Új üres tételnél a részletek rögtön nyitva vannak.

## Ellenőrzés

189/189 üzleti/Core/Host teszt és 69/69 WPF render sikeres. Külön ellenőrzött: admin/warehouse jogosultság, céges és nézetenkénti elkülönítés, méretkorlátok, nem véges méretek tiltása, párhuzamos mentés, korábbi nézet, alapnézet, visszatöltés, nem mentett számlaadatok megőrzése és egérgörgős továbbgörgetés. A helyi parancscsatornán nem lehet a kérésbe írt admin-tokenre hivatkozva nézetet menteni: a Host az aktuális munkamenetet ellenőrzi.

A változás nem állít ki számlát, nem küld e-mailt, nem módosít készletet és nem kapcsolja be a fejlesztési számlakiállító API-t. A próbák elkülönített tesztadatokkal futottak. Frissítés után a felületet és a külön futó háttérfolyamatot is újra kell indítani a Start.cmd segítségével.
