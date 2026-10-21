# Pénztári eladás – 0.4.7

## Gyors használat

1. Az Eladás oldalon egyetlen keresősorba írd a termék nevének részletét, cikkszámát, gyártói cikkszámát vagy vonalkódját. A találatok gépelés közben jelennek meg, külön frissítőgomb nélkül. Az ékezet nélküli keresés is működik.
2. Egy kattintás a találatra egy darabot tesz a kosárba. A pontos, egyértelmű vonalkód automatikusan hozzáadja; az olvasó utáni Enter nem duplázza. Több azonos vonalkódnál választani kell. A nyílbillentyű és Enter is használható.
3. A kosárban közvetlenül szerkeszthető a mennyiség, a bruttó egységár és az áfakulcs. Minden sor saját Törlés gombot kapott. Azonos termék ismételt hozzáadása azonos ár és áfa mellett növeli a mennyiséget. A végösszeg azonnal frissül.
4. Az Eladás véglegesítése megnyitja a fizetési ablakot. Válassz Készpénzt vagy Kártyát, majd rögzítsd az eladást. Külön második megerősítő ablak nincs. A kártyás mód a fizetés módjának nyilvántartása; bankkártyaterminált nem vezérel.
5. A korábbi bizonylatok a lenyitható Korábbi eladások részben kereshetők, megtekinthetők, exportálhatók és visszafordíthatók.

A raktár alapértelmezésben Bolt. Tele kosár mellett nem váltható másik raktárra. Nulla/elégtelen készletnél nincs automatikus negatív készlet; a végső készletellenőrzés és könyvelés a háttérszolgáltatás egyetlen tranzakciójában történik.

## Ár a terméktörzsből

A Terméktörzsben beállítható a nettó eladási egységár és az eladási áfakulcs; ebből jelenik meg a bruttó ár a pénztárban. Hiányzó ár esetén a kosár árat kér, nem enged figyelmetlenül automatikus ingyenes eladást. A korábban betöltött DEMO-csomagban a már eltárolt névleges tesztár is használható, kizárólag a jelölt demóadatoknál. Új mintacsomagban az ár és áfa már rendes termékmezőbe kerül. A kosár árának módosítása csak az adott eladásra vonatkozik, nem írja át a törzs alapárát.

## Vevő és NAV-adószámlekérdezés

A fizetési ablakban a Számlát kér a vevő jelölés azonnal megnyitja az aktív partnertörzs keresőjét. Névrészlet és adószám alapján gépelés közben szűr. A meglévő partner kiválasztható; a csak szállítóként szereplő partner teljes cím esetén vevőszerepet is kap, a szállítói szerepe és meglévő adatai megmaradnak. Hiányos cím kiegészíthető a kézi űrlapon.

Új céges partnernél beírható a 8 jegyű törzsszám vagy a teljes magyar adószám, majd a NAV-lekérdezés és vevő kiválasztása gomb indítja a tényleges queryTaxpayer kérést. A sikeres, érvényes adózót és teljes címet tartalmazó válaszból a vevő automatikusan bekerül a partnertörzsbe és ki lesz választva. Az adószám törzse szerinti egyezés nem hoz létre duplikált partnert, a meglévő nem üres nevet/címet nem írja felül. Érvénytelen adózó, hiányos vagy hibás válasz, másik adószám vagy kérésazonosító, hálózati hiba vagy hiányzó konfiguráció nem hoz létre üres partnert.

A lekérdezés a Beállításokban admin által eltárolt, cégenként titkosított NAV technikai felhasználót használja; tesztmódban a NAV tesztrendszerét, egyébként az éles rendszert. A kulcsok a szerveren maradnak. Eladó csak a pénztári vevőfelvételt és ezt a lekérdezést végezheti; általános partnerírási/adminjogot nem kap. Megfelelő eladási, partnerírási és NAV-modullicenc szükséges. Kézi vevőfelvétel adószám nélkül is használható, névvel és teljes címmel.

**A számlaigény, a fizetési mód és a vevő eladáskori neve/adószáma/címe mentődik. Ez még nem adóügyi számla kiállítása vagy NAV-számlabeküldés.** A vevő későbbi átnevezése nem írja át a korábbi eladás számlázási adatait.

## Megbízhatóság és ellenőrzés

A keresés rövid késleltetéssel indul; korábbi vagy más céghez/raktárhoz tartozó késői válasz nem írhatja felül az új találatokat. A kosármezők validáltak, hibás szám esetén nem véglegesíthető. Bizonytalan küldési eredménynél a kosár zárolva marad. Az eredményellenőrző gomb előbb azonosító alapján olvassa vissza az eladást, és ha már mentve van, nem küldi be újra. Ha még nem létezik, ugyanazt a műveletet, fizetési módot és tételeket próbálja újra. A cégváltás/kijelentkezés nem dobhat el függő eladást; lejárt munkamenet után az eredeti felhasználó vagy céges admin oldhatja fel. Régi háttérszolgáltatás vagy pénztári metaadatokat nem ismerő eladásmodul nem fogadhat csendben hiányos mentést. Korábbi bizonylatok fizetési módját nem találjuk ki utólag.

