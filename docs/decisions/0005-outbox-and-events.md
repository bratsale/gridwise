# ADR 0005: Outbox, Kafka topics i format događaja

## Kontekst

Catalog upisuje promjene i događaje u outbox u istoj transakciji (ADR 0004). Događaje treba prenijeti preko Kafke do Recommendationa bez gubitka, iako Kafka može biti nedostupna, a poruke mogu stići više puta. Recommendation mora moći napuniti praznu kopiju podataka, a format događaja mora se moći mijenjati bez lomljenja potrošača.

## Odluka

- Topics: catalog.plan-events (ključ = id plana), catalog.tax-group-events (ključ = code), recommendation.events, recommendation.dlq. DLQ je po potrošaču. Topici se prave pri startu, a automatsko kreiranje je isključeno.
- 3 particije po topicu. Isti ključ ide u istu particiju, pa verzije istog plana stižu redom. Redoslijed između različitih planova nije garantovan.
- Zajednički JSON omot: id, type, version, timestamp, correlationId, data. data je zaseban, verzionisan tip ugovora (npr. PlanVersionCreatedV1), a ne API odgovor.
- Evolucija formata: dodaju se samo opcionalna polja, a potrošač ignoriše nepoznata. Nekompatibilna promjena podiže version, i obje verzije se neko vrijeme podržavaju. Ugovorni test zaključava oblik.
- Catalog topics se ne brišu (retention bez isteka, postavljen na -1). Prazna kopija se puni čitanjem topica od početka.
- Publisher je pozadinski servis u Catalogu. Čita neposlate redove po vremenu nastanka, šalje sa acks=all i idempotentnim producerom, pa upisuje published_at. Ako Kafka ne radi, redovi čekaju. Poslani redovi stariji od 7 dana se brišu.
- Dostava je bar jednom. Potrošač prepoznaje duplikat po id iz omota.
- Klijent: Confluent.Kafka. Lokalno: apache/kafka, jedan čvor, KRaft.

## Zašto

- Ključ po agregatu daje redoslijed po planu bez globalnog redoslijeda.
- Potrošač upisuje verziju samo ako je nema, pa je ispravan i kad poruka
  stigne dvaput ili van redoslijeda. Redoslijed je dodatna sigurnost.
- Zaseban tip ugovora sprečava da promjena API-ja tiho promijeni događaj.
- Čitanje od početka puni samo onog potrošača koji to radi i ne utiče na
  ostale. Isti mehanizam služi i za ponovnu izgradnju izvještaja.
- Pun Kafka klijent pokazuje mehanizam koji se ocjenjuje.

## Alternative

- Jedan topic catalog.events: manje topica, ali manje pregledno. Odbačeno.
- Compaction: čuva samo zadnju poruku po ključu, a potrebne su sve verzije.
  Odbačeno.
- Avro ili Protobuf sa schema registryjem: stroži ugovor, ali dodatna
  infrastruktura i manje čitljivo. Odbačeno.
- Snapshot iz Cataloga preko HTTP-a pri startu: uvodi sinhronu zavisnost. Odbačeno.
- Ponovno objavljivanje svih verzija: zagadi zajednički topic za sve potrošače. Ostaje kao alat za oporavak ako se topic izgubi.
- CDC (Debezium): bez pollinga, ali dodatni servis. Odbačeno.
- MassTransit ili KafkaFlow: gotov outbox i retry, ali skrivaju mehanizam. Odbačeno.

## Posljedice

- Duplikati su mogući (pad poslije slanja, prije upisa published_at); potrošači moraju biti idempotentni.
- Publisher pretpostavlja jednu instancu servisa. Za više instanci potreban je SELECT ... FOR UPDATE SKIP LOCKED ili izbor lidera.
- Lokalno je replication factor 1 (jedan broker), pa nema otpornosti na pad brokera.
- Topic bez isteka raste; za ovaj obim događaja to je zanemarljivo.