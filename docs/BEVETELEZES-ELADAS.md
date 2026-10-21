# Bevételezés és eladás – 0.4.4

A bevételezés lezárása és az eladás mentése a készletmozgással együtt, SQLite-tranzakcióban történik. Ugyanaz a kérés nem könyvel kétszer. Eladáskor a háttérszolgáltatás ellenőrzi a rendelkezésre álló készletet.

## Használat

- Bevételezés: válaszd ki a szállítót, raktárt és termékeket; ellenőrizd a mennyiségeket, majd véglegesítsd. Excel/CSV/XML import és NAV XML betöltése elérhető. Élő NAV-lekérés és PDF-feldolgozás nincs beépítve.
- Lezárt bevételezés: indoklással visszafordítható. Ha közben eladták a készletet és negatív egyenleg keletkezne, a teljes visszafordítás elutasításra kerül. Régi, készletkönyvelés nélküli minősítés nem fordítható vissza automatikusan. A visszafordított bizonylat és előzménye megmarad.
- Eladás: keresés névvel, saját cikkszámmal, vonalkóddal vagy gyártói cikkszámmal. A kiválasztott raktár elérhető készlete látszik; a kosárban ugyanazon termék több sorát együtt ellenőrizzük. A kijelölt sor szerkeszthető és lecserélhető. Mentés és visszafordítás után a készletadat frissül. A képernyő készlete pillanatfelvétel, a végleges ellenőrzést a háttérszolgáltatás végzi.
- A listák exportja az utoljára alkalmazott keresést követi. Az eladási bizonylat helyi üzleti nyilvántartás, nem NAV-számlázás.

## Ellenőrzés és következő lépés

155/155 automatizált Core/üzleti/Host teszt, 21/21 WPF-render három témában, adatkapcsolási hiba nélkül. Célzott eset: bevételezés → részleges eladás → tiltott bevételezés-visszafordítás → eladás-visszafordítás → bevételezés-visszafordítás → ismételt kérés változatlan készlettel.

A teljes napi átvételhez valós céges adatokkal végzett felhasználói próba szükséges. A 0.4.5-ben elkészült a központi alkalmazásbelépés és az admin/raktáros/eladó/megtekintő szerepkörök háttérszolgáltatási ellenőrzése. A helyi demócég mentésének másolatán sikeres a raktáros–eladó munkafolyamat; a forrásban nulla termék és egy partner volt, ezért a próba-termék csak a másolatban készült. 158/158 teszt és 30/30 WPF-render sikeres. Az eredeti adatbázis lenyomata változatlan. Részletek: BELEPES-ES-JOGOSULTSAGOK.md. Következő üzleti fejlesztések: eladási piszkozatok és árazás, majd nagykerenkénti rendelési kosarak.