168/168 Core/üzleti/valódi Host teszt és 39/39 WPF-render három témában sikeres. Célzott esetek: ékezet nélküli és pontos vonalkódos keresés; késői találat; vonalkód és Enter egyszeri hozzáadása; közvetlen szerkesztés és törlés; bizonytalan küldés változatlan újrapróbálása és mentett eladás visszaolvasása; fizetési mód és számlaigény; vevőadat-pillanatkép; régi eladások újrapróbálása; eladói korlátozott vevőfelvétel; adószám szerinti duplikációvédelem; NAV SHA3-512 tesztvektorok, aláírt kérés, válaszellenőrzés és XML-külső entitások tiltása. A NAV-hívás automatizált ellenőrzése szimulált HTTP-válasszal történt; éles NAV-kulcsokkal végzett szolgáltatási próba még szükséges. Valós eladás nem került rögzítésre a felhasználó adatbázisában.

NAV-protokoll elsődleges forrásai: [queryTaxpayer kérésminta](https://github.com/nav-gov-hu/Online-Invoice/blob/master/sample/API%20sample/queryTaxpayer.xml) és [NAV WADL/séma](https://github.com/nav-gov-hu/Online-Invoice/blob/master/sample/WADL/application.wadl).

## 0.4.8 – kerekítés és tizedesjegyek

Készpénzzel a teljes HUF végösszeget a legközelebbi 5 Ft-ra kerekítjük: a 2,50 és 7,50 Ft-os határtól felfelé. A tételek nettó/áfa/bruttó értékei nem változnak. A bizonylat külön tárolja a kerekítési eltérést és a fizetendőt, ezek CSV-exportban és részletezéskor is látszanak. Kártyán nincs 5 Ft-os kerekítés. Régi bizonylatokat nem módosítunk utólag. [2008. évi III. törvény](https://njt.jog.gov.hu/jogszabaly/2008-3-00-00).

A Beállítások / Pénzösszegek megjelenítése alatt 0–4 tizedesjegy választható (alap: 0). Helyi munkaállomás-beállítás, megmarad újraindítás után. A pénztári árak, összegek, eladási előzmények és bevételezési összesítők követik; a szerkesztéskor a pontos érték látszik, formázás nem írja át az árat. A kerekítési eltérést mindig két tizedessel mutatjuk, hogy a különbség pontosan ellenőrizhető legyen.

A NAV-mentésnél jelentkező NotSupportedException oka a modulbetöltő natív könyvtártiltása volt, nem a NAV hitelesítése. Csak a Windows rendszermappából betöltött crypt32.dll és kernel32.dll kapott kivételt a DPAPI titkosításhoz. A titkok továbbra is titkosítva tárolódnak; teszteléshez szintetikus hozzáférések készültek. A valódi cég jelszavát/kulcsait nem olvastuk ki vagy módosítottuk. A mentés önmagában nem ellenőrzi a NAV-hozzáférés érvényességét.
## 0.4.9 – eladási előzmények és részleges visszavét

Az előzményekben az Eladás napja mező egy budapesti naptári napra szűr; Minden nap megszünteti a szűrést. A CSV-export ugyanazt a betöltött napot és keresést követi. A bizonylat kiválasztásakor külön Tételek gomb nélkül megjelenik a részletezés és a soronként még visszavehető mennyiség.

Válassz tételt, majd Kijelölt tétel visszavétele a kosárba. Egy darab (tört mennyiségnél legfeljebb a maradék) negatív előjellel kerül a kosárba. A mennyiséget negatív számként átírhatod; az eredeti ár és áfa nem módosítható. Több eredeti sor is hozzáadható, egy kosárban egy bizonylatról. Eladás és visszavét külön művelet. A kosársor törölhető.

A Visszavét véglegesítése után választhatsz készpénzes vagy kártyás visszatérítést. A negatív fizetendő a vevőnek visszajáró összeg, készpénznél 5 Ft-ra kerekítve. A készlet az eredeti raktárban nő. Külön VV bizonylat készül, az eredeti eladás megmarad; a korábban visszavett mennyiségnél többet a háttérszolgáltatás sem enged. Részletekben történő visszavét összesített nettója/áfája fillérre egyezik az eredeti sorral. Ez továbbra sem adóügyi helyesbítő számla vagy terminálos visszatérítés vezérlése.

A régi teljes visszafordítás gomb helyét a tételes visszavét vette át. Részleges visszavét után a teljes reverse parancs tiltott, hogy ne kerülhessen kétszer vissza készletre ugyanaz az áru. Az eredeti, még érintetlen régi bizonylatok is visszavehetők. A negatív VV bizonylat újabb visszavétele nem engedett.