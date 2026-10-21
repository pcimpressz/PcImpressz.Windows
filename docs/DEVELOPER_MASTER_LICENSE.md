# Fejlesztői mesterlicenc (külön fejlesztési változat)

A `tools/PcImpressz.DevelopmentIssuer` ES256-aláírt fejlesztői jogosultságot és lease-t bocsát ki 2099. december 31-i lejárattal. A Core kizárólag a `pcimpressz-development` kibocsátó és `development` szervezet, valamint `development-` licencazonosító együttes teljesülése esetén fogad el hét napnál hosszabb lease-t. A normál kiadói licenceknél megmarad a 7 napos korlát. A jogosultság továbbra is aláírás-, telepítés-, modul- és képességellenőrzött.

**Fontos:** A fejlesztői kiadó privát kulcsát (`.issuer`) soha ne terjeszd a telepítővel, és a fejlesztői publikus kulcsot ne telepítsd ügyfélkörnyezetbe. A fejlesztői licencet a projekt meglévő fejlesztői indítási folyamatával kell kiállítani. A 2099-es végdátum gyakorlati, nem matematikai végtelenség.

A módosítás forráskód-ellenőrzött, de .NET 10 fordítás és teszt Windows-környezetben még szükséges.
