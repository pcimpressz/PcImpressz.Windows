# Céges belépés és jogosultságok – 0.4.5

## Első indítás és napi használat

1. Indítsd újra a programot a Start.cmd segítségével, és válassz céget. Az üzleti képernyők használatához céges bejelentkezés szükséges.
2. Korábbi adminfiókkal a meglévő felhasználónév és jelszó használható. Ha a kiválasztott cégnek még nincs fiókja, az első admin létrehozása jelenik meg. Nincs előre beállított jelszó. Az első admin az adott helyi Windows-tulajdonos felügyelete mellett hozható létre.
3. Adminbelépés után az oldalsáv **Felhasználók és jogosultságok** gombjával hozhatók létre és módosíthatók a dolgozói fiókok. A felhasználónév 3–40 ASCII betű/szám/pont/kötőjel/aláhúzás, a jelszó 12–200 karakter. Új jelszót kétszer kell megadni.
4. Meglévő fiók kiválasztásakor módosítható a szerepkör, a tiltás és szükség esetén a jelszó. Üres jelszómezőnél a korábbi jelszó megmarad. Módosítás után a felhasználó minden korábbi céges munkamenete érvénytelenné válik. Az utolsó aktív admin szerepköre nem vonható vissza és nem tiltható le.
5. A **Jelszócsere** a saját jelenlegi jelszót is kéri. Siker után minden saját céges munkamenet megszűnik. A **Kijelentkezés** és a cégváltás előtt a program rákérdez a nem mentett változtatások elvetésére.

A munkamenet 30 percig érvényes; újraindításkor vagy visszavonáskor ismét be kell jelentkezni. A token csak memóriában van. Lejáratkor az üzleti nézetet belépőképernyő takarja; ugyanabba a cégbe ugyanazzal a felhasználóval történő újbóli belépés megőrzi a még nyitott piszkozatot. Más felhasználó belépése vagy cégváltás nem viszi tovább a másik felhasználó adatait. A felület a munkamenet állapotát négy másodpercenként ellenőrzi, a háttérszolgáltatás minden kérésnél.

## Szerepkörök

- **Adminisztrátor:** teljes üzleti hozzáférés; készletkorrekció és nyitókészlet; felületszerkesztés; felhasználókezelés; NAV-beállítás; cégmentés és visszaállítás; modulkapcsolók. A cégjegyzék és a modulkapcsolók telepítésszintűek, ezért bármely cég adminja kezelheti őket. Cégmentéshez a mentendő cég adminjának kell lenni.
- **Raktáros:** partneradatok megtekintése, termék/kategória/mértékegység szerkesztése és exportja, készlet megtekintése/exportja, bevételezés és visszafordítása/importja/exportja. Kézi készletkorrekciót és eladást nem végezhet; NAV-kapcsolatot nem módosíthat.
- **Eladó:** partner- és termékadatok, készlet megtekintése; eladás és visszafordítás, eladási export. Törzsadatot, bevételezést vagy készletkorrekciót nem írhat.
- **Megtekintő:** partner- és terméklista, készlet megtekintése. A korábbi `user` fiókok ezt a szerepet kapják, nem teljes üzleti hozzáférést.

A partnerdosszié több üzleti modul történetét tartalmazza, ezért ebben a változatban adminhoz kötött. Általános partnerkezelés adminfeladat. A 0.4.7-től az eladó a pénztárban korlátozott vevőfelvételt és NAV-adószámlekérdezést használhat; ez nem ad általános partnerírási jogot. A demóadat-törlés kizárólag a bejelentkezéshez tartozó kiválasztott demócégre terjed ki.

## Ellenőrzés és korlátok

A háttérszolgáltatás a saját tárolójából állapítja meg a szerepet; a kérésben küldött szerepkör, jogosultságlista vagy szereplő neve nem ad jogot. Minden üzleti olvasás, export és írás céghez/Windows SID-hez kötött munkamenetet igényel; a licencellenőrzés továbbra is megmarad. Az üzleti napló szereplője a Windows SID és a bejelentkezett felhasználónév együttese. A jelszavak sózott PBKDF2-lenyomatai az elkülönített helyi security adatbázisban maradnak. A jelszó- és fiókmódosítások naplózottak, a jelszóértékek nem.

Ez helyi Windows-alkalmazás: a névcső azonos Windows-felhasználóra korlátozott. Külön Windows-fiókos vagy hálózati többgépes használathoz további kiszolgálói fejlesztés szükséges. Az alkalmazásjogok nem akadályozzák meg a Windows-adatmappához hozzáférő helyi tulajdonost a fájlok közvetlen kezelésében.

158/158 automatizált Core/üzleti/Host teszt és 30/30 WPF-render három témában sikeres. Próbák: hitelesítés nélküli és más céges kérés; hamis szerepkör; eladó bevételezési/készletkorrekciós/admin kéréseinek elutasítása; raktáros eladásának és NAV-módosításának elutasítása; fióktiltás és munkamenet-visszavonás; utolsó admin védelme; jelszócsere; szerepkörfüggő termékszerkesztő.

A 2026-10-21-i helyi mentésben egy demócég, egy partner és nulla termék volt. A mentést új cégként, elkülönített tesztkönyvtárba állítottuk vissza; a hiányzó próba-termék kizárólag ebben a másolatban készült. Sikeres a raktáros bevételezése → eladó eladása → készlethiány miatt elutasított bevételezés-visszafordítás → eladás-visszafordítás → bevételezés-visszafordítás folyamat. Az eredeti adatbázis SHA-256 lenyomata változatlan. Ez másolati technikai próba, a valódi céges adatokkal végzett felhasználói átvételt nem helyettesíti.

A saját éles cég előkészítése után a kezelővel közös következő próba: saját mértékegység és termék, valós szállító, bevételezés, készletellenőrzés, eladás, szükség esetén visszafordítás, végül ellenőrzött cégmentés. A bizonylatok továbbra sem adóügyi számlák; élő NAV-lekérés és PDF-feldolgozás nincs beépítve.
