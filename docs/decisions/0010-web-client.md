# ADR 0010: Web klijent i upravljanje stanjem

## Kontekst

Klijent služi kupcu (preporuka i prihvatanje) i administratoru (planovi i poreske grupe). Ne računa novac, nego prikazuje ono što vrate servisi. API oba servisa je opisan OpenAPI dokumentom.

## Odluka

- Vite + React + TypeScript, organizovano po funkcionalnosti (features/customer, features/admin/...), a unutar svake: api (pozivi), hooks (podaci i stanje) i components (samo prikaz).
- Stanje je lokalno: useState u hookovima po funkcionalnosti, bez globalne biblioteke (Redux, Zustand) i bez biblioteke za keširanje (TanStack Query). Globalni su samo jezik i tema, preko React Contexta.
- Optimistične izmjene u administraciji: lista odmah pokaže očekivani rezultat, a pri grešci se vraća tačan snimak prethodnog stanja.
- Tipovi API-ja se generišu iz OpenAPI dokumenta i čuvaju u repozitoriju.
- Dva jezika (rječnik bez biblioteke, tipiziran), svijetla i tamna tema, stranice se učitavaju po potrebi (React.lazy).

## Zašto

- Podaci jedne stranice ne trebaju drugoj: lista planova i preporuka nemaju zajedničko stanje, pa globalni store ne bi imao šta da dijeli.
- Izvor istine je server; klijent drži samo ono što je trenutno na ekranu i osvježava poslije izmjene.
- Generisani tipovi pretvaraju promjenu API-ja u grešku pri kompajliranju.
- Komponente bez poziva prema serveru se lako mijenjaju i testiraju.

## Alternative

- Redux ili Zustand: korisni kad više dijelova aplikacije dijeli i mijenja isto stanje; ovdje toga nema. Odbačeno.
- TanStack Query: keš, ponovni pokušaji i optimistične izmjene gotovi; prvi izbor kad aplikacija naraste. Za dvije funkcionalnosti mali hook je dovoljan i pokazuje mehanizam. Odbačeno za sada.
- Ručno pisani tipovi: razilaze se sa API-jem. Odbačeno.
- Next.js: renderovanje na serveru ovdje ne donosi ništa. Odbačeno.

## Posljedice

- Poslije promjene API-ja treba ponovo generisati tipove (npm run gen:api).
- Kad više stranica počne dijeliti podatke, prelazak na TanStack Query je sljedeći korak.
- Poruke koje dolaze sa servera prikazuju se na engleskom.