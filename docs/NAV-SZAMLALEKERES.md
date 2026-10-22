Aktuális, 0.4.19-es működés: [gyors NAV-előkészítés, soronkénti készlet, árképzés és Bluetooth-címke](NAV-GYORS-BEVETELEZES-0.4.19.md). Az alábbi szöveg korábbi verziót ír le.

Aktuális, 0.4.18-as működés: [automatikus előnézet, állapotszínek, termékadatok és kisker ár](NAV-BEVETELEZES-0.4.18.md). Az alábbi korábbi verzió leírása történeti állapot.

A 0.4.15-től a menü neve **Számlák → Bejövő számlák**; a NAV-lekérés ugyanazon az oldalon, lenyitható panelben jelenik meg. [Aktuális munkafelület](SZAMLAK-MUNKAFELULET.md).

# NAV bejövő számlák – 0.4.14

A korábbi raktárkezelő alapfolyamatát követi: számlalista, kiválasztás, tétel- és termékegyeztetés, majd bevételezés / költség / függő besorolás. A korábbi rendszer minden automatizmusának átvétele még nem történt meg.

1. Bevételezés → NAV-számlák lekérése.
2. Válassz legfeljebb 35 napos időszakot, majd kérd le a listát. Több oldal esetén lapozhatsz.
3. Válassz számlát. A szállító adószám alapján kerül kiválasztásra; hiányzó partner létrehozható.
4. Töltsd be a kiválasztott számlát, egyeztesd a termékeket és a szolgáltatásokat.
5. Véglegesítsd a megfelelő besorolással. A letöltés önmagában nem módosít készletet.

Ugyanazon szállító ugyanazon NAV-számlájának ismételt betöltése a meglévő bizonylatot nyitja meg; nem hoz létre új készletmozgást. A korábban letöltött NAV XML feltöltése továbbra is elérhető a lenyitható részen.

A cég beállításaiban mentett NAV technikai felhasználó és teszt/éles környezet használatos. A hitelesítő adatok a háttérfolyamatban maradnak. A hálózati letöltés után, a mentés előtt a munkamenet, jogosultság és licenc ismét ellenőrzésre kerül. A fogadott számla vevőjének és szállítójának adószámát, számlaszámát és összesítéseit is ellenőrzi a program.

Első körben a normál HUF-számlák és a meglévő importáló által támogatott 0/5/18/27 százalékos áfa kezelhető. Módosító, sztornó, devizás és különleges áfakezelésű számlák automatikus könyvelése nem támogatott; ezek külön ellenőrzést igényelnek. Csoportos adóalanyok eltérő vevőazonosítása is külön fejlesztést igényelhet. A funkció bejövő számlákat olvas, nem állít ki NAV-számlát.

Ellenőrzés: 177/177 üzleti/Core/Host teszt és 57/57 WPF render sikeres. Szintetikus HTTP-válaszokkal tesztelt dátumszűrés, lapozás, aláírás, tömörítés, hibás XML, azonosítók, ismételt import és készletvédelem. A céges technikai felhasználóval végzett élő NAV-próba még hátravan; a fejlesztés ellenőrzése nem írt az eredeti céges adatbázisba.

Hivatalos protokoll: [NAV API-séma](https://github.com/nav-gov-hu/Online-Invoice/blob/master/src/schemas/nav/gov/hu/OSA/invoiceApi.xsd), [számlalekérési példa](https://github.com/nav-gov-hu/Online-Invoice/blob/master/sample/API%20sample/queryInvoiceData.xml).
