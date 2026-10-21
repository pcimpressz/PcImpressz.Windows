# Adminisztrátori felirat- és elrendezésszerkesztő – 0.4.3

## Használat

1. Válaszd ki a céget, majd nyisd meg a Terméktörzset. A **Feliratok és elrendezés – admin** gomb a termék-munkaterület tetején található. Az általános törzsadatlapokon, például a partnereknél, az adatlap műveletei között is elérhető.
2. Az adott cég első használatakor hozd létre az első adminfiókot. A felhasználónév 3–40 ASCII betű/szám/pont/kötőjel/aláhúzás; a jelszó legalább 12 és legfeljebb 200 karakter. A jelszót kétszer kell megadni. Nincs beépített adminjelszó vagy mesterjelszó.
3. Később a céghez tartozó admin felhasználónévvel és jelszóval jelentkezz be. A normál `user` szerep nem nyithat szerkesztőt és közvetlen Host-kéréssel sem menthet vagy állíthat vissza elrendezést.
4. A bal oldali mezőlistában húzd a mezőt a kívánt helyre, vagy használd a **Fel/Le** gombokat. A kijelölt mező felirata átírható, szélessége fél vagy teljes sorra állítható. Nem kötelező mező elrejthető. A kötelező mezők megmaradnak. Jobbra azonnali előnézet látszik; a termék üzleti adatai ettől nem változnak.
5. A **Mentés** céghez és adatlaphoz kötötten rögzíti a nézetet. Az aktuális adatlap azonnal átveszi, a beírt termék-/partnerértékeket megőrzi. Másik nyitott alkalmazáspéldány cégváltáskor vagy újraindításkor tölti be az új nézetet.
6. Az **Alapértelmezett visszaállítása** és **Előző elrendezés visszaállítása** új, auditált változatot ment. Az utolsó 10 előző változat megmarad. Két egymásra nyitott szerkesztő közül az elavult mentése elutasításra kerül; az ablakot újra kell nyitni.
7. A **Felhasználó létrehozása** gombbal az admin új `user` vagy `admin` szerepű fiókot vehet fel az adott céghez. Más céghez külön fiók tartozik. A szerkesztő bezárásakor a munkamenet kijelentkezik; legfeljebb 30 perc után egyébként is lejár.

## Mit szerkeszt az első változat?

Az általános törzsadatlapok meglévő mezőinek feliratát, sorrendjét, fél-/teljes soros szélességét és a nem kötelező mezők láthatóságát. A belső mezőazonosító, mezőtípus, kötelezőség, üzleti validálás, licenc és műveleti jogosultság nem változtatható. A listafejlécek, teljes menürendszer, tetszőleges panelek és bizonylatszerkesztők átalakítása nem része ennek az első változatnak. Formázott termékleírás-szerkesztés nincs benne.

A terméklista, a termékadatlap és a készlet továbbra is külön belső fül. Az új/szerkesztési munkafolyamat és a teljes szélességű lista a 0.4.2 szerint működik.

## Jogosultság és tárolás

A Host ellenőrzi a felhasználót és az adminszerepet minden elrendezésmentéskor/visszaállításkor/fióklétrehozáskor. A véletlenszerű, 30 perces munkamenet céghez és a megbízható IPC-hívó Windows SID-jéhez kötött. A kérésben küldött `Role` vagy `Actor` mező nem ad jogosultságot. A jelszó 16 bájtos véletlen sóval, PBKDF2-SHA256, 210 000 iterációval, 32 bájtos lenyomatként tárolódik. Öt hibás próbálkozás után egyperces tiltás következik. Host-újraindításkor minden munkamenet megszűnik.

Az elrendezés és az előtte/utána auditesemény a céges SQLite-adatbázisban, egy tranzakcióban mentődik. A tárolt beállítás strukturált JSON, nem futtatható XAML vagy kód. Ismeretlen mező, duplikált mező, hibás szélesség, elrejtett kötelező mező vagy túl hosszú felirat nem menthető. Az újabb elrendezésséma alapnézettel jelenik meg és nem írható felül régi programmal.

A fióklenyomatok külön, helyi `.local/security/<cégazonosító>/company.db` adatbázisban vannak. A hordozható cégmentés az elrendezést viszi, a fiókokat és jelszólenyomatokat nem. Új cégként visszaállított mentéshez új első admint kell létrehozni. A teljes helyi biztonsági mentés részeként a security mappát is külön, bizalmasan kell kezelni.

Ez a bejelentkezés jelenleg a felületszerkesztést és fióklétrehozást védi. A napi üzleti műveletek továbbra is a helyi Windows-tulajdonosi pilot módot használják; a raktáros/eladó/beszerző szerepkörök és általános alkalmazásbelépés további fejlesztés. Az adatkönyvtár feletti Windows-hozzáféréssel rendelkező helyi tulajdonossal szemben ez nem külön operációsrendszer-biztonsági határ.

## Hozzáférés megőrzése és nyitott feladatok

Őrizd meg az adminjelszót. Létrehozható második adminfiók. Offline adminjelszó-helyreállító út nincs beépítve; univerzális mesterjelszó sincs. Felhasználó tiltása, jelszócsere és teljes céges szerepkörkezelés későbbi külön csomag.

## Ellenőrzés

148/148 Core/üzleti/valódi Host-teszt; wheel-ok; 15/15 WPF-render mindhárom témában, adatkapcsolási hiba nélkül. Célzott próbák: normál szerep közvetlen IPC-s írási kísérlete és hamis adminmező; cég/SID/lejárat/kijelentkezés/újraindítás; jelszótárolás és próbálkozáskorlát; verzióütközés, alap-/előző nézet, kötelező mező; újabb séma és mentés/visszaállítás; előnézet/átrendezés és a szerkesztett üzleti adatok megőrzése. Teljes kézi drag-and-drop és különböző DPI-s átvétel még szükséges.
