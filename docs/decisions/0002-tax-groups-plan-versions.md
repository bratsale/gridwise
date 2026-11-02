# ADR 0002: Verzionisanje planova i poreskih grupa i istovremena izmjena

## Kontekst

Planovi i poreske grupe se mijenjaju kroz vrijeme. Promjena važi od zadatog datuma i ne smije promijeniti objašnjenje ranijih preporuka. Brisanje plana ga povlači. Dva administratora koji istovremeno mijenjaju isti plan ne smiju prepisati jedan drugog. Recommendation servis drži kopiju podataka iz događaja, a događaji mogu stići više puta i van redoslijeda.

## Odluka

1. Identitet i verzije su odvojeni. Tabela `plans` čuva samo id (uuid).
Sve što se mijenja (naziv, popust, razredi, status) je u `plan_versions`, razredi u `plan_tiers`. Poreske grupe isto: `tax_groups` (ključ `code`, npr. household) i `tax_group_versions` (PDV, eko stopa).
2. Verzije su nepromjenjive. Izmjena je INSERT nove verzije sa
version_no = prethodni + 1. Nema UPDATE ni DELETE nad verzijama.
3. Povlačenje je nova verzija sa statusom `retired`, bez razreda, sa vlastitim effective_from. Povučen plan je konačno stanje: nova izmjena vraća 409. Isto važi za poreske grupe.
4. Važeća verzija za datum D je ona sa najvećim effective_from <= D; kod
istog datuma pobjeđuje veći version_no. Ako važeća verzija ima status
`retired`, ili važeće verzije nema, plan se ne nudi.
5. Datumi su tipa `date` i tumače se u UTC (dosljedno ADR 0001). Kroz API effective_from mora biti >= danas i >= effective_from prethodne verzije. Početni podaci ulaze kroz migraciju i ne podliježu tom pravilu.
6. Istovremena izmjena (optimistično zaključavanje): klijent šalje version_no na kojem je zasnovao izmjenu (zaglavlje If-Match). Ako to nije trenutna verzija, odgovor je 409. Ako dva zahtjeva istovremeno prođu provjeru, drugi INSERT pada na primarnom ključu (plan_id, version_no) i odgovor je takođe 409. Posljednja odbrana je ograničenje u bazi, ne provjera u kodu.
7. Svaki događaj nosi cijelu verziju i njen version_no. Potrošač upisuje samo verziju koju još nema (po ključu id + version_no), a duplikat ili stariju verziju ignoriše. Potrošač čuva sve verzije, uključujući buduće, i bira važeću na dan zahtjeva.

## Zašto

- Preporuka pamti (plan_id, version_no) i (code, version_no). Pošto se verzije ne mijenjaju, svaki raniji račun se može ponoviti.
- Jedan monoton broj po planu pokriva tri zahtjeva: istovremenu izmjenu,
  redoslijed događaja i povlačenje od datuma.
- Primarni ključ garantuje jedinstvenost i pod trkom, bez zaključavanja reda i bez dodatne kolone.
- Događaj sa cijelom verzijom je samodovoljan: potrošač ne mora znati
  prethodno stanje niti zvati Catalog.

## Alternative

- UPDATE na mjestu uz tabelu istorije: trenutno stanje je primarno, istorija sporedna, pa reproducibilnost zavisi od toga da se istorija uvijek ispravno upiše. Odbačeno.
- Povlačenje kao kolona `retired_at` uz brojač `revision` na planu: dva broja koja potrošač mora pratiti. Odbačeno.
- Pesimističko zaključavanje (SELECT ... FOR UPDATE): zaključavanje traje koliko i transakcija, a izgubljena izmjena nastaje između dva HTTP zahtjeva (admin učita, pa kasnije sačuva). Ne rješava problem. Odbačeno.
- Razredi kao jsonb u verziji: jednostavniji upis, ali bez ograničenja i
  provjera u bazi. Odbačeno.
- Kodovi 412 (If-Match ne odgovara) i 428 (nedostaje If-Match): semantički precizniji, ali jedan kod za sukob je jednostavniji za klijenta. Odbačeno.

## Mane

- Verzija `retired` nema razreda: izuzetak koji validacija i potrošač moraju poznavati.
- Tabele rastu sa svakom izmjenom. Za ovaj obim zanemarljivo.
- Greška u budućoj verziji ispravlja se novom verzijom; istorija prikazuje i pogrešnu.
- Povlačenje se ne može poništiti; ponovno uvođenje je novi plan.
- Administrator koji dobije 409 mora ponovo učitati plan i ponoviti izmjenu.