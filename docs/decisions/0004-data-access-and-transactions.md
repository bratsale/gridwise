# ADR 0004: Pristup podacima i transakcije u Catalog servisu

## Kontekst

Izmjena plana upisuje verziju, razrede, događaj u outbox i idempotency
ključ. Ti upisi moraju uspjeti ili propasti zajedno, i pri padu procesa
ili konekcije.

## Odluka

- EF Core (Npgsql), servis koristi DbContext direktno, bez repository sloja.
- Jedna operacija = jedan SaveChanges = jedna transakcija.
- Prolazne greške konekcije se ponavljaju; ponovni upis pada na jedinstveni ključ (23505) i obrađuje se kao sukob ili već obrađen zahtjev.
- Migracije se primjenjuju pri startu servisa.

## Zašto

- DbContext je već unit of work, a DbSet repository.
- Atomičnost outboxa je jedan poziv, pa je teško pogriješiti.
- Idempotentni upisi čine ponovni pokušaj bezopasnim.

## Alternative

- Repository po agregatu: bolja izolacija, ima smisla kad domen naraste. Odbačeno za sada.
- Generički repository: ponavlja DbContext i skriva mogućnosti EF-a. Odbačeno.
- Dapper i ručni SQL: brže, ali ručno mapiranje; brzina ovdje nije kritična. Odbačeno.

## Posljedice

- Servis je vezan za EF Core.
- Logika servisa se testira integraciono, protiv pravog PostgreSQL-a.
- Skalabilnost ne zavisi od ove odluke: Catalog piše samo administrator, a masovni upisi (batch) rješavaju se serijama i indeksima.