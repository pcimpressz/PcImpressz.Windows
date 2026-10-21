# Adminisztrátori felületszerkesztő – következő fejlesztési csomag

## Elkészült: 0.4.2 elrendezés

A terméktörzs három belső fület használ: Terméklista, Termékadatlap, Készlet és mozgások. A lista és az adatlap külön-külön a rendelkezésre álló teljes szélességet kapja. Az Új termék gomb a külön adatlapot nyitja. Kijelölt rekord szerkesztése gombbal vagy a terméksor dupla kattintásával nyitható. A fülváltás nem üríti ki az adatlapot. Az új rekord indítása továbbra is megerősítést kér, ha mentetlen adatot kellene elvetni.

A terméknév és a többsoros leírás/paraméter/megjegyzés teljes sort kap. A saját és gyártói cikkszám két széles mezőben szerepel. A lista alapértelmezett terméknév-, sajátcikkszám- és gyártóicikkszám-oszlopa is szélesebb. A cella teljes értéke egérrel rámutatva megtekinthető.

## Tervezett, még nem megvalósított szerkesztő

A felhasználó pontosítása alapján a cél a mezők és panelek elrendezésének, méretének és a felületi feliratoknak a szerkesztése. A formázott termékleírás nem része ennek a munkacsomagnak.

1. **Alkalmazásszintű szerepkörök.** Jelenleg a Host ugyanazt a Windows-felhasználót helyi tulajdonosként kezeli. Még nincs admin/normál felhasználó belépés. Ezt először valódi, céges szerepkörrel kell kiegészíteni. A felületszerkesztéshez külön jogosultság kell, amelyet a Host minden mentéskor ellenőriz. A fejlesztői licenc, egy beállításkapcsoló vagy egy elrejtett gomb nem adminhitelesítés. Az első admin kijelöléséhez és helyreállításához külön, ellenőrzött folyamat szükséges.
2. **Elrendezés szerkesztése.** Csak admin látja a Szerkesztő módot. A meglévő mezők fogóponttal mozgathatók; állítható a sorrend, a csoport, a szélesség, az egy- vagy kéthasábos elrendezés és a felirat. Legyen billentyűzetes Fel/Le mozgatás is. Előnézet, Mentés, Mégse és Alapértelmezett visszaállítása szükséges.
3. **Korlátozott, verziózott beállítás.** A szerkesztő strukturált mezőazonosítókat és megengedett tulajdonságokat tárol a céges SQL-ben; nem tetszőleges XAML-t vagy futtatható kódot. A felirat nem módosíthat belső mezőazonosítót. Kötelező mező nem tehető használhatatlanná; az üzleti validálás és jogosultság nem szerkeszthető. A mentés verzióellenőrzött, adminhoz kötött auditbejegyzéssel és korábbi elrendezés-visszaállítással történik.
4. **Kiadási és használati próba.** Normál felhasználó közvetlen IPC-kéréssel sem menthet elrendezést. Hibás vagy régebbi sémájú beállításnál az alapnézet marad használható. Próbák: két admin ütköző módosítása, cégelkülönítés, kötelező mezők, hosszú magyar feliratok, kis kijelző, billentyűzet és 100/125/150% DPI.

Ez a dokumentum terv, nem kész adminisztrációs vagy drag-and-drop funkció. A 0.4.2-ben a szélesebb és különválasztott képernyők működnek; tetszőleges feliratszerkesztést és felületmódosítást még nem engedünk.